using System.Text.Json.Serialization;

namespace ArsExtractum.Core.Documents;

public sealed record SanitizedDocument(
    string SchemaVersion,
    string RulesVersion,
    string DocumentId,
    string FileName,
    IReadOnlyList<SanitizedPage> Pages)
{
    public int ActiveLineCount => Pages.Sum(static page =>
        page.Lines.Count(static line => line.Disposition == SanitizedDisposition.Active));

    public int SuppressedLineCount => Pages.Sum(static page =>
        page.Lines.Count(static line => line.Disposition != SanitizedDisposition.Active));

    public int UnresolvedHeaderFragmentCount =>
        Pages.Sum(static page => page.Header.UnresolvedFragments.Count);
}

public sealed record SanitizedPage(
    int PageNumber,
    SanitizedHeader Header,
    IReadOnlyList<SanitizedLine> Lines,
    bool FooterRecognized);

public sealed record SanitizedHeader(
    string? Issuer,
    string? Laboratory,
    string? PatientName,
    string? Sex,
    string? BirthDate,
    string? ReportedAge,
    string? Requester,
    string? RequesterRegistration,
    string? RequestNumber,
    string? RequestDate,
    string? RequestTime,
    string? Origin,
    string? CollectionDate,
    string? CollectionTime,
    IReadOnlyList<string> SourceLineIds,
    IReadOnlyList<string> UnresolvedFragments)
{
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(PatientName) &&
        !string.IsNullOrWhiteSpace(BirthDate) &&
        !string.IsNullOrWhiteSpace(RequestNumber) &&
        !string.IsNullOrWhiteSpace(RequestDate);
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SanitizedDisposition
{
    Active,
    Header,
    Footer,
    Reference,
    History,
    Method,
    BoilerplateNote,
    EmptyLabel,
    TypographicAttachment,
    TextContinuation,
}

public sealed record SanitizedLine(
    string Id,
    int SourceIndex,
    string OriginalText,
    string Text,
    SanitizedDisposition Disposition,
    IReadOnlyList<string> AppliedRuleIds,
    IReadOnlyList<string> SourceWordIds,
    IReadOnlyList<SuppressedTextSegment> SuppressedSegments);

public sealed record SuppressedTextSegment(
    string Text,
    SanitizedDisposition Disposition,
    string RuleId);
