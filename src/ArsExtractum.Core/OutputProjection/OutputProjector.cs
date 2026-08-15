using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ArsExtractum.Core.DerivedMeasurements;
using ArsExtractum.Core.LaboratorySemantic;
using ArsExtractum.Core.Pipeline;

namespace ArsExtractum.Core.OutputProjection;

public sealed class OutputProjector
{
    public const string CurrentSchemaVersion = "clinical-output-batch/1.2";
    public const string CurrentRulesVersion = "output-projection-rules/1.2";

    public static StageDescriptor Descriptor { get; } = new(
        StageIds.OutputProjection,
        "Projeção clínica final",
        "Ordena e formata as entidades laboratoriais em texto clínico copiável e editável.",
        CurrentSchemaVersion,
        [StageIds.DerivedMeasurementComputation]);

    public static ClinicalOutputBatch Project(OutputProjectionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var semantic = input.SemanticPatientBatch;
        if (semantic.SchemaVersion != LaboratorySemanticExtractor.CurrentSchemaVersion ||
            semantic.DerivedMeasurementRulesVersion != DerivedMeasurementComputer.CurrentRulesVersion ||
            semantic.DerivedMeasurementCoverage?.IsComplete != true)
        {
            throw new InvalidOperationException(
                "Output Projection v1 exige SemanticPatientBatch 1.1 enriquecido por CKD-EPI 2021 com coverage completo.");
        }

        var options = input.Options ?? new OutputProjectionOptions();
        var notices = new List<OutputProjectionNotice>();
        foreach (var patient in semantic.Patients.Where(static patient => patient.Episodes
                     .SelectMany(static episode => episode.LaboratoryOccurrences).Any(IsCultureOccurrence)))
        {
            var cultureOccurrences = patient.Episodes.SelectMany(static episode => episode.LaboratoryOccurrences)
                .Count(IsCultureOccurrence);
            var cultureEpisodes = patient.Episodes.Count(static episode =>
                episode.LaboratoryOccurrences.Any(IsCultureOccurrence));
            notices.Add(new OutputProjectionNotice(
                "output-projection.culture-verification-required",
                $"ATENÇÃO: foram detectados {cultureOccurrences} exame(s) de cultura/microbiologia em {cultureEpisodes} episódio(s). A extração desses resultados pode ser incompleta devido à variação documental. Confira os resultados no documento-fonte antes de utilização clínica.",
                patient.PatientKey));
        }
        var patients = semantic.Patients.Select(patient => new ClinicalOutputPatient(
            patient.PatientKey,
            patient.Identity,
            patient.Episodes
                .OrderByDescending(EpisodeDateTime)
                .ThenBy(static episode => episode.EpisodeKey, StringComparer.Ordinal)
                .Select(episode => ProjectEpisode(patient, episode, options, notices))
                .ToArray())).ToArray();
        var projected = patients.SelectMany(static patient => patient.Episodes)
            .SelectMany(static episode => episode.ProjectedOccurrences).ToArray();
        var sourceOccurrenceCount = semantic.Patients.SelectMany(static patient => patient.Episodes)
            .Sum(static episode => episode.LaboratoryOccurrences.Count);
        var fieldRecords = projected.SelectMany(static item => item.FieldProjectionRecords).ToArray();
        var coverage = new OutputProjectionCoverage(
            semantic.Patients.Count,
            patients.Length,
            semantic.Patients.Sum(static patient => patient.Episodes.Count),
            patients.Sum(static patient => patient.Episodes.Count),
            sourceOccurrenceCount,
            projected.Count(static item => item.Disposition == ProjectionDisposition.Projected),
            projected.Count(static item => item.Disposition == ProjectionDisposition.SuppressedByExplicitPolicy),
            projected.Count(static item => item.Disposition == ProjectionDisposition.SafeFallback),
            projected.Count(static item => item.Disposition == ProjectionDisposition.ProjectionFailure),
            Math.Max(0, sourceOccurrenceCount - projected.Length),
            fieldRecords.Length,
            fieldRecords.Count(static item => item.Disposition != FieldProjectionDisposition.ProjectionFailure),
            0,
            fieldRecords.Count(static item => item.Disposition == FieldProjectionDisposition.ProjectionFailure));
        if (!coverage.IsComplete)
        {
            notices.Add(new OutputProjectionNotice(
                "output-projection.coverage-not-complete",
                "A projeção não reconciliou todas as ocorrências semânticas."));
        }

        return new ClinicalOutputBatch(
            CurrentSchemaVersion,
            CurrentRulesVersion,
            semantic.SchemaVersion,
            options,
            patients,
            coverage,
            notices);
    }

