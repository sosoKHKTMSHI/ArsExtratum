using ArsExtractum.Core.Documents;
using ArsExtractum.Core.Pipeline;
using ArsExtractum.Core.Reconstruction;
using ArsExtractum.Core.Sanitization;
using ArsExtractum.PdfPig;
using Xunit;

namespace ArsExtractum.Tests;

public sealed class PdfPigIntegrationSmokeTests
{
    [Fact]
    public async Task Teste05OrdersTheSplitCultureResultAfterItsLabel()
    {
        var path = Environment.GetEnvironmentVariable("ARS_EXTRACTUM_TESTE05_PDF");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var pipeline = new ProcessingPipeline(
        [
            new PdfPigCaptureStage(),
            new RawReconstructionStage(),
            new SanitizationStage(),
        ]);
        var execution = await pipeline.ExecuteAsync(
            new SourcePdf(Path.GetFileName(path), await File.ReadAllBytesAsync(path)),
            [StageIds.Sanitization]);
        var reconstruction = Assert.IsType<ReconstructedDocument>(
            execution.Stages[StageIds.RawReconstruction].Payload);
        var sanitized = Assert.IsType<SanitizedDocument>(
            execution.Stages[StageIds.Sanitization].Payload);
        Assert.All(sanitized.Pages, static page => Assert.True(page.Header.IsComplete));
        Assert.All(sanitized.Pages, static page => Assert.True(page.FooterRecognized));
        Assert.DoesNotContain(
            sanitized.Pages.SelectMany(static page => page.Lines),
            static line =>
                line.Disposition == SanitizedDisposition.Active &&
                line.Text.StartsWith("Laudo conferido", StringComparison.Ordinal));
        var lines = reconstruction.Pages.Single(static page => page.PageNumber == 32)
            .Lines.Select(static line => line.Text).ToArray();
        var labelIndex = Array.FindIndex(
            lines,
            static line =>
                line.StartsWith("CULTURAL", StringComparison.Ordinal) &&
                line.Contains("...", StringComparison.Ordinal));

        Assert.True(labelIndex >= 0);
        Assert.StartsWith("CULTURA PREJUDICADA", lines[labelIndex + 1], StringComparison.Ordinal);
        Assert.StartsWith("CONTAMINANTE", lines[labelIndex + 2], StringComparison.Ordinal);
        Assert.StartsWith("UMA NOVA", lines[labelIndex + 3], StringComparison.Ordinal);

        foreach (var pageNumber in new[] { 1, 6, 27, 56, 66, 72 })
        {
            var pageLines = reconstruction.Pages.Single(page => page.PageNumber == pageNumber)
                .Lines.Select(static line => line.Text).ToArray();
            var icoIndex = Array.FindIndex(
                pageLines,
                static line => line.StartsWith("ICO (", StringComparison.Ordinal));

            Assert.True(icoIndex >= 0);
            Assert.Equal("cutoff)", pageLines[icoIndex + 1]);
        }
    }

