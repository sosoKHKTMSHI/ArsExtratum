using ArsExtractum.Core.Documents;
using ArsExtractum.Core.Pipeline;
using ArsExtractum.Core.Reconstruction;
using ArsExtractum.PdfPig;
using Xunit;
using Xunit.Abstractions;

namespace ArsExtractum.Tests;

public sealed class ReferenceCorpusIntegrityTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ReconstructionRemainsLosslessAcrossReferenceCorpus()
    {
        var directory = Environment.GetEnvironmentVariable(
            "ARS_EXTRACTUM_REFERENCE_PDF_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var paths = Directory.GetFiles(directory, "Teste*.pdf")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(5, paths.Length);
        var pipeline = new ProcessingPipeline(
        [
            new PdfPigCaptureStage(),
            new RawReconstructionStage(),
        ]);

        foreach (var path in paths)
        {
            var execution = await pipeline.ExecuteAsync(
                new SourcePdf(Path.GetFileName(path), await File.ReadAllBytesAsync(path)),
                [StageIds.RawReconstruction]);
            var capture = Assert.IsType<CaptureDocument>(
                execution.Stages[StageIds.PdfPigCapture].Payload);
            var reconstruction = Assert.IsType<ReconstructedDocument>(
                execution.Stages[StageIds.RawReconstruction].Payload);
            var reconstructedWordIds = reconstruction.Pages
                .SelectMany(static page => page.Lines)
                .SelectMany(static line => line.WordIds)
                .ToArray();

            Assert.Equal(capture.WordCount, reconstructedWordIds.Length);
            Assert.Equal(capture.WordCount, reconstructedWordIds.Distinct().Count());
            Assert.Equal(0, reconstruction.UnresolvedWordCount);
            Assert.All(
                reconstruction.Pages.SelectMany(static page => page.TypographicAttachments),
                attachment =>
                {
                    Assert.Contains(attachment.WordId, reconstructedWordIds);
                    Assert.Contains(attachment.BaseWordId, reconstructedWordIds);
                });

            var standaloneScripts = reconstruction.Pages
                .SelectMany(static page => page.Lines)
                .Count(static line => line.Text is "2" or "3");
            output.WriteLine(
                $"{capture.FileName}: words={capture.WordCount}; " +
                $"attachments={reconstruction.TypographicAttachmentCount}; " +
                $"standalone-2-or-3={standaloneScripts}");
        }
    }
}