    private static ClinicalOutputEpisode ProjectEpisode(
        SemanticPatient patient,
        SemanticEpisode episode,
        OutputProjectionOptions options,
        List<OutputProjectionNotice> notices)
    {
        var occurrences = episode.LaboratoryOccurrences;
        var projected = occurrences
            .Select((occurrence, documentaryOrder) => new
            {
                Occurrence = occurrence,
                DocumentaryOrder = documentaryOrder,
                Order = OutputProjectionRules.OrderOf(occurrence.ConceptId),
            })
            .OrderBy(static item => item.Order.Group)
            .ThenBy(static item => item.Order.Order)
            .ThenBy(static item => item.DocumentaryOrder)
            .Select(item => ProjectOccurrence(patient.PatientKey, episode.EpisodeKey, item.Occurrence,
                occurrences, options, notices))
            .ToArray();
        var text = ClinicalOutputTextFormatter.FormatEpisode(
            episode.DocumentaryEpisode.RequestDate,
            episode.DocumentaryEpisode.RequestTime,
            projected);
        return new ClinicalOutputEpisode(
            episode.EpisodeKey,
            episode.DocumentaryEpisode.RequestNumber,
            episode.DocumentaryEpisode.RequestDate,
            episode.DocumentaryEpisode.RequestTime,
            patient.SourceDocuments,
            projected,
            text);
    }

