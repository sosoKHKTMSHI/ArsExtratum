using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ArsExtractum.Core.Documents;
using ArsExtractum.Core.Pipeline;

namespace ArsExtractum.Core.Assembly;

public static class PatientEpisodeAssembler
{
    public const string CurrentSchemaVersion = "1.2";
    public const string CurrentRulesVersion = "patient-episode-assembly-rules/1.0";

    public static StageDescriptor Descriptor { get; } = new(
        StageIds.PatientEpisodeAssembly,
        "Montagem de pacientes e episódios",
        "Reúne PDFs do mesmo paciente e organiza páginas por requisição, data e hora.",
        CurrentSchemaVersion,
        [StageIds.Sanitization]);

    public static PatientBatch Assemble(IEnumerable<SanitizedDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        return Assemble(documents.Select(static (document, index) =>
            new PatientAssemblyInput(document, index)));
    }

    public static PatientBatch Assemble(IEnumerable<PatientAssemblyInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var patientBuilders = new Dictionary<string, PatientBuilder>(StringComparer.Ordinal);
        var unassignedDocuments = new List<UnassignedDocument>();
        var ledger = new List<AssemblyLedgerEntry>();

        foreach (var item in inputs)
        {
            ArgumentNullException.ThrowIfNull(item.Document);
            var pages = CreatePages(item.Document, item.InputIndex);
            if (!TryReadDocumentIdentity(item.Document, out var identity, out var reason))
            {
                unassignedDocuments.Add(new UnassignedDocument(
                    item.Document.DocumentId,
                    item.Document.FileName,
                    reason,
                    pages));
                ledger.Add(CreateLedgerEntry(item.Document, item.InputIndex, "Rejected", 0, pages.Length, reason));
                continue;
            }

            var patientKey = CreatePatientKey(identity);
            if (!patientBuilders.TryGetValue(patientKey, out var patient))
            {
                patient = new PatientBuilder(patientKey, identity);
                patientBuilders.Add(patientKey, patient);
            }
            else if (!patient.IsCompatible(identity))
            {
                unassignedDocuments.Add(new UnassignedDocument(
                    item.Document.DocumentId,
                    item.Document.FileName,
                    "Documento com sexo divergente de outro PDF da mesma identidade.",
                    pages));
                ledger.Add(CreateLedgerEntry(
                    item.Document,
                    item.InputIndex,
                    "Rejected",
                    0,
                    pages.Length,
                    "Documento com sexo divergente de outro PDF da mesma identidade."));
                continue;
            }

            var stats = patient.AddDocument(item.Document, item.InputIndex, pages);
            ledger.Add(CreateLedgerEntry(
                item.Document,
                item.InputIndex,
                stats.UnassignedPageCount == 0 ? "Assigned" : "AssignedWithUnassignedPages",
                stats.AssignedPageCount,
                stats.UnassignedPageCount,
                stats.UnassignedPageCount == 0
                    ? null
                    : "Uma ou mais páginas não possuem chave completa de episódio."));
        }

        var patients = patientBuilders.Values
            .Select(static builder => builder.Build())
            .OrderBy(static patient => Normalize(patient.Identity.PatientName), StringComparer.Ordinal)
            .ThenBy(static patient => patient.Identity.BirthDate, StringComparer.Ordinal)
            .ToArray();

        return new PatientBatch(CurrentSchemaVersion, CurrentRulesVersion, patients, unassignedDocuments)
        {
            Ledger = ledger.OrderBy(static entry => entry.InputIndex).ToArray(),
        };
    }

    private static AssemblyLedgerEntry CreateLedgerEntry(
        SanitizedDocument document,
        int inputIndex,
        string disposition,
        int assignedPageCount,
        int unassignedPageCount,
        string? reason)
    {
        var activeLineCount = document.Pages.Sum(static page =>
            page.Lines.Count(static line => line.Disposition == SanitizedDisposition.Active));
        var suppressedLineCount = document.Pages.Sum(static page =>
            page.Lines.Count(static line => line.Disposition != SanitizedDisposition.Active));
        return new AssemblyLedgerEntry(
            document.DocumentId,
            document.FileName,
            inputIndex,
            disposition,
            document.Pages.Count,
            assignedPageCount,
            unassignedPageCount,
            activeLineCount,
            suppressedLineCount,
            reason);
    }

