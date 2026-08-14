namespace ArsExtractum.Core.Documents;

public sealed record CaptureDocument(
    string SchemaVersion,
    string DocumentId,
    string FileName,
    long ByteLength,
    string Sha256,
    string PdfPigVersion,
    IReadOnlyList<CapturePage> Pages)
{
    public int PageCount => Pages.Count;

    public int GlyphCount => Pages.Sum(static page => page.Glyphs.Count);

    public int WordCount => Pages.Sum(static page => page.Words.Count);
}

public sealed record CapturePage(
    int PageNumber,
    double Width,
    double Height,
    int RotationDegrees,
    IReadOnlyList<CapturedGlyph> Glyphs,
    IReadOnlyList<CapturedWord> Words);

public sealed record CapturedGlyph(
    string Id,
    int Index,
    string Text,
    PdfBounds Bounds,
    PdfPoint StartBaseline,
    PdfPoint EndBaseline,
    string? FontName,
    double? FontSize,
    string TextOrientation);

public sealed record CapturedWord(
    string Id,
    int Index,
    string Text,
    PdfBounds Bounds,
    double BaselineY,
    string? FontName,
    IReadOnlyList<string> GlyphIds,
    string TextOrientation);