    private static ClinicalProjectedOccurrence ProjectOccurrence(
        string patientKey,
        string episodeKey,
        LaboratoryOccurrence occurrence,
        IReadOnlyList<LaboratoryOccurrence> episodeOccurrences,
        OutputProjectionOptions options,
        List<OutputProjectionNotice> notices)
    {
        try
        {
            if (!options.ShowCultures && IsCultureOccurrence(occurrence))
            {
                return new ClinicalProjectedOccurrence(
                    StableId("projection", CurrentRulesVersion, occurrence.OccurrenceId,
                        options.ShowUnits.ToString(), options.ShowCultures.ToString()),
                    occurrence.OccurrenceId, occurrence.ConceptId,
                    ProjectionDisposition.SuppressedByExplicitPolicy, [],
                    occurrence.Observations.Select(static item => item.ObservationId).ToArray(),
                    occurrence.DerivedObservations.Select(static item => item.DerivedObservationId).ToArray(),
                    BuildFieldRecords(occurrence, [], false, false));
            }

            var incomingRelationship = episodeOccurrences.SelectMany(static item => item.Relationships)
                .FirstOrDefault(relationship =>
                    relationship.Relation == "culture-has-susceptibility" &&
                    relationship.TargetId == occurrence.OccurrenceId);
            if (incomingRelationship is not null)
            {
                return new ClinicalProjectedOccurrence(
                    StableId("projection", CurrentRulesVersion, occurrence.OccurrenceId,
                        options.ShowUnits.ToString(), options.ShowCultures.ToString()),
                    occurrence.OccurrenceId, occurrence.ConceptId,
                    ProjectionDisposition.SuppressedByExplicitPolicy, [],
                    occurrence.Observations.Select(static item => item.ObservationId).ToArray(),
                    occurrence.DerivedObservations.Select(static item => item.DerivedObservationId).ToArray(),
                    BuildFieldRecords(occurrence, [], false, true));
            }

            if (occurrence.StructuralForm == "susceptibility-panel")
            {
                notices.Add(new OutputProjectionNotice(
                    "output-projection.unlinked-susceptibility-preserved",
                    "Antibiograma sem relação inequívoca com cultura; ocorrência preservada independentemente.",
                    patientKey, episodeKey, occurrence.OccurrenceId));
            }

            var linkedSusceptibilities = episodeOccurrences.SelectMany(static item => item.Relationships)
                .Where(relationship => relationship.Relation == "culture-has-susceptibility" &&
                    relationship.SourceId == occurrence.OccurrenceId)
                .Select(relationship => episodeOccurrences.SingleOrDefault(item =>
                    item.OccurrenceId == relationship.TargetId))
                .OfType<LaboratoryOccurrence>()
                .ToArray();
            var lines = RenderOccurrence(occurrence, linkedSusceptibilities, options);
            var disposition = lines.Count == 0
                ? ProjectionDisposition.SafeFallback
                : ProjectionDisposition.Projected;
            if (disposition == ProjectionDisposition.SafeFallback)
            {
                var residual = occurrence.Narratives.Select(static item => item.RawText)
                    .Concat(occurrence.FieldEvidence.Select(static item => item.SanitizedText))
                    .Where(static item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                lines = [$"Resultado não estruturado: {string.Join(" | ", residual)}"];
                notices.Add(new OutputProjectionNotice(
                    "output-projection.safe-fallback",
                    "A ocorrência não possuía representação editorial segura e foi preservada como fallback.",
                    patientKey, episodeKey, occurrence.OccurrenceId));
            }

            return new ClinicalProjectedOccurrence(
                StableId("projection", CurrentRulesVersion, occurrence.OccurrenceId,
                    options.ShowUnits.ToString(), options.ShowCultures.ToString()),
                occurrence.OccurrenceId,
                occurrence.ConceptId,
                disposition,
                lines,
                occurrence.Observations.Select(static item => item.ObservationId).ToArray(),
                occurrence.DerivedObservations.Select(static item => item.DerivedObservationId).ToArray(),
                BuildFieldRecords(occurrence, lines, true, options.ShowCultures));
        }
        catch (Exception exception)
        {
            notices.Add(new OutputProjectionNotice(
                "output-projection.failure",
                exception.Message,
                patientKey, episodeKey, occurrence.OccurrenceId));
            return new ClinicalProjectedOccurrence(
                StableId("projection-failure", CurrentRulesVersion, occurrence.OccurrenceId),
                occurrence.OccurrenceId,
                occurrence.ConceptId,
                ProjectionDisposition.ProjectionFailure,
                [],
                occurrence.Observations.Select(static item => item.ObservationId).ToArray(),
                occurrence.DerivedObservations.Select(static item => item.DerivedObservationId).ToArray(),
                BuildFieldRecords(occurrence, [], false, options.ShowCultures, true));
        }
    }

    private static List<string> RenderOccurrence(
        LaboratoryOccurrence occurrence,
        IReadOnlyList<LaboratoryOccurrence> linkedSusceptibilities,
        OutputProjectionOptions options)
    {
        if (occurrence.ConceptId == "fsph-nh.hemograma-completo")
        {
            return RenderHemogram(occurrence, options);
        }

        if (occurrence.ConceptId is "fsph-nh.gasometria-arterial" or "fsph-nh.gasometria-venosa")
        {
            return RenderOrderedPanel(occurrence, options,
                ["PH", "PCO2", "PO2", "HCO3", "B.E", "BE", "CO2 TOTAL", "O2 SATURACAO", "SATO2", "LACTATO"]);
        }

        if (occurrence.ConceptId == "fsph-nh.exame-qualitativo-de-urina-equ")
        {
            return RenderOrderedPanel(occurrence, options,
                ["COR", "ASPECTO", "DENSIDADE", "PH", "PROTEINAS", "GLICOSE", "CETONAS", "BILIRRUBINA", "UROBILINOGENIO", "NITRITO", "LEUCOCITOS", "LEUCO", "HEMACIAS", "CELULAS EPITELIAIS", "BACTERIAS"]);
        }

        if (occurrence.ConceptId == "fsph-nh.tempo-de-tromboplastina-parcial-ativada-ttpa")
        {
            var result = occurrence.Observations.FirstOrDefault(static observation =>
                ReferenceLaboratoryCatalog.Normalize(observation.Label) == "AMOSTRA");
            return result is null ? [] : [$"TTPa {FormatObservation(result, options)}"];
        }

        if (occurrence.ConceptId == "fsph-nh.tempo-de-protrombina-tp")
        {
            var tpItems = occurrence.Observations
                .Where(static observation => ReferenceLaboratoryCatalog.Normalize(observation.Label) is
                    "ATIVIDADE" or "RNI")
                .Select(observation => $"{(ReferenceLaboratoryCatalog.Normalize(observation.Label) == "RNI" ? "RNI" : "Atividade")} {FormatObservation(observation, options)}")
                .ToArray();
            return tpItems.Length == 0 ? [] : [$"TP: {string.Join(" | ", tpItems)}"];
        }

        if (occurrence.ConceptId == "fsph-nh.bacterioscopico-gram")
        {
            var bacterioscopyItems = occurrence.Observations.Select((observation, index) =>
                    index == 0 || ReferenceLaboratoryCatalog.Normalize(observation.Label) == "RESULTADO"
                        ? observation.RawValue
                        : $"{OutputProjectionRules.ComponentLabel(observation.Label)} {observation.RawValue}")
                .Concat(occurrence.Narratives.Select(static narrative => narrative.RawText))
                .Where(static value => !string.IsNullOrWhiteSpace(value)).ToList();
            if (occurrence.Specimens.Count > 0)
            {
                bacterioscopyItems.Add($"material {occurrence.Specimens[0].RawSpecimen}");
            }

            return bacterioscopyItems.Count == 0 ? [] : [$"Bacterioscópico (Gram): {string.Join(" | ", bacterioscopyItems)}"];
        }

        if (occurrence.Microbiology is not null &&
            occurrence.StructuralForm is "microbiology-culture" or "susceptibility-panel")
        {
            return RenderMicrobiology(occurrence, linkedSusceptibilities, options);
        }

        var observations = occurrence.Observations
            .Where(static observation => !IsLaboratoryReportedEgfr(observation))
            .ToArray();
        if (observations.Length == 0 && occurrence.Attributes.Count > 0)
        {
            var attributes = occurrence.Attributes
                .Where(static item => ReferenceLaboratoryCatalog.Normalize(item.Name) is not "MATERIAL" and not "AMOSTRA")
                .Select(item => $"{OutputProjectionRules.ComponentLabel(item.Name)} {item.RawValue}")
                .ToList();
            if (attributes.Count > 0)
            {
                return occurrence.StructuralForm is "key-value-panel" or "sectioned-panel" or "multi-analyte-panel"
                    ? [$"{PanelLabel(occurrence)}: {string.Join(" | ", attributes)}"]
                    : attributes;
            }
        }
        var items = observations.Select((observation, index) =>
            $"{(index == 0 && occurrence.StructuralForm == "scalar-related" ? OutputProjectionRules.ConceptLabel(occurrence) : OutputProjectionRules.ComponentLabel(observation.Label))} {FormatObservation(observation, options)}")
            .ToList();
        if (occurrence.ConceptId == "fsph-nh.creatinina")
        {
            var computed = occurrence.DerivedObservations.SingleOrDefault(static item =>
                item.Status == DerivedObservationStatus.Computed);
            if (computed?.NumericValue is not null)
            {
                var truncated = Math.Truncate(computed.NumericValue.Value * 10d) / 10d;
                var tfg = truncated.ToString("0.0", CultureInfo.GetCultureInfo("pt-BR"));
                var index = Math.Min(1, items.Count);
                items.Insert(index, $"TFG {tfg}");
            }
        }

        if (items.Count == 0)
        {
            return [];
        }

        var panel = occurrence.StructuralForm is "sectioned-panel" or "key-value-panel" or "multi-analyte-panel" or "indexed-assay";
        if (panel)
        {
            return [$"{PanelLabel(occurrence)}: {string.Join(" | ", items)}"];
        }

        if (items.Count == 1)
        {
            return [$"{OutputProjectionRules.ConceptLabel(occurrence)} {FormatObservation(observations[0], options)}"];
        }

        return [string.Join(" | ", items)];
    }

    private static List<string> RenderHemogram(
        LaboratoryOccurrence occurrence,
        OutputProjectionOptions options)
    {
        var observations = occurrence.Observations;
        LaboratoryObservation? First(params string[] labels) => observations.FirstOrDefault(observation =>
            labels.Any(label => NormalizedLabel(observation.Label).Contains(label, StringComparison.Ordinal)));
        var mainDefinitions = new (string Label, string[] Match)[]
        {
            ("Hb", ["HEMOGLOBINA"]), ("Ht", ["HEMATOCRITO"]), ("VCM", ["V C M", "VCM"]),
            ("HCM", ["H C M", "HCM"]), ("CHCM", ["C H C M", "CHCM"]), ("RDW", ["RDW"]),
            ("Hemácias", ["HEMACIAS"]),
        };
        var items = new List<string>();
        foreach (var definition in mainDefinitions)
        {
            var observation = First(definition.Match);
            if (observation is not null)
            {
                items.Add($"{definition.Label} {FormatObservation(observation, options)}");
            }
        }

        var leucocytes = First("LEUCOCITOS");
        if (leucocytes is not null)
        {
            var differentialDefinitions = new (string Label, string[] Match)[]
            {
                ("Neut", ["SEGMENTADOS", "NEUTROFILOS"]), ("Bast", ["BASTONETES"]),
                ("Meta", ["METAMIELOCITOS"]), ("Mielo", ["MIELOCITOS"]),
                ("Blastos", ["BLASTOS"]),
                ("Linf", ["LINFOCITOS"]), ("Mono", ["MONOCITOS"]),
                ("Eos", ["EOSINOFILOS"]), ("Baso", ["BASOFILOS"]),
            };
            var differential = new List<string>();
            foreach (var definition in differentialDefinitions)
            {
                var matching = observations.Where(observation =>
                {
                    var normalized = NormalizedLabel(observation.Label);
                    return definition.Label is "Mielo" or "Blastos"
                        ? normalized == definition.Match[0]
                        : definition.Match.Any(label => normalized.Contains(label, StringComparison.Ordinal));
                }).ToArray();
                var percentage = matching.FirstOrDefault(static observation =>
                    string.Equals(observation.RawUnit?.Trim(), "%", StringComparison.Ordinal));
                var absolute = matching.FirstOrDefault(static observation =>
                    !string.Equals(observation.RawUnit?.Trim(), "%", StringComparison.Ordinal));
                if (percentage is not null && absolute is not null)
                {
                    var absoluteText = options.ShowUnits && !string.IsNullOrWhiteSpace(absolute.RawUnit)
                        ? $"{absolute.RawValue} {absolute.RawUnit}"
                        : absolute.RawValue;
                    differential.Add($"{definition.Label} {percentage.RawValue}% [{absoluteText}]");
                }
                else if (percentage is not null)
                {
                    differential.Add($"{definition.Label} {percentage.RawValue}%");
                }
                else if (absolute is not null)
                {
                    differential.Add($"{definition.Label} [{FormatObservation(absolute, options)}]");
                }
            }

            var erythroblasts = First("ERITROBLASTOS/100 LEUCOCITOS", "ERITROBLASTOS");
            if (erythroblasts is not null)
            {
                differential.Add($"Eritroblastos/100 Leuco {FormatObservation(erythroblasts, options)}");
            }

            items.Add($"Leuco {FormatObservation(leucocytes, options)}" +
                      (differential.Count == 0 ? string.Empty : $" ({string.Join(" | ", differential)})"));
        }

        var morphology = observations.Where(static observation =>
                NormalizedLabel(observation.Label).Contains("OBSERV", StringComparison.Ordinal))
            .Select(static observation => observation.RawValue)
            .Concat(occurrence.Narratives.Select(static narrative => narrative.RawText))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Where(static value => ReferenceLaboratoryCatalog.Normalize(value) is not "ERITROCITOS" and not "LEUCOCITOS" and not "PLAQUETAS")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        items.AddRange(morphology.Select(static value => $"Observação laboratorial: {value}"));

        var platelets = First("PLAQUETAS");
        if (platelets is not null)
        {
            items.Add($"Plaq {FormatObservation(platelets, options)}");
        }

        return items.Count == 0 ? [] : [string.Join(" | ", items)];
    }

    private static List<string> RenderOrderedPanel(
        LaboratoryOccurrence occurrence,
        OutputProjectionOptions options,
        IReadOnlyList<string> labelOrder)
    {
        var ordered = occurrence.Observations
            .Select((observation, index) => new
            {
                Observation = observation,
                Index = index,
                Order = FindLabelOrder(NormalizedLabel(observation.Label), labelOrder),
            })
            .OrderBy(static item => item.Order)
            .ThenBy(static item => item.Index)
            .Select(item => $"{OutputProjectionRules.ComponentLabel(item.Observation.Label)} {FormatObservation(item.Observation, options)}")
            .ToArray();
        return ordered.Length == 0 ? [] : [$"{PanelLabel(occurrence)}: {string.Join(" | ", ordered)}"];
    }

    private static int FindLabelOrder(string label, IReadOnlyList<string> order)
    {
        for (var index = 0; index < order.Count; index++)
        {
            if (label.Contains(order[index], StringComparison.Ordinal))
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private static string NormalizedLabel(string label) =>
        ReferenceLaboratoryCatalog.Normalize(label).Replace('.', ' ').Replace("  ", " ", StringComparison.Ordinal);

    private static List<string> RenderMicrobiology(
        LaboratoryOccurrence occurrence,
        IReadOnlyList<LaboratoryOccurrence> linkedSusceptibilities,
        OutputProjectionOptions options)
    {
        var lines = new List<string>();
        var material = occurrence.Specimens.Count > 0 ? occurrence.Specimens[0].RawSpecimen : null;
        var result = (occurrence.Observations.Count > 0 ? occurrence.Observations[0].RawValue : null) ??
                     occurrence.Attributes.FirstOrDefault(static item =>
                         ReferenceLaboratoryCatalog.Normalize(item.Name) == "CULTURAL")?.RawValue;
        var header = OutputProjectionRules.ConceptLabel(occurrence) + ":";
        if (!string.IsNullOrWhiteSpace(material))
        {
            header += $" material {material}";
        }
        if (!string.IsNullOrWhiteSpace(result))
        {
            header += (header.EndsWith(':') ? " " : " | ") + result;
        }
        lines.Add(header);
        lines.AddRange(occurrence.Narratives.Select(static item => item.RawText)
            .Where(static item => !string.IsNullOrWhiteSpace(item)));

        if (occurrence.Microbiology is null)
        {
            return lines;
        }

        foreach (var (organism, index) in occurrence.Microbiology.Organisms.Select((item, index) => (item, index)))
        {
            lines.Add($"Organismo {index + 1}: {organism.RawName}");
            var group = occurrence.Microbiology.SusceptibilityGroups.FirstOrDefault(item =>
                item.OrganismId == organism.OrganismId) ?? linkedSusceptibilities
                .SelectMany(static item => item.Microbiology?.SusceptibilityGroups ?? [])
                .FirstOrDefault(item => ReferenceLaboratoryCatalog.Normalize(item.RawOrganismName ?? string.Empty) ==
                    ReferenceLaboratoryCatalog.Normalize(organism.RawName));
            if (group is null || group.Entries.Count == 0)
            {
                continue;
            }

            var entries = group.Entries.Select(entry =>
            {
                var interpretation = entry.Interpretation switch
                {
                    "SENSIVEL" => "S",
                    "RESISTENTE" => "R",
                    "INTERMEDIARIO" => "I",
                    _ => entry.RawResult,
                };
                return FormatSusceptibilityEntry(entry, interpretation);
            });
            lines.Add($"Antibiograma: {string.Join(" | ", entries)}");
        }

        return lines;
    }

    private static string FormatSusceptibilityEntry(
        LaboratorySusceptibilityEntry entry,
        string interpretation)
    {
        var normalized = ReferenceLaboratoryCatalog.Normalize(entry.RawResult);
        var hasMic = normalized.Contains("MIC", StringComparison.Ordinal) ||
                     normalized.Contains("CIM", StringComparison.Ordinal);
        return hasMic
            ? $"{entry.RawAntimicrobial} {entry.RawResult}"
            : $"{entry.RawAntimicrobial} {interpretation}";
    }

    private static string FormatObservation(LaboratoryObservation observation, OutputProjectionOptions options) =>
        options.ShowUnits && !string.IsNullOrWhiteSpace(observation.RawUnit)
            ? $"{observation.RawValue} {observation.RawUnit}"
            : observation.RawValue;

    private static bool IsLaboratoryReportedEgfr(LaboratoryObservation observation) =>
        ReferenceLaboratoryCatalog.Normalize(observation.Label) == "TAXA DE FILTRACAO GLOMERULAR ESTIMADA";

    public static bool IsCultureOccurrence(LaboratoryOccurrence occurrence) =>
        occurrence.StructuralForm is "microbiology-culture" or "susceptibility-panel";

    private static List<FieldProjectionRecord> BuildFieldRecords(
        LaboratoryOccurrence occurrence,
        IReadOnlyList<string> outputLines,
        bool rendered,
        bool culturesShown,
        bool failure = false)
    {
        var records = new List<FieldProjectionRecord>();
        FieldProjectionDisposition Disposition(bool projected, bool auditOnly = false) => failure
            ? FieldProjectionDisposition.ProjectionFailure
            : projected ? FieldProjectionDisposition.Projected
            : auditOnly ? FieldProjectionDisposition.AuditOnly
            : FieldProjectionDisposition.SuppressedByExplicitPolicy;
        string Reason(bool projected, string hidden) => failure ? "formatter-failure" : projected ? "rendered" : hidden;
        var culture = IsCultureOccurrence(occurrence);

        foreach (var observation in occurrence.Observations)
        {
            var normalized = ReferenceLaboratoryCatalog.Normalize(observation.Label);
            var explicitlySuppressed = IsLaboratoryReportedEgfr(observation) ||
                (occurrence.ConceptId == "fsph-nh.tempo-de-protrombina-tp" &&
                 normalized is "CONTROLE NORMAL" or "AMOSTRA");
            var projected = rendered && !explicitlySuppressed && (!culture || culturesShown);
            records.Add(new FieldProjectionRecord(observation.ObservationId, "Observation",
                Disposition(projected, culture && !culturesShown),
                Reason(projected, explicitlySuppressed ? "explicit-clinical-policy" : "culture-review-only")));
        }

        foreach (var derived in occurrence.DerivedObservations)
        {
            var projected = rendered && derived.Status == DerivedObservationStatus.Computed;
            records.Add(new FieldProjectionRecord(derived.DerivedObservationId, "DerivedObservation",
                Disposition(projected, !projected), Reason(projected, "not-computed-audit-only")));
        }

        for (var index = 0; index < occurrence.Attributes.Count; index++)
        {
            records.Add(new FieldProjectionRecord($"attributes[{index}]", "Attribute",
                Disposition(false, true), Reason(false, "structured-duplicate-or-audit-only")));
        }

        for (var index = 0; index < occurrence.Narratives.Count; index++)
        {
            var narrative = occurrence.Narratives[index];
            var hemogramNarrativeIsContent = occurrence.ConceptId == "fsph-nh.hemograma-completo" &&
                !string.IsNullOrWhiteSpace(narrative.RawText) &&
                ReferenceLaboratoryCatalog.Normalize(narrative.RawText) is not
                    "ERITROCITOS" and not "LEUCOCITOS" and not "PLAQUETAS";
            var projected = rendered && (culture ? culturesShown : hemogramNarrativeIsContent ||
                occurrence.ConceptId == "fsph-nh.bacterioscopico-gram");
            records.Add(new FieldProjectionRecord($"narratives[{index}]", "Narrative",
                Disposition(projected, !projected), Reason(projected, culture ? "culture-review-only" : "audit-only")));
        }

        for (var index = 0; index < occurrence.Specimens.Count; index++)
        {
            var projected = rendered && (culture ? culturesShown : occurrence.ConceptId == "fsph-nh.bacterioscopico-gram");
            records.Add(new FieldProjectionRecord($"specimens[{index}]", "Specimen",
                Disposition(projected, !projected), Reason(projected, culture ? "culture-review-only" : "compact-output-policy")));
        }

        for (var index = 0; index < occurrence.References.Count; index++)
        {
            records.Add(new FieldProjectionRecord($"references[{index}]", "Reference",
                Disposition(false, true), Reason(false, "reference-audit-only")));
        }

        for (var index = 0; index < occurrence.Relationships.Count; index++)
        {
            var projected = rendered && culturesShown;
            records.Add(new FieldProjectionRecord($"relationships[{index}]", "Relationship",
                Disposition(projected, !projected), Reason(projected, "culture-review-only")));
        }

        if (occurrence.Microbiology is not null)
        {
            for (var index = 0; index < occurrence.Microbiology.Organisms.Count; index++)
            {
                var projected = rendered && culturesShown;
                records.Add(new FieldProjectionRecord($"microbiology.organisms[{index}]", "Organism",
                    Disposition(projected, !projected), Reason(projected, "culture-review-only")));
            }

            for (var groupIndex = 0; groupIndex < occurrence.Microbiology.SusceptibilityGroups.Count; groupIndex++)
            {
                var group = occurrence.Microbiology.SusceptibilityGroups[groupIndex];
                var projected = rendered && culturesShown;
                records.Add(new FieldProjectionRecord($"microbiology.susceptibilityGroups[{groupIndex}]", "SusceptibilityGroup",
                    Disposition(projected, !projected), Reason(projected, "culture-review-only")));
                for (var entryIndex = 0; entryIndex < group.Entries.Count; entryIndex++)
                {
                    records.Add(new FieldProjectionRecord(
                        $"microbiology.susceptibilityGroups[{groupIndex}].entries[{entryIndex}]", "SusceptibilityEntry",
                        Disposition(projected, !projected), Reason(projected, "culture-review-only")));
                }
            }
        }

        return records.Select(record => AttachOutputLocator(record, occurrence, outputLines)).ToList();
    }

    private static FieldProjectionRecord AttachOutputLocator(
        FieldProjectionRecord record,
        LaboratoryOccurrence occurrence,
        IReadOnlyList<string> outputLines)
    {
        if (record.Disposition != FieldProjectionDisposition.Projected)
        {
            return record;
        }

        var searchTerms = ProjectionSearchTerms(record, occurrence);
        var lineIndex = outputLines
            .Select((line, index) => new { Line = line, Index = index })
            .FirstOrDefault(item => searchTerms.Any(term =>
                !string.IsNullOrWhiteSpace(term) && item.Line.Contains(term, StringComparison.OrdinalIgnoreCase)));
        return lineIndex is null
            ? record with
            {
                Disposition = FieldProjectionDisposition.AuditOnly,
                ReasonCode = "not-selected-by-editorial-policy",
            }
            : record with
            {
                OutputLineIndex = lineIndex.Index,
                OutputFragment = lineIndex.Line,
            };
    }

    private static IReadOnlyList<string> ProjectionSearchTerms(
        FieldProjectionRecord record,
        LaboratoryOccurrence occurrence)
    {
        if (record.FieldKind == "Observation")
        {
            var observation = occurrence.Observations.FirstOrDefault(item => item.ObservationId == record.FieldKey);
            return observation is null ? [] : [observation.RawValue];
        }

        if (record.FieldKind == "DerivedObservation")
        {
            var derived = occurrence.DerivedObservations.FirstOrDefault(item => item.DerivedObservationId == record.FieldKey);
            return derived?.NumericValue is null
                ? []
                : [$"TFG {Math.Truncate(derived.NumericValue.Value * 10d) / 10d:0.0}", "TFG"];
        }

        if (TryParseIndexedField(record.FieldKey, "narratives", out var narrativeIndex) &&
            narrativeIndex < occurrence.Narratives.Count)
        {
            return [occurrence.Narratives[narrativeIndex].RawText];
        }

        if (TryParseIndexedField(record.FieldKey, "specimens", out var specimenIndex) &&
            specimenIndex < occurrence.Specimens.Count)
        {
            return [occurrence.Specimens[specimenIndex].RawSpecimen];
        }

        if (TryParseIndexedField(record.FieldKey, "microbiology.organisms", out var organismIndex) &&
            occurrence.Microbiology is not null && organismIndex < occurrence.Microbiology.Organisms.Count)
        {
            return [occurrence.Microbiology.Organisms[organismIndex].RawName];
        }

        if (record.FieldKind == "SusceptibilityEntry" && occurrence.Microbiology is not null)
        {
            var indices = record.FieldKey.Split(['[', ']'], StringSplitOptions.RemoveEmptyEntries)
                .Where(static part => int.TryParse(part, out _)).Select(int.Parse).ToArray();
            if (indices.Length == 2 && indices[0] < occurrence.Microbiology.SusceptibilityGroups.Count)
            {
                var entries = occurrence.Microbiology.SusceptibilityGroups[indices[0]].Entries;
                if (indices[1] < entries.Count)
                {
                    return [entries[indices[1]].RawAntimicrobial];
                }
            }
        }

        return record.FieldKind switch
        {
            "Relationship" or "SusceptibilityGroup" => ["Antibiograma"],
            _ => [],
        };
    }

    private static bool TryParseIndexedField(string fieldKey, string prefix, out int index)
    {
        index = -1;
        if (!fieldKey.StartsWith(prefix + "[", StringComparison.Ordinal) || !fieldKey.EndsWith(']'))
        {
            return false;
        }

        return int.TryParse(fieldKey[(prefix.Length + 1)..^1], CultureInfo.InvariantCulture, out index);
    }

    private static string PanelLabel(LaboratoryOccurrence occurrence) => occurrence.ConceptId switch
    {
        "fsph-nh.hemograma-completo" => "Hemograma",
        "fsph-nh.exame-qualitativo-de-urina-equ" => "EQU",
        "fsph-nh.gasometria-arterial" => "Gasometria arterial",
        "fsph-nh.gasometria-venosa" => "Gasometria venosa",
        _ => OutputProjectionRules.ConceptLabel(occurrence),
    };

    private static DateTime EpisodeDateTime(SemanticEpisode episode)
    {
        var value = $"{episode.DocumentaryEpisode.RequestDate} {episode.DocumentaryEpisode.RequestTime}";
        return DateTime.TryParseExact(value, "dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed) ? parsed : DateTime.MinValue;
    }

    private static string StableId(string prefix, params string[] parts)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f', parts)));
        return $"{prefix}-{Convert.ToHexString(bytes).ToLowerInvariant()[..16]}";
    }
}