    private static bool TryReadDocumentIdentity(
        SanitizedDocument document,
        out PatientIdentity identity,
        out string reason)
    {
        identity = new PatientIdentity(string.Empty, string.Empty, null);
        reason = string.Empty;
        if (document.Pages.Count == 0)
        {
            reason = "Documento sem páginas higienizadas.";
            return false;
        }

        var first = document.Pages[0].Header;
        if (string.IsNullOrWhiteSpace(first.PatientName) || string.IsNullOrWhiteSpace(first.BirthDate))
        {
            reason = "A primeira página não possui nome e nascimento suficientes para identificar o paciente.";
            return false;
        }

        var expectedName = Normalize(first.PatientName);
        var expectedBirthDate = NormalizeDate(first.BirthDate);
        var expectedSex = NormalizeOptional(first.Sex);
        foreach (var page in document.Pages)
        {
            var header = page.Header;
            if (string.IsNullOrWhiteSpace(header.PatientName) || string.IsNullOrWhiteSpace(header.BirthDate))
            {
                reason = $"A página {page.PageNumber} não possui identidade completa.";
                return false;
            }

            if (!string.Equals(Normalize(header.PatientName), expectedName, StringComparison.Ordinal) ||
                !string.Equals(NormalizeDate(header.BirthDate), expectedBirthDate, StringComparison.Ordinal))
            {
                reason = $"A página {page.PageNumber} apresenta outro nome ou nascimento.";
                return false;
            }

            var pageSex = NormalizeOptional(header.Sex);
            if (expectedSex is not null && pageSex is not null &&
                !string.Equals(pageSex, expectedSex, StringComparison.Ordinal))
            {
                reason = $"A página {page.PageNumber} apresenta sexo divergente.";
                return false;
            }
        }

        identity = new PatientIdentity(
            first.PatientName.Trim(),
            first.BirthDate.Trim(),
            first.Sex?.Trim());
        return true;
    }

    private static AssembledPage[] CreatePages(SanitizedDocument document, int inputIndex) =>
        document.Pages
            .OrderBy(static page => page.PageNumber)
            .Select(page => new AssembledPage(
                document.DocumentId,
                document.FileName,
                inputIndex,
                page.PageNumber,
                page.Header,
                page.Lines
                    .Where(static line => line.Disposition == SanitizedDisposition.Active)
                    .OrderBy(static line => line.SourceIndex)
                    .ToArray()))
            .ToArray();

    private static string CreatePatientKey(PatientIdentity identity) =>
        StableKey("patient", Normalize(identity.PatientName), NormalizeDate(identity.BirthDate));

    private static string CreateEpisodeKey(
        string patientKey,
        string requestNumber,
        string requestDate,
        string requestTime) =>
        StableKey("episode", patientKey, Normalize(requestNumber), NormalizeDate(requestDate), requestTime.Trim());

