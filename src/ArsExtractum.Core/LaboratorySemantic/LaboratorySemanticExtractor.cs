using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ArsExtractum.Core.Assembly;
using ArsExtractum.Core.Documents;
using ArsExtractum.Core.Pipeline;

namespace ArsExtractum.Core.LaboratorySemantic;

public sealed partial class LaboratorySemanticExtractor
{
    public const string CurrentSchemaVersion = "semantic-patient-batch/1.1";
    public const string CurrentRulesVersion = "laboratory-semantic-extraction-rules/1.0";
    private const string SegmentationRule = "catalog-anchor-segmentation/1.0";
    private readonly ReferenceLaboratoryCatalog _catalog;

    public static StageDescriptor Descriptor { get; } = new(
        StageIds.LaboratorySemanticExtraction,
        "Extração semântica laboratorial",
        "Reconhece exames e campos nos blocos canônicos, preservando episódio e proveniência.",
        CurrentSchemaVersion,
        [StageIds.PatientEpisodeAssembly]);

    public LaboratorySemanticExtractor(ReferenceLaboratoryCatalog? catalog = null) =>
        _catalog = catalog ?? ReferenceLaboratoryCatalog.LoadBuiltIn();

    public SemanticPatientBatch Extract(LaboratorySemanticExtractionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.PatientBatch.SchemaVersion != PatientEpisodeAssembler.CurrentSchemaVersion ||
            input.PatientBatch.RulesVersion != PatientEpisodeAssembler.CurrentRulesVersion)
        {
            throw new InvalidOperationException("Laboratory Semantic Extraction v1 exige PatientBatch 1.2 e suas regras congeladas.");
        }

        var notices = new List<LaboratorySemanticNotice>();
        var patients = input.PatientBatch.Patients.Select(patient => new SemanticPatient(
            patient.PatientKey,
            patient.Identity,
            patient.SourceDocuments,
            patient.Episodes.Select(episode => ExtractEpisode(patient.PatientKey, episode, notices)).ToArray())).ToArray();
        var episodeCoverages = patients.SelectMany(static patient => patient.Episodes)
            .Select(static episode => episode.Coverage).ToArray();
        var coverage = new LaboratorySemanticCoverage(
            patients.Length,
            episodeCoverages.Length,
            episodeCoverages.Sum(static item => item.CanonicalBlockCount),
            episodeCoverages.Sum(static item => item.CanonicalActiveLineCount),
            episodeCoverages.Sum(static item => item.OccurrenceCount),
            episodeCoverages.Sum(static item => item.OwnedActiveLineCount),
            episodeCoverages.Sum(static item => item.UnsupportedActiveLineCount),
            episodeCoverages.Sum(static item => item.MultiplyOwnedActiveLineCount),
            episodeCoverages.Sum(static item => item.RepresentationFailureCount),
            episodeCoverages.Sum(static item => item.KnownAnchorCount),
            episodeCoverages.Sum(static item => item.RecognizedAnchorCount));

