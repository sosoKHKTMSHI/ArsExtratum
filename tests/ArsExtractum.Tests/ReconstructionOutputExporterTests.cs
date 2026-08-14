using System.IO.Compression;
using System.Text.Json;
using ArsExtractum.App.Services;
using ArsExtractum.App.ViewModels;
using ArsExtractum.Core.Documents;
using ArsExtractum.Core.Pipeline;
using Xunit;

namespace ArsExtractum.Tests;

public sealed class ReconstructionOutputExporterTests
{
    [Fact]
    public async Task ExportersPreserveSeparateDocumentsInZipAndSingleJson()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "ArsExtractum.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var generatedAt = new DateTimeOffset(2026, 8, 2, 11, 58, 0, TimeSpan.FromHours(-3));
            var runs = new[]
            {
                CreateRun("Teste04.pdf", "doc-04"),
                CreateRun("Teste05.pdf", "doc-05"),
            };
            var zipPath = Path.Combine(temporaryDirectory, "outputs.zip");
            var bundlePath = Path.Combine(temporaryDirectory, "outputs.json");

            await ReconstructionOutputExporter.ExportZipAsync(zipPath, runs, generatedAt);
            await ReconstructionOutputExporter.ExportSingleFileAsync(bundlePath, runs, generatedAt);

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                Assert.Equal(
                    [
                        "Teste04-reconstruction-02081158.json",
                        "Teste05-reconstruction-02081158.json",
                    ],
                    archive.Entries.Select(static entry => entry.FullName).Order());
            }

            await using var bundleStream = File.OpenRead(bundlePath);
            using var bundle = await JsonDocument.ParseAsync(bundleStream);
            var root = bundle.RootElement;
            Assert.Equal("ars-extractum.reconstruction-bundle", root.GetProperty("type").GetString());
            Assert.Equal(2, root.GetProperty("documentCount").GetInt32());
            var documents = root.GetProperty("documents");
            Assert.Equal("Teste04.pdf", documents[0].GetProperty("sourceFileName").GetString());
            Assert.Equal("doc-04", documents[0].GetProperty("reconstruction").GetProperty("documentId").GetString());
            Assert.Equal("Teste05.pdf", documents[1].GetProperty("sourceFileName").GetString());
            Assert.Equal("doc-05", documents[1].GetProperty("reconstruction").GetProperty("documentId").GetString());
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static DocumentRunRecord CreateRun(string fileName, string documentId)
    {
        var reconstruction = new ReconstructedDocument(
            "1.2",
            documentId,
            fileName,
            [new ReconstructedPage(1, 600, 800, [], [], [], [])]);
        var descriptor = new StageDescriptor(
            StageIds.RawReconstruction,
            "Reconstrução bruta",
            string.Empty,
            "1.2",
            [StageIds.PdfPigCapture]);
        var stages = new Dictionary<string, StageExecutionResult>(StringComparer.Ordinal)
        {
            [StageIds.RawReconstruction] = new(
                descriptor,
                reconstruction,
                "reconstruction",
                [],
                TimeSpan.Zero),
        };
        return new DocumentRunRecord(
            fileName,
            new DocumentExecution(fileName, stages),
            null);
    }
}