    private static string StableKey(string prefix, params string[] parts)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001F', parts)));
        return $"{prefix}-{Convert.ToHexString(bytes)[..16].ToLowerInvariant()}";
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.Normalize(NormalizationForm.FormC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Normalize(value);

    private static string NormalizeDate(string value) =>
        DateOnly.TryParseExact(
            value.Trim(),
            "dd/MM/yyyy",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : Normalize(value);

    private sealed class PatientBuilder(string patientKey, PatientIdentity identity)
    {
        private readonly List<PatientSourceDocument> _documents = [];
        private readonly Dictionary<string, EpisodeBuilder> _episodes = new(StringComparer.Ordinal);
        private readonly List<AssembledPage> _unassignedPages = [];

        public bool IsCompatible(PatientIdentity candidate)
        {
            var expectedSex = NormalizeOptional(identity.Sex);
            var candidateSex = NormalizeOptional(candidate.Sex);
            return expectedSex is null || candidateSex is null ||
                   string.Equals(expectedSex, candidateSex, StringComparison.Ordinal);
        }

        public AssemblyDocumentStats AddDocument(
            SanitizedDocument document,
            int inputIndex,
            AssembledPage[] pages)
        {
            _documents.Add(new PatientSourceDocument(document.DocumentId, document.FileName, inputIndex));
            var unassignedPageCount = 0;
            foreach (var page in pages)
            {
                var header = page.Header;
                if (string.IsNullOrWhiteSpace(header.RequestNumber) ||
                    string.IsNullOrWhiteSpace(header.RequestDate) ||
                    string.IsNullOrWhiteSpace(header.RequestTime))
                {
                    _unassignedPages.Add(page);
                    unassignedPageCount++;
                    continue;
                }

                var episodeKey = CreateEpisodeKey(
                    patientKey,
                    header.RequestNumber,
                    header.RequestDate,
                    header.RequestTime);
                if (!_episodes.TryGetValue(episodeKey, out var episode))
                {
                    episode = new EpisodeBuilder(
                        episodeKey,
                        header.RequestNumber,
                        header.RequestDate,
                        header.RequestTime);
                    _episodes.Add(episodeKey, episode);
                }

                episode.Add(page);
            }

            return new AssemblyDocumentStats(
                pages.Length - unassignedPageCount,
                unassignedPageCount);
        }

        public AssembledPatient Build() =>
            new(
                patientKey,
                identity,
                _documents.OrderBy(static document => document.InputIndex).ToArray(),
                _episodes.Values
                    .Select(episode => episode.Build(identity.BirthDate))
                    .OrderByDescending(static episode => ParseEpisodeDateTime(episode.RequestDate, episode.RequestTime))
                    .ThenBy(static episode => episode.RequestNumber, StringComparer.Ordinal)
                    .ToArray(),
                _unassignedPages
                    .OrderBy(static page => page.InputIndex)
                    .ThenBy(static page => page.PageNumber)
                    .ToArray());
    }

    private sealed record AssemblyDocumentStats(
        int AssignedPageCount,
        int UnassignedPageCount);

    private sealed class EpisodeBuilder(
        string episodeKey,
        string requestNumber,
        string requestDate,
        string requestTime)
    {
        private readonly List<AssembledPage> _pages = [];

        public void Add(AssembledPage page) => _pages.Add(page);

        public AssembledEpisode Build(string birthDate)
        {
            var pages = _pages.OrderBy(static page => page.InputIndex)
                .ThenBy(static page => page.PageNumber)
                .ToArray();
            var contentBlocks = BuildCanonicalContentBlocks(episodeKey, pages);
            return new AssembledEpisode(
                episodeKey,
                requestNumber,
                requestDate,
                requestTime,
                CalculateAgeAtRequest(birthDate, requestDate),
                CalculateCoverage(pages, contentBlocks),
                pages.Select(static page => page.Header.Origin)
                    .Where(static origin => !string.IsNullOrWhiteSpace(origin))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(static origin => origin!)
                .ToArray(),
                pages,
                contentBlocks);
        }
    }

    private static EpisodeAssemblyCoverage CalculateCoverage(
        IReadOnlyList<AssembledPage> pages,
        IReadOnlyList<CanonicalEpisodeContentBlock> blocks)
    {
        // Coverage is derived from both sides of the mapping so a future change cannot
        // silently omit a source page or assign the same source to multiple blocks.
        var activePages = pages.Where(static page => page.ActiveLines.Count > 0).ToArray();
        var ownerCounts = blocks
            .SelectMany(static block => block.Sources)
            .GroupBy(static source => (source.DocumentId, source.InputIndex, source.PageNumber))
            .ToDictionary(static group => group.Key, static group => group.Count());
        var orphanSourceCount = activePages.Count(page =>
            !ownerCounts.ContainsKey((page.DocumentId, page.InputIndex, page.PageNumber)));
        var multiplyAssignedSourceCount = ownerCounts.Values.Count(static count => count > 1);
        var sourceActiveLineCount = activePages.Sum(static page => page.ActiveLines.Count);
        var canonicalActiveLineCount = blocks.Sum(static block => block.ActiveLines.Count);
        var sourceCount = blocks.Sum(static block => block.Sources.Count);
        var equivalenceMetadataConsistent = blocks.All(static block =>
            block.Equivalence.SourceCount == block.Sources.Count);
        var sourceEvidenceConsistent = blocks.All(block => block.Sources.All(source =>
        {
            var page = activePages.SingleOrDefault(page =>
                page.DocumentId == source.DocumentId &&
                page.InputIndex == source.InputIndex &&
                page.PageNumber == source.PageNumber);
            return page is not null &&
                   source.LineIds.SequenceEqual(
                       page.ActiveLines.Select(static line => line.Id),
                       StringComparer.Ordinal) &&
                   block.ActiveLines.Select(static line => line.Text).SequenceEqual(
                       page.ActiveLines.Select(static line => line.Text),
                       StringComparer.Ordinal);
        }));
        var isLossless = orphanSourceCount == 0 &&
                         multiplyAssignedSourceCount == 0 &&
                         sourceCount == activePages.Length &&
                         equivalenceMetadataConsistent &&
                         sourceEvidenceConsistent &&
                         canonicalActiveLineCount <= sourceActiveLineCount;

        return new EpisodeAssemblyCoverage(
            pages.Count,
            activePages.Length,
            pages.Count - activePages.Length,
            sourceActiveLineCount,
            blocks.Count,
            canonicalActiveLineCount,
            sourceCount - blocks.Count,
            sourceActiveLineCount - canonicalActiveLineCount,
            orphanSourceCount,
            multiplyAssignedSourceCount,
            isLossless);
    }

    private static List<CanonicalEpisodeContentBlock> BuildCanonicalContentBlocks(
        string episodeKey,
        IReadOnlyList<AssembledPage> pages)
    {
        var blocks = new List<CanonicalEpisodeContentBlock>();
        foreach (var page in pages.Where(static page => page.ActiveLines.Count > 0))
        {
            var texts = page.ActiveLines.Select(static line => line.Text).ToArray();
            var fingerprint = StableKey("content", texts);
            // A hash narrows candidates, but exact ordinal text equality is the authority.
            // Divergent or partially matching content therefore remains in separate blocks.
            var equivalentIndex = blocks.FindIndex(block =>
                block.ContentFingerprint == fingerprint &&
                block.ActiveLines.Select(static line => line.Text).SequenceEqual(texts, StringComparer.Ordinal));
            var source = new EpisodeContentSource(
                page.DocumentId,
                page.FileName,
                page.InputIndex,
                page.PageNumber,
                page.ActiveLines.Select(static line => line.Id).ToArray());
            if (equivalentIndex < 0)
            {
                blocks.Add(new CanonicalEpisodeContentBlock(
                    StableKey("block", episodeKey, fingerprint, blocks.Count.ToString(CultureInfo.InvariantCulture)),
                    fingerprint,
                    new EpisodeContentEquivalence(
                        "episode.exact-active-page-content.v1",
                        "ExactOrdinal",
                        1),
                    page.ActiveLines,
                    [source]));
            }
            else
            {
                var equivalent = blocks[equivalentIndex];
                blocks[equivalentIndex] = equivalent with
                {
                    Sources = equivalent.Sources.Append(source).ToArray(),
                    Equivalence = equivalent.Equivalence with
                    {
                        SourceCount = equivalent.Equivalence.SourceCount + 1,
                    },
                };
            }
        }

        return blocks;
    }

    private static EpisodeAgeAtRequest CalculateAgeAtRequest(string birthDate, string requestDate)
    {
        // Documentary reported age is intentionally ignored: episode age is reproducibly
        // derived only from the patient's birth date and the episode request date.
        if (!DateOnly.TryParseExact(birthDate.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var birth))
        {
            return new EpisodeAgeAtRequest(null, "NotComputed", "BirthDateInvalid");
        }

        if (!DateOnly.TryParseExact(requestDate.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var request))
        {
            return new EpisodeAgeAtRequest(null, "NotComputed", "RequestDateInvalid");
        }

        if (request < birth)
        {
            return new EpisodeAgeAtRequest(null, "NotComputed", "RequestBeforeBirth");
        }

        var years = request.Year - birth.Year;
        if (request < birth.AddYears(years))
        {
            years--;
        }

        return new EpisodeAgeAtRequest(years, "Computed", null);
    }

    private static DateTime ParseEpisodeDateTime(string date, string time) =>
        DateTime.TryParseExact(
            $"{date} {time}",
            "dd/MM/yyyy HH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : DateTime.MaxValue;
}