        return new SemanticPatientBatch(
            CurrentSchemaVersion,
            CurrentRulesVersion,
            _catalog.Document.CatalogVersion,
            _catalog.Document.ReferenceCorpusId,
            patients,
            coverage,
            notices);
    }

    private SemanticEpisode ExtractEpisode(
        string patientKey,
        AssembledEpisode episode,
        List<LaboratorySemanticNotice> notices)
    {
        var candidates = new List<OccurrenceCandidate>();
        var unsupported = new List<UnsupportedLaboratoryContent>();
        foreach (var block in episode.ContentBlocks)
        {
            var anchors = FindAnchors(block);
            if (anchors.Count == 0)
            {
                if (candidates.Count > 0 && CanContinue(candidates[^1], block))
                {
                    // The frozen bundle contains one hemogram specimen line on the
                    // immediately following page. Context and physical continuity are mandatory.
                    candidates[^1].Add(block, 0, block.ActiveLines.Count);
                }
                else
                {
                    unsupported.Add(CreateUnsupported(episode, block, 0, block.ActiveLines.Count, "NoKnownAnchor"));
                }

                continue;
            }

            if (anchors[0].Index > 0)
            {
                if (candidates.Count > 0 && CanContinue(candidates[^1], block))
                {
                    candidates[^1].Add(block, 0, anchors[0].Index);
                }
                else
                {
                    unsupported.Add(CreateUnsupported(episode, block, 0, anchors[0].Index, "PrefixWithoutSafeContext"));
                }
            }

            for (var index = 0; index < anchors.Count; index++)
            {
                var start = anchors[index];
                var end = index + 1 < anchors.Count ? anchors[index + 1].Index : block.ActiveLines.Count;
                var candidate = new OccurrenceCandidate(start.Concept);
                candidate.Add(block, start.Index, end);
                candidates.Add(candidate);
            }
        }

        var occurrences = candidates.Select(candidate => BuildOccurrence(patientKey, episode, candidate)).ToArray();
        occurrences = BuildSafeMicrobiologyRelationships(occurrences);

        // Ownership is calculated independently of extraction fields. This makes a
        // dropped or multiply segmented line visible even when the text output looks plausible.
        var owners = new Dictionary<(string BlockId, string LineId), int>();
        foreach (var candidate in candidates)
        {
            foreach (var segment in candidate.Segments)
            {
                foreach (var line in segment.Lines)
                {
                    owners.TryGetValue((segment.Block.BlockId, line.Id), out var count);
                    owners[(segment.Block.BlockId, line.Id)] = count + 1;
                }
            }
        }

        var totalLines = episode.ContentBlocks.Sum(static block => block.ActiveLines.Count);
        var unsupportedLines = unsupported.Sum(static item => item.CanonicalLineIds.Count);
        var multiplyOwned = owners.Values.Count(static count => count > 1);
        var representationFailures = occurrences.Count(static item =>
            item.Status == LaboratoryRepresentationStatus.RepresentationFailure);
        var knownAnchors = candidates.Count;
        var episodeCoverage = new LaboratorySemanticEpisodeCoverage(
            episode.ContentBlocks.Count,
            totalLines,
            occurrences.Length,
            owners.Count,
            unsupportedLines,
            multiplyOwned,
            representationFailures,
            knownAnchors,
            knownAnchors);
        if (!episodeCoverage.IsLossless)
        {
            notices.Add(new LaboratorySemanticNotice(
                "semantic.coverage-not-lossless",
                "A cobertura semântica do episódio não reconciliou todas as linhas canônicas.",
                episode.EpisodeKey));
        }

        return new SemanticEpisode(episode.EpisodeKey, episode, occurrences, unsupported, episodeCoverage);
    }

    private List<Anchor> FindAnchors(CanonicalEpisodeContentBlock block)
    {
        var result = new List<Anchor>();
        ReferenceLaboratoryConcept? current = null;
        var currentIndex = -1;
        for (var index = 0; index < block.ActiveLines.Count; index++)
        {
            var line = block.ActiveLines[index];
            if (!_catalog.TryMatch(line.Text, out var concept))
            {
                continue;
            }

            var label = ReferenceLaboratoryCatalog.Normalize(ReferenceLaboratoryCatalog.LiteralLabel(line.Text));
            if (current is not null && IsObservedComponent(current, label))
            {
                continue;
            }

            if (current?.StructuralFormId == "microbiology-culture" && label == "CULTURAL")
            {
                continue;
            }

            if (current?.ConceptId == concept.ConceptId && currentIndex >= 0)
            {
                var intervening = block.ActiveLines.Skip(currentIndex).Take(index - currentIndex);
                var priorClosed = intervening.Any(static item =>
                    ReferenceLaboratoryCatalog.Normalize(ReferenceLaboratoryCatalog.LiteralLabel(item.Text)) == "MATERIAL");
                if (!priorClosed && !HasResult(block.ActiveLines[currentIndex].Text))
                {
                    continue;
                }
            }

            result.Add(new Anchor(index, concept));
            current = concept;
            currentIndex = index;
        }

        return result;
    }

    private static bool IsObservedComponent(ReferenceLaboratoryConcept concept, string label) =>
        concept.ObservedComponents.Any(component =>
            ReferenceLaboratoryCatalog.Normalize(component) == label);

    private static bool HasResult(string text) =>
        text.Contains(':', StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(text[(text.IndexOf(':', StringComparison.Ordinal) + 1)..]);

    private static bool CanContinue(OccurrenceCandidate previous, CanonicalEpisodeContentBlock next)
    {
        var priorSource = previous.Segments[^1].Block.Sources[0];
        var nextSource = next.Sources[0];
        return priorSource.DocumentId == nextSource.DocumentId &&
               priorSource.InputIndex == nextSource.InputIndex &&
               priorSource.PageNumber + 1 == nextSource.PageNumber &&
               (previous.Concept.StructuralFormId == "sectioned-panel" ||
                ReferenceLaboratoryCatalog.Normalize(
                    ReferenceLaboratoryCatalog.LiteralLabel(next.ActiveLines[0].Text)) == "MATERIAL");
    }

    private static LaboratoryOccurrence BuildOccurrence(
        string patientKey,
        AssembledEpisode episode,
        OccurrenceCandidate candidate)
    {
        var first = candidate.Segments[0];
        var evidence = candidate.Segments.SelectMany(segment => segment.Lines.Select((line, index) =>
            CreateEvidence(episode, segment.Block, line, segment.StartIndex + index, "sourceLine"))).ToArray();
        var occurrenceId = StableId(
            "occurrence",
            episode.EpisodeKey,
            candidate.Concept.ConceptId,
            string.Join('|', evidence.Select(static item => $"{item.BlockId}:{item.CanonicalLineId}")));
        var observations = new List<LaboratoryObservation>();
        var specimens = new List<LaboratorySpecimen>();
        var attributes = new List<LaboratoryAttributeValue>();
        var references = new List<LaboratoryReference>();
        var narratives = new List<LaboratoryNarrative>();
        var appliedRules = new HashSet<string>(StringComparer.Ordinal) { SegmentationRule, candidate.Concept.ExtractionStrategyId };

        for (var evidenceIndex = 0; evidenceIndex < evidence.Length; evidenceIndex++)
        {
            var item = evidence[evidenceIndex];
            var text = item.SanitizedText;
            var label = ReferenceLaboratoryCatalog.LiteralLabel(text);
            var separator = text.IndexOf(':', StringComparison.Ordinal);
            var rawAfterLabel = separator >= 0 ? text[(separator + 1)..].Trim() : string.Empty;
            var normalizedLabel = ReferenceLaboratoryCatalog.Normalize(label);
            // In the reference laboratory's TTPA layout, AMOSTRA is the measured
            // clotting time. This exception is deliberately concept-scoped.
            var isTtpaResult = candidate.Concept.ConceptId ==
                "fsph-nh.tempo-de-tromboplastina-parcial-ativada-ttpa" &&
                normalizedLabel == "AMOSTRA";
            var isSpecimen = normalizedLabel is "MATERIAL" or "AMOSTRA" && !isTtpaResult;
            if (isSpecimen)
            {
                specimens.Add(new LaboratorySpecimen(rawAfterLabel, item with { FieldPath = $"specimens[{specimens.Count}]" }));
            }
            else if (separator >= 0 && rawAfterLabel.Length > 0)
            {
                attributes.Add(new LaboratoryAttributeValue(
                    label,
                    rawAfterLabel,
                    item with { FieldPath = $"attributes[{attributes.Count}]" }));
            }

            // Numeric parsing is restricted to the documentary result segment. Digits
            // in assay names (ANTI-HIV 1 E 2) and organism markers (Germe 1) are labels.
            var resultSegment = ResultSegment(text, separator);
            var matches = isSpecimen
                ? Array.Empty<Match>()
                : NumericWithOptionalUnitRegex().Matches(resultSegment).Cast<Match>().ToArray();
            foreach (Match match in matches)
            {
                var rawValue = match.Groups["value"].Value.Trim();
                var rawUnit = match.Groups["unit"].Success ? match.Groups["unit"].Value.Trim() : null;
                observations.Add(new LaboratoryObservation(
                    StableId("observation", occurrenceId, item.CanonicalLineId, observations.Count.ToString(CultureInfo.InvariantCulture)),
                    label,
                    rawValue,
                    ParseDecimal(rawValue),
                    null,
                    rawUnit,
                    rawUnit,
                    item with { FieldPath = $"observations[{observations.Count}]" }));
            }

            if (matches.Length == 0 && rawAfterLabel.Length > 0 && !isSpecimen)
            {
                observations.Add(new LaboratoryObservation(
                    StableId("observation", occurrenceId, item.CanonicalLineId, observations.Count.ToString(CultureInfo.InvariantCulture)),
                    label,
                    rawAfterLabel,
                    null,
                    rawAfterLabel,
                    null,
                    null,
                    item with { FieldPath = $"observations[{observations.Count}]" }));
            }

            foreach (var suppressed in item.SuppressedSegments.Where(static segment =>
                         segment.Disposition == SanitizedDisposition.Reference))
            {
                references.Add(new LaboratoryReference(
                    suppressed.Text,
                    item with { FieldPath = $"references[{references.Count}]" }));
            }

            if (separator < 0 && matches.Length == 0 && evidenceIndex > 0)
            {
                narratives.Add(new LaboratoryNarrative(
                    text,
                    "UnstructuredNarrativePreserved",
                    item with { FieldPath = $"narratives[{narratives.Count}]" }));
            }

            foreach (var rule in item.AppliedRuleIds)
            {
                appliedRules.Add(rule);
            }
        }

        var microbiology = candidate.Concept.StructuralFormId is "microbiology-culture" or "susceptibility-panel"
            ? ExtractMicrobiology(occurrenceId, candidate, episode)
            : null;
        var status = narratives.Count > 0
            ? LaboratoryRepresentationStatus.StructuredWithResidual
            : LaboratoryRepresentationStatus.FullyStructured;
        var segments = candidate.Segments.Select(segment => new OccurrenceSourceSegment(
            segment.Block.BlockId,
            segment.Lines.Select(static line => line.Id).ToArray(),
            segment.Lines.SelectMany((line, index) =>
                CreateEvidence(episode, segment.Block, line, segment.StartIndex + index, "sourceLine").SourceAppearances)
                .ToArray())).ToArray();

        return new LaboratoryOccurrence(
            occurrenceId,
            episode.EpisodeKey,
            candidate.Concept.ConceptId,
            candidate.Concept.DisplayName,
            candidate.Concept.StructuralFormId,
            status,
            observations,
            specimens,
            attributes,
            references,
            narratives,
            [],
            microbiology,
            segments,
            evidence,
            appliedRules.Order(StringComparer.Ordinal).ToArray(),
            []);
    }

    private static LaboratoryMicrobiology ExtractMicrobiology(
        string occurrenceId,
        OccurrenceCandidate candidate,
        AssembledEpisode episode)
    {
        var located = candidate.Segments.SelectMany(segment => segment.Lines.Select((line, index) => new
        {
            Line = line,
            Evidence = CreateEvidence(episode, segment.Block, line, segment.StartIndex + index, "microbiology"),
        })).ToArray();
        var organisms = new List<LaboratoryOrganism>();
        var groups = new List<LaboratorySusceptibilityGroup>();

        if (candidate.Concept.StructuralFormId == "susceptibility-panel")
        {
            var markerIndex = Array.FindIndex(located, static item =>
                ReferenceLaboratoryCatalog.Normalize(item.Line.Text).StartsWith("GERME ", StringComparison.Ordinal));
            if (markerIndex >= 0 && markerIndex + 1 < located.Length)
            {
                var organismNames = located[markerIndex + 1].Line.Text.Split('|', StringSplitOptions.TrimEntries);
                foreach (var name in organismNames)
                {
                    var organismId = StableId("organism", occurrenceId, name);
                    organisms.Add(new LaboratoryOrganism(organismId, name,
                        located[markerIndex + 1].Evidence with { FieldPath = $"microbiology.organisms[{organisms.Count}]" }));
                }

                for (var column = 0; column < organismNames.Length; column++)
                {
                    var entries = new List<LaboratorySusceptibilityEntry>();
                    for (var index = markerIndex + 2; index < located.Length; index++)
                    {
                        var cells = located[index].Line.Text.Split('|', StringSplitOptions.TrimEntries);
                        if (column >= cells.Length || !TrySplitField(cells[column], out var antimicrobial, out var result))
                        {
                            continue;
                        }

                        entries.Add(new LaboratorySusceptibilityEntry(
                            antimicrobial,
                            result,
                            NormalizeSusceptibility(result),
                            located[index].Evidence with
                            {
                                FieldPath = $"microbiology.susceptibilityGroups[{column}].entries[{entries.Count}]",
                            }));
                    }

                    groups.Add(new LaboratorySusceptibilityGroup(
                        StableId("susceptibility-group", occurrenceId, column.ToString(CultureInfo.InvariantCulture)),
                        organisms[column].OrganismId,
                        organisms[column].RawName,
                        entries,
                        located[markerIndex].Evidence with { FieldPath = $"microbiology.susceptibilityGroups[{column}]" }));
                }
            }
        }
        else
        {
            foreach (var item in located)
            {
                if (!TrySplitField(item.Line.Text, out var label, out var value) ||
                    ReferenceLaboratoryCatalog.Normalize(label) != "CULTURAL" ||
                    string.IsNullOrWhiteSpace(value) ||
                    value.Equals("NEGATIVO", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var organismId = StableId("organism", occurrenceId, value);
                organisms.Add(new LaboratoryOrganism(
                    organismId,
                    value,
                    item.Evidence with { FieldPath = $"microbiology.organisms[{organisms.Count}]" }));
            }
        }

        return new LaboratoryMicrobiology(organisms, groups);
    }

    private static LaboratoryOccurrence[] BuildSafeMicrobiologyRelationships(LaboratoryOccurrence[] occurrences)
    {
        var cultures = occurrences.Where(static item => item.StructuralForm == "microbiology-culture").ToArray();
        for (var index = 0; index < occurrences.Length; index++)
        {
            var susceptibility = occurrences[index];
            if (susceptibility.StructuralForm != "susceptibility-panel" || susceptibility.Microbiology is null)
            {
                continue;
            }

            var susceptibilityNames = susceptibility.Microbiology.Organisms
                .Select(static item => ReferenceLaboratoryCatalog.Normalize(item.RawName)).ToHashSet(StringComparer.Ordinal);
            var matches = cultures.Where(culture => culture.Microbiology is not null &&
                culture.Microbiology.Organisms.Any(organism => susceptibilityNames.Contains(
                    ReferenceLaboratoryCatalog.Normalize(organism.RawName)))).ToArray();
            if (matches.Length != 1)
            {
                continue;
            }

            var evidence = susceptibility.FieldEvidence[0] with { FieldPath = "relationships" };
            var relationship = new LaboratoryRelationship(
                StableId("relationship", matches[0].OccurrenceId, susceptibility.OccurrenceId),
                "culture-has-susceptibility",
                matches[0].OccurrenceId,
                susceptibility.OccurrenceId,
                evidence);
            occurrences[index] = susceptibility with { Relationships = [.. susceptibility.Relationships, relationship] };
        }

        return occurrences;
    }

    private static UnsupportedLaboratoryContent CreateUnsupported(
        AssembledEpisode episode,
        CanonicalEpisodeContentBlock block,
        int start,
        int end,
        string reason) => new(
            reason,
            block.BlockId,
            block.ActiveLines.Skip(start).Take(end - start).Select(static line => line.Id).ToArray(),
            block.ActiveLines.Skip(start).Take(end - start)
                .Select((line, index) => CreateEvidence(episode, block, line, start + index, "unsupported"))
                .ToArray());

    private static SemanticFieldEvidence CreateEvidence(
        AssembledEpisode episode,
        CanonicalEpisodeContentBlock block,
        SanitizedLine canonicalLine,
        int lineIndex,
        string fieldPath)
    {
        var appearances = block.Sources.Select(source =>
        {
            var sourceLineId = source.LineIds[lineIndex];
            var sourceLine = episode.Pages.Single(page =>
                    page.DocumentId == source.DocumentId &&
                    page.InputIndex == source.InputIndex &&
                    page.PageNumber == source.PageNumber)
                .ActiveLines.Single(line => line.Id == sourceLineId);
            return new SemanticSourceAppearance(
                source.DocumentId,
                source.FileName,
                source.InputIndex,
                source.PageNumber,
                sourceLineId,
                sourceLine.SourceWordIds);
        }).ToArray();
        return new SemanticFieldEvidence(
            fieldPath,
            block.BlockId,
            canonicalLine.Id,
            canonicalLine.OriginalText,
            canonicalLine.Text,
            canonicalLine.SourceWordIds,
            canonicalLine.AppliedRuleIds,
            canonicalLine.SuppressedSegments,
            appearances);
    }

    private static bool TrySplitField(string text, out string label, out string value)
    {
        var separator = text.IndexOf(':', StringComparison.Ordinal);
        if (separator < 0)
        {
            label = string.Empty;
            value = string.Empty;
            return false;
        }

        label = text[..separator].Trim();
        value = text[(separator + 1)..].Trim();
        return true;
    }

    private static string ResultSegment(string text, int separator)
    {
        if (separator >= 0)
        {
            return text[(separator + 1)..];
        }

        var pipe = text.IndexOf('|', StringComparison.Ordinal);
        if (pipe >= 0 && StartsWithNumericResult(text[(pipe + 1)..]))
        {
            return text[(pipe + 1)..];
        }

        var trimmed = text.TrimStart();
        return StartsWithNumericResult(trimmed)
            ? trimmed
            : string.Empty;
    }

    private static bool StartsWithNumericResult(string value)
    {
        var trimmed = value.TrimStart();
        return trimmed.Length > 0 && (char.IsDigit(trimmed[0]) || trimmed[0] is '<' or '>' or '-');
    }

    private static string? NormalizeSusceptibility(string value)
    {
        var normalized = ReferenceLaboratoryCatalog.Normalize(value);
        return normalized is "SENSIVEL" or "RESISTENTE" or "INTERMEDIARIO" ? normalized : null;
    }

    private static decimal? ParseDecimal(string raw)
    {
        var normalized = raw.Trim().TrimStart('<', '>', '=').Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static string StableId(string prefix, params string[] parts)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f', parts)));
        return $"{prefix}-{Convert.ToHexString(bytes).ToLowerInvariant()[..16]}";
    }

    [GeneratedRegex(
        @"(?<value>(?:[<>]=?\s*)?-?\d+(?:\.\d{3})*(?:,\d+)?)(?:\s*(?<unit>mg\s*/\s*d[lL]|ng\s*/\s*[mM]?[lL]|pg\s*/\s*[mM][lL]|mEq\s*/\s*[lL]|mmol\s*/\s*[lL]|U\s*/\s*[lL]|UI\s*/\s*[mM][lL]|uUI\s*/\s*[mM][lL]|mUI\s*/\s*[mM][lL]|g\s*/\s*d[lL]|mg\s*/\s*[lL]|%|mmHg|segundos?|UFC\s*/\s*mL|mL/min/1,73m[²2]|mm[³3]|/mm[³3]|/uL|f[lL]|pg))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NumericWithOptionalUnitRegex();

    private sealed record Anchor(int Index, ReferenceLaboratoryConcept Concept);

    private sealed class OccurrenceCandidate(ReferenceLaboratoryConcept concept)
    {
        public ReferenceLaboratoryConcept Concept { get; } = concept;

        public List<OccurrenceSegment> Segments { get; } = [];

        public void Add(CanonicalEpisodeContentBlock block, int start, int end) =>
            Segments.Add(new OccurrenceSegment(block, start, block.ActiveLines.Skip(start).Take(end - start).ToArray()));
    }

    private sealed record OccurrenceSegment(
        CanonicalEpisodeContentBlock Block,
        int StartIndex,
        IReadOnlyList<SanitizedLine> Lines);
}