    [Fact]
    public async Task Teste06OrdersMultilineLabelsBeforeTheirResults()
    {
        var path = Environment.GetEnvironmentVariable("ARS_EXTRACTUM_TESTE06_PDF");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var pipeline = new ProcessingPipeline(
        [
            new PdfPigCaptureStage(),
            new RawReconstructionStage(),
        ]);
        var execution = await pipeline.ExecuteAsync(
            new SourcePdf(Path.GetFileName(path), await File.ReadAllBytesAsync(path)),
            [StageIds.RawReconstruction]);
        var reconstruction = Assert.IsType<ReconstructedDocument>(
            execution.Stages[StageIds.RawReconstruction].Payload);

        foreach (var pageNumber in new[] { 3, 34, 39, 47, 121, 191, 194, 207 })
        {
            var lines = reconstruction.Pages.Single(page => page.PageNumber == pageNumber)
                .Lines.Select(static line => line.Text).ToArray();
            var icoIndex = Array.FindIndex(
                lines,
                static line => line.StartsWith("ICO (", StringComparison.Ordinal));

            Assert.True(icoIndex >= 0);
            Assert.Equal("cutoff)", lines[icoIndex + 1]);
        }

        var hbsagLines = reconstruction.Pages.Single(static page => page.PageNumber == 226)
            .Lines.Select(static line => line.Text).ToArray();
        var hbsagIndex = Array.FindIndex(
            hbsagLines,
            static line => line.Contains("HBSAG", StringComparison.Ordinal));

        Assert.True(hbsagIndex >= 0);
        Assert.StartsWith("em gestante", hbsagLines[hbsagIndex + 1], StringComparison.Ordinal);
        Assert.Contains("REAGENTE", hbsagLines[hbsagIndex + 2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Teste09KeepsKnownMultilineLabelsInLogicalOrder()
    {
        var path = Environment.GetEnvironmentVariable("ARS_EXTRACTUM_TESTE09_PDF");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var pipeline = new ProcessingPipeline(
        [
            new PdfPigCaptureStage(),
            new RawReconstructionStage(),
        ]);
        var execution = await pipeline.ExecuteAsync(
            new SourcePdf(Path.GetFileName(path), await File.ReadAllBytesAsync(path)),
            [StageIds.RawReconstruction]);
        var reconstruction = Assert.IsType<ReconstructedDocument>(
            execution.Stages[StageIds.RawReconstruction].Payload);

        var antiHcvLines = reconstruction.Pages.Single(static page => page.PageNumber == 263)
            .Lines.Select(static line => line.Text).ToArray();
        var antiHcvIndex = Array.FindIndex(
            antiHcvLines,
            static line => line.StartsWith("ANTI-HCV", StringComparison.Ordinal));
        Assert.True(antiHcvIndex >= 0);
        Assert.StartsWith(", parceiro", antiHcvLines[antiHcvIndex + 1], StringComparison.Ordinal);
        Assert.Contains("REAGENTE", antiHcvLines[antiHcvIndex + 2], StringComparison.Ordinal);

        var psaLines = reconstruction.Pages.Single(static page => page.PageNumber == 330)
            .Lines.Select(static line => line.Text).ToArray();
        var psaIndex = Array.FindIndex(
            psaLines,
            static line =>
                line.Contains("PROST", StringComparison.Ordinal) &&
                line.Contains("LIVRE", StringComparison.Ordinal));
        Assert.True(psaIndex >= 0);
        Assert.StartsWith("(PSA LIVRE)", psaLines[psaIndex + 1], StringComparison.Ordinal);
        Assert.True(char.IsAsciiDigit(psaLines[psaIndex + 2][0]));
    }

    [Fact]
    public async Task Teste01OrdersLegacyTshLabelBeforeValue()
    {
        var path = Environment.GetEnvironmentVariable("ARS_EXTRACTUM_TESTE01_PDF");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var pipeline = new ProcessingPipeline(
        [
            new PdfPigCaptureStage(),
            new RawReconstructionStage(),
        ]);
        var execution = await pipeline.ExecuteAsync(
            new SourcePdf(Path.GetFileName(path), await File.ReadAllBytesAsync(path)),
            [StageIds.RawReconstruction]);
        var reconstruction = Assert.IsType<ReconstructedDocument>(
            execution.Stages[StageIds.RawReconstruction].Payload);

        foreach (var pageNumber in new[] { 3, 91 })
        {
            var lines = reconstruction.Pages.Single(page => page.PageNumber == pageNumber)
                .Lines.Select(static line => line.Text).ToArray();
            var labelIndex = Array.FindIndex(
                lines,
                static line => line.StartsWith("(TSH)", StringComparison.Ordinal));
            Assert.True(labelIndex >= 0);
            Assert.True(char.IsAsciiDigit(lines[labelIndex + 1][0]));
            Assert.Contains("uUI/mL", lines[labelIndex + 1], StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Teste04ProducesTraceableCaptureAndReadableReconstruction()
    {
        var path = Environment.GetEnvironmentVariable("ARS_EXTRACTUM_TESTE04_PDF");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        Assert.True(File.Exists(path), "O PDF configurado para o smoke test não foi encontrado.");
        var pipeline = new ProcessingPipeline(
        [
            new PdfPigCaptureStage(),
            new RawReconstructionStage(),
        ]);
        var execution = await pipeline.ExecuteAsync(
            new SourcePdf(Path.GetFileName(path), await File.ReadAllBytesAsync(path)),
            [StageIds.RawReconstruction]);
        var capture = Assert.IsType<CaptureDocument>(
            execution.Stages[StageIds.PdfPigCapture].Payload);
        var reconstruction = Assert.IsType<ReconstructedDocument>(
            execution.Stages[StageIds.RawReconstruction].Payload);

        Assert.Equal(7, capture.PageCount);
        Assert.Equal(13_625, capture.GlyphCount);
        Assert.Equal(1_940, capture.WordCount);
        Assert.All(
            capture.Pages.SelectMany(static page => page.Words),
            static word => Assert.NotEmpty(word.GlyphIds));
        Assert.Equal(0, reconstruction.UnresolvedWordCount);
        Assert.Contains(
            reconstruction.Pages.SelectMany(static page => page.Lines),
            static line => line.Text.Contains("PLAQUETAS", StringComparison.Ordinal));
        var hemogramPage = reconstruction.Pages.Single(static page => page.PageNumber == 2);
        Assert.Contains(
            hemogramPage.Lines,
            static line =>
                line.Text.Contains("Hemácias", StringComparison.Ordinal) &&
                line.Text.Contains("p/mm3", StringComparison.Ordinal));
        Assert.DoesNotContain(
            hemogramPage.Lines,
            static line => string.Equals(line.Text, "3", StringComparison.Ordinal));

        var igmLines = reconstruction.Pages.Single(static page => page.PageNumber == 3)
            .Lines.Select(static line => line.Text).ToArray();
        var igmLabelIndex = Array.FindIndex(
            igmLines,
            static line => line.StartsWith("IgM", StringComparison.Ordinal));
        Assert.True(igmLabelIndex >= 0);
        Assert.Contains("REAGENTE", igmLines[igmLabelIndex + 1], StringComparison.Ordinal);
        Assert.Equal(
            "cutoff)",
            igmLines[Array.FindIndex(
                igmLines,
                static line => line.StartsWith("ICO", StringComparison.Ordinal)) + 1]);

        foreach (var expected in new[]
                 {
                     (PageNumber: 5, ResultStart: "Pesquisa"),
                     (PageNumber: 6, ResultStart: "NÃO HOUVE"),
                 })
        {
            var cultureLines = reconstruction.Pages.Single(page => page.PageNumber == expected.PageNumber)
                .Lines.Select(static line => line.Text).ToArray();
            var labelIndex = Array.FindIndex(
                cultureLines,
                static line =>
                    line.StartsWith("CULTURAL", StringComparison.Ordinal) &&
                    line.Contains("...", StringComparison.Ordinal));
            Assert.True(labelIndex >= 0);
            Assert.StartsWith(
                expected.ResultStart,
                cultureLines[labelIndex + 1],
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Teste02ReattachesGasometrySubscripts()
    {
        var path = Environment.GetEnvironmentVariable("ARS_EXTRACTUM_TESTE02_PDF");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        Assert.True(File.Exists(path), "O PDF configurado para o teste de gasometria não foi encontrado.");
        var pipeline = new ProcessingPipeline(
        [
            new PdfPigCaptureStage(),
            new RawReconstructionStage(),
        ]);
        var execution = await pipeline.ExecuteAsync(
            new SourcePdf(Path.GetFileName(path), await File.ReadAllBytesAsync(path)),
            [StageIds.RawReconstruction]);
        var reconstruction = Assert.IsType<ReconstructedDocument>(
            execution.Stages[StageIds.RawReconstruction].Payload);
        var gasometryPage = reconstruction.Pages.Single(static page => page.PageNumber == 23);
        var lines = gasometryPage.Lines.Select(static line => line.Text).ToArray();

        Assert.Contains(lines, static line => line.StartsWith("pO2", StringComparison.Ordinal));
        Assert.Contains(lines, static line => line.StartsWith("pCO2", StringComparison.Ordinal));
        Assert.Contains(lines, static line => line.StartsWith("HCO3", StringComparison.Ordinal));
        Assert.Contains(lines, static line => line.StartsWith("CO2 TOTAL", StringComparison.Ordinal));
        Assert.Contains(lines, static line => line.StartsWith("O2 SATURAÇÃO", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, static line => line is "2" or "3");
    }
}
