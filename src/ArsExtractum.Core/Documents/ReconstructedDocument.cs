namespace ArsExtractum.Core.Documents;

public sealed record ReconstructedDocument(
    string SchemaVersion,
    string DocumentId,
    string FileName,
    IReadOnlyList<ReconstructedPage> Pages)
{
    public int LineCount => Pages.Sum(static page => page.Lines.Count);

    public int CellCount => Pages.Sum(static page => page.Lines.Sum(static line => line.Cells.Count));

    public int UnresolvedWordCount => Pages.Sum(static page => page.UnresolvedWordIds.Count);

    public int UnassignedGlyphCount => Pages.Sum(static page => page.UnassignedGlyphIds.Count);

    public int TypographicAttachmentCount =>
        Pages.Sum(static page => page.TypographicAttachments.Count);
}

public sealed record ReconstructedPage(
    int PageNumber,
    double Width,
    double Height,
    IReadOnlyList<ReconstructedLine> Lines,
    IReadOnlyList<string> UnresolvedWordIds,
    IReadOnlyList<string> UnassignedGlyphIds,
    IReadOnlyList<TypographicAttachment> TypographicAttachments);

public sealed record TypographicAttachment(
    string WordId,
    string BaseWordId,
    string Relation);

public sealed record ReconstructedLine(
    string Id,
    int Index,
    string Text,
    string DisplayText,
    PdfBounds Bounds,
    double BaselineY,
    IReadOnlyList<string> WordIds,
    IReadOnlyList<ReconstructedCell> Cells);

public sealed record ReconstructedCell(
    string Id,
    int Index,
    string Text,
    PdfBounds Bounds,
    IReadOnlyList<string> WordIds);
