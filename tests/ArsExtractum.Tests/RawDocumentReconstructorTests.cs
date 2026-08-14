using ArsExtractum.Core.Documents;
using ArsExtractum.Core.Reconstruction;
using Xunit;

namespace ArsExtractum.Tests;

public sealed class RawDocumentReconstructorTests
{
    [Fact]
    public void ReconstructPreservesWordsAndSeparatesLargeHorizontalGaps()
    {
        var glyphs = new[]
        {
            Glyph("g1", 0, "Nome", 10, 100, 30, 108),
            Glyph("g2", 1, "Paciente", 34, 100, 70, 108),
            Glyph("g3", 2, "Sexo", 300, 100, 320, 108),
            Glyph("g4", 3, "POTÁSSIO", 10, 80, 60, 88),
            Glyph("g5", 4, "4,5", 65, 80, 80, 88),
        };
        var words = new[]
        {
            Word("w1", 0, "Nome", 10, 100, 30, 108, "g1"),
            Word("w2", 1, "Paciente", 34, 100, 70, 108, "g2"),
            Word("w3", 2, "Sexo", 300, 100, 320, 108, "g3"),
            Word("w4", 3, "POTÁSSIO", 10, 80, 60, 88, "g4"),
            Word("w5", 4, "4,5", 65, 80, 80, 88, "g5"),
        };
        var source = new CaptureDocument(
            "1.0",
            "doc-test",
            "synthetic.pdf",
            100,
            "hash",
            "test",
            [new CapturePage(1, 600, 800, 0, glyphs, words)]);

        var result = RawDocumentReconstructor.Reconstruct(source);

        Assert.Equal(2, result.LineCount);
        Assert.Equal(2, result.Pages[0].Lines[0].Cells.Count);
        Assert.Equal("Nome Paciente | Sexo", result.Pages[0].Lines[0].DisplayText);
        Assert.Empty(result.Pages[0].UnresolvedWordIds);
        Assert.Empty(result.Pages[0].UnassignedGlyphIds);
        Assert.Equal(
            words.Select(static word => word.Id).Order(),
            result.Pages[0].Lines.SelectMany(static line => line.WordIds).Order());
    }

    [Fact]
    public void ReconstructAttachesNumericScriptsWithoutRemovingLegitimateValues()
    {
        var words = new[]
        {
            WordWithBaseline("w1", 0, "pO", 10, 100, 20, 108, 100, "g1"),
            WordWithBaseline("w2", 1, "resultado", 25, 100, 80, 108, 100, "g2"),
            WordWithBaseline("w3", 2, "2", 21, 97.8, 23, 102, 97.8, "g3"),
            WordWithBaseline("w4", 3, "p/mm", 10, 80, 25, 88, 80, "g4"),
            WordWithBaseline("w5", 4, "3", 26, 84.2, 28, 88.4, 84.2, "g5"),
            WordWithBaseline("w6", 5, "2", 100, 60, 108, 68, 60, "g6"),
        };
        var glyphs = words
            .Select((word, index) => Glyph(
                word.GlyphIds[0],
                index,
                word.Text,
                word.Bounds.Left,
                word.Bounds.Bottom,
                word.Bounds.Right,
                word.Bounds.Top))
            .ToArray();
        var source = new CaptureDocument(
            "1.0",
            "doc-test",
            "synthetic.pdf",
            100,
            "hash",
            "test",
            [new CapturePage(1, 600, 800, 0, glyphs, words)]);

        var result = RawDocumentReconstructor.Reconstruct(source);
        var lines = result.Pages[0].Lines.Select(static line => line.Text).ToArray();

        Assert.Contains("pO2 resultado", lines);
        Assert.Contains("p/mm3", lines);
        Assert.Contains("2", lines);
        Assert.Equal(2, result.TypographicAttachmentCount);
        Assert.Equal(6, result.Pages[0].Lines.SelectMany(static line => line.WordIds).Count());
        Assert.Empty(result.Pages[0].UnresolvedWordIds);
    }

    [Fact]
    public void ReconstructOrdersStaggeredFieldLabelsBeforeTheirValues()
    {
        var words = new[]
        {
            WordWithBaseline("w1", 0, "EXAME", 10, 120, 50, 126.6, 120, "g1"),
            WordWithBaseline("w2", 1, "2,80", 200, 114.6, 230, 121.2, 114.6, "g2"),
            WordWithBaseline("w3", 2, "(TSH).........:", 10, 109.2, 100, 115.8, 109.2, "g3"),
            WordWithBaseline("w4", 3, "Pesquisa", 200, 84.6, 250, 91.2, 84.6, "g4"),
            WordWithBaseline("w5", 4, "CULTURAL........:", 10, 79.2, 150, 85.8, 79.2, "g5"),
            WordWithBaseline("w6", 5, "continuação", 200, 73.8, 260, 80.4, 73.8, "g6"),
            WordWithBaseline("w7", 6, "NÃO REAGENTE", 200, 64.6, 280, 71.2, 64.6, "g7"),
            WordWithBaseline("w8", 7, "IgM........:", 10, 59.2, 100, 65.8, 59.2, "g8"),
            WordWithBaseline("w9", 8, "ICO (índice de", 120, 45, 190, 51.6, 45, "g9"),
            WordWithBaseline("w10", 9, "0,10", 200, 40, 230, 46.6, 40, "g10"),
            WordWithBaseline("w11", 10, "cutoff)", 10, 35, 60, 41.6, 35, "g11"),
        };
        var glyphs = words
            .Select((word, index) => Glyph(
                word.GlyphIds[0],
                index,
                word.Text,
                word.Bounds.Left,
                word.Bounds.Bottom,
                word.Bounds.Right,
                word.Bounds.Top))
            .ToArray();
        var source = new CaptureDocument(
            "1.0",
            "doc-test",
            "synthetic.pdf",
            100,
            "hash",
            "test",
            [new CapturePage(1, 600, 800, 0, glyphs, words)]);

        var lines = RawDocumentReconstructor.Reconstruct(source)
            .Pages[0].Lines.Select(static line => line.Text).ToArray();

        Assert.True(Array.IndexOf(lines, "(TSH).........:") < Array.IndexOf(lines, "2,80"));
        Assert.Equal(
            ["CULTURAL........:", "Pesquisa", "continuação"],
            lines.Skip(Array.IndexOf(lines, "CULTURAL........:")).Take(3));
        Assert.True(Array.IndexOf(lines, "IgM........:") < Array.IndexOf(lines, "NÃO REAGENTE"));
        Assert.Equal("cutoff)", lines[Array.IndexOf(lines, "ICO (índice de") + 1]);
        Assert.Equal(11, lines.Length);
    }

