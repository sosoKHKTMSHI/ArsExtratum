using ArsExtractum.Core.Documents;

namespace ArsExtractum.Core.Assembly;

public sealed record PatientBatch(
    string SchemaVersion,
    string RulesVersion,
    IReadOnlyList<AssembledPatient> Patients,
    IReadOnlyList<UnassignedDocument> UnassignedDocuments)
{
    public IReadOnlyList<AssemblyLedgerEntry> Ledger { get; init; } = [];

    public int EpisodeCount => Patients.Sum(static patient => patient.Episodes.Count);

    public int PageCount => Patients.Sum(static patient => patient.Episodes.Sum(static episode => episode.Pages.Count)) +
                            Patients.Sum(static patient => patient.UnassignedPages.Count) +
                            UnassignedDocuments.Sum(static document => document.Pages.Count);
}

public sealed record PatientAssemblyInput(
    SanitizedDocument Document,
    int InputIndex);

public sealed record AssembledPatient(
    string PatientKey,
    PatientIdentity Identity,
    IReadOnlyList<PatientSourceDocument> SourceDocuments,
    IReadOnlyList<AssembledEpisode> Episodes,
    IReadOnlyList<AssembledPage> UnassignedPages);

public sealed record PatientIdentity(
    string PatientName,
    string BirthDate,
    string? Sex);

public sealed record PatientSourceDocument(
    string DocumentId,
    string FileName,
    int InputIndex);

public sealed record AssembledEpisode(
    string EpisodeKey,
    string RequestNumber,
    string RequestDate,
    string RequestTime,
    EpisodeAgeAtRequest AgeAtRequest,
    EpisodeAssemblyCoverage Coverage,
    IReadOnlyList<string> Origins,
    IReadOnlyList<AssembledPage> Pages,
    IReadOnlyList<CanonicalEpisodeContentBlock> ContentBlocks);

public sealed record EpisodeAgeAtRequest(int? CompletedYears, string Status, string? Reason);

public sealed record EpisodeAssemblyCoverage(
    int SourcePageCount,
    int ActivePageCount,
    int EmptyActivePageCount,
    int SourceActiveLineCount,
    int CanonicalBlockCount,
    int CanonicalActiveLineCount,
    int EquivalentSourceCount,
    int DeduplicatedLineCount,
    int OrphanSourceCount,
    int MultiplyAssignedSourceCount,
    bool IsLossless);

public sealed record CanonicalEpisodeContentBlock(
    string BlockId,
    string ContentFingerprint,
    EpisodeContentEquivalence Equivalence,
    IReadOnlyList<SanitizedLine> ActiveLines,
    IReadOnlyList<EpisodeContentSource> Sources);

public sealed record EpisodeContentEquivalence(
    string RuleId,
    string Comparison,
    int SourceCount);

public sealed record EpisodeContentSource(
    string DocumentId,
    string FileName,
    int InputIndex,
    int PageNumber,
    IReadOnlyList<string> LineIds);

public sealed record AssembledPage(
    string DocumentId,
    string FileName,
    int InputIndex,
    int PageNumber,
    SanitizedHeader Header,
    IReadOnlyList<SanitizedLine> ActiveLines);

public sealed record UnassignedDocument(
    string DocumentId,
    string FileName,
    string Reason,
    IReadOnlyList<AssembledPage> Pages);

public sealed record AssemblyLedgerEntry(
    string DocumentId,
    string FileName,
    int InputIndex,
    string Disposition,
    int? SourcePageCount,
    int AssignedPageCount,
    int UnassignedPageCount,
    int ActiveLineCount,
    int SuppressedLineCount,
    string? Reason);