    [Fact]
    public void ReconstructSplitsInlineContinuationAfterAStaggeredFieldLabel()
    {
        var words = new[]
        {
            WordWithBaseline("w1", 0, "CULTURA PREJUDICADA", 200, 30, 330, 36.6, 30, "g1"),
            WordWithBaseline("w2", 1, "CULTURAL........:", 10, 24.6, 150, 31.2, 24.6, "g2"),
            WordWithBaseline("w3", 2, "CONTAMINANTE", 200, 24.6, 290, 31.2, 24.6, "g3"),
            WordWithBaseline("w4", 3, "NOVA SOLICITAÇÃO", 200, 19.2, 300, 25.8, 19.2, "g4"),
        };
        var glyphs = words
            .Select((word, index) => Glyph(
                word.GlyphIds[0],
                index,
                word.Text,
                word.Bounds.Left,
                word.Bounds.Bottom,
                word.Bounds.Right,
                word.Bounds.Top))
            .ToArray();
        var source = new CaptureDocument(
            "1.0",
            "doc-test",
            "synthetic.pdf",
            100,
            "hash",
            "test",
            [new CapturePage(1, 600, 800, 0, glyphs, words)]);

        var lines = RawDocumentReconstructor.Reconstruct(source)
            .Pages[0].Lines.Select(static line => line.Text).ToArray();

        Assert.Equal(
            ["CULTURAL........:", "CULTURA PREJUDICADA", "CONTAMINANTE", "NOVA SOLICITAÇÃO"],
            lines);
        Assert.Equal(4, lines.Length);
    }

    [Fact]
    public void ReconstructOrdersWrappedFieldLabelBeforeRightHandContent()
    {
        var words = new[]
        {
            WordWithBaseline("w1", 0, "Pesquisa de antigeno", 10, 100, 150, 106.6, 100, "g1"),
            WordWithBaseline("w2", 1, "NAO REAGENTE", 162, 94.6, 230, 101.2, 94.6, "g2"),
            WordWithBaseline("w3", 2, "Valores de Referencia", 300, 94.6, 390, 101.2, 94.6, "g3"),
            WordWithBaseline("w4", 3, "em", 10, 89.2, 20, 95.8, 89.2, "g4"),
            WordWithBaseline("w5", 4, "gestante........:", 24, 89.2, 150, 95.8, 89.2, "g5"),
        };
        var glyphs = words
            .Select((word, index) => Glyph(
                word.GlyphIds[0],
                index,
                word.Text,
                word.Bounds.Left,
                word.Bounds.Bottom,
                word.Bounds.Right,
                word.Bounds.Top))
            .ToArray();
        var source = new CaptureDocument(
            "1.0",
            "doc-test",
            "synthetic.pdf",
            100,
            "hash",
            "test",
            [new CapturePage(1, 600, 800, 0, glyphs, words)]);

        var page = RawDocumentReconstructor.Reconstruct(source).Pages[0];
        var lines = page.Lines.Select(static line => line.Text).ToArray();

        Assert.Equal(
            ["Pesquisa de antigeno", "em gestante........:", "NAO REAGENTE Valores de Referencia"],
            lines);
        Assert.Equal(words.Length, page.Lines.SelectMany(static line => line.WordIds).Count());
        Assert.Empty(page.UnresolvedWordIds);
    }

    private static CapturedGlyph Glyph(
        string id,
        int index,
        string text,
        double left,
        double bottom,
        double right,
        double top) => new(
            id,
            index,
            text,
            new PdfBounds(left, bottom, right, top),
            new PdfPoint(left, bottom),
            new PdfPoint(right, bottom),
            "TestFont",
            10,
            "Horizontal");

    private static CapturedWord Word(
        string id,
        int index,
        string text,
        double left,
        double bottom,
        double right,
        double top,
        string glyphId) => new(
            id,
            index,
            text,
            new PdfBounds(left, bottom, right, top),
            bottom,
            "TestFont",
            [glyphId],
            "Horizontal");

    private static CapturedWord WordWithBaseline(
        string id,
        int index,
        string text,
        double left,
        double bottom,
        double right,
        double top,
        double baseline,
        string glyphId) => new(
            id,
            index,
            text,
            new PdfBounds(left, bottom, right, top),
            baseline,
            "TestFont",
            [glyphId],
            "Horizontal");
}
