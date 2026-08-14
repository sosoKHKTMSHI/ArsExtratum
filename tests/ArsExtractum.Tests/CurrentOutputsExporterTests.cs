using System.IO.Compression;
using ArsExtractum.App.Services;
using ArsExtractum.App.ViewModels;
using ArsExtractum.Core.Assembly;
using ArsExtractum.Core.DerivedMeasurements;
using ArsExtractum.Core.LaboratorySemantic;
using ArsExtractum.Core.OutputProjection;
using System.Text.Json;
using Xunit;

namespace ArsExtractum.Tests;

public sealed class CurrentOutputsExporterTests
{
    [Fact]
    public async Task ExportCreatesSingleCompactOutputPackage()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ArsExtractum.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var destination = Path.Combine(directory, "outputs.zip");
            var patientBatch = new PatientBatch(
                PatientEpisodeAssembler.CurrentSchemaVersion,
                PatientEpisodeAssembler.CurrentRulesVersion,
                [],
                []);
            var semanticBatch = new LaboratorySemanticExtractor().Extract(
                new LaboratorySemanticExtractionInput(patientBatch));
            semanticBatch = DerivedMeasurementComputer.Enrich(
                new DerivedMeasurementComputationInput(semanticBatch));
            var clinicalOutput = OutputProjector.Project(new OutputProjectionInput(semanticBatch));
            await CurrentOutputsExporter.ExportAsync(
                destination,
                [new DocumentRunRecord("failed.pdf", null, "falha esperada")],
                patientBatch,
                semanticBatch,
                clinicalOutput);

            using var archive = ZipFile.OpenRead(destination);
            Assert.Equal(
                ["README.txt", "culture-review.txt", "current-output.json", "current-output.txt"],
                archive.Entries.Select(static entry => entry.FullName).Order(StringComparer.Ordinal));
            Assert.DoesNotContain(archive.Entries, static entry =>
                entry.FullName.Contains("reconstruction", StringComparison.OrdinalIgnoreCase) ||
                entry.FullName.Contains("sanitization", StringComparison.OrdinalIgnoreCase));
            using var stream = archive.GetEntry("current-output.json")!.Open();
            using var json = JsonDocument.Parse(stream);
            Assert.Equal("current-outputs/1.2", json.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal(
                LaboratorySemanticExtractor.CurrentSchemaVersion,
                json.RootElement.GetProperty("laboratorySemanticExtraction")
                    .GetProperty("schemaVersion").GetString());
            var semanticJson = json.RootElement.GetProperty("laboratorySemanticExtraction");
            Assert.Equal(
                DerivedMeasurementComputer.CurrentRulesVersion,
                semanticJson.GetProperty("derivedMeasurementRulesVersion").GetString());
            Assert.Equal(0, semanticJson.GetProperty("derivedMeasurementCoverage")
                .GetProperty("derivedRecordCount").GetInt32());
            Assert.False(json.RootElement.TryGetProperty("derivedMeasurementComputation", out _));
            Assert.Equal(OutputProjector.CurrentSchemaVersion,
                json.RootElement.GetProperty("outputProjection").GetProperty("schemaVersion").GetString());
            using var textReader = new StreamReader(archive.GetEntry("current-output.txt")!.Open());
            var text = await textReader.ReadToEndAsync();
            Assert.Contains("SEMANTIC PATIENT BATCH", text, StringComparison.Ordinal);
            Assert.DoesNotContain("DERIVED MEASUREMENT BATCH", text, StringComparison.Ordinal);
            Assert.Contains("OUTPUT CLÍNICO FINAL", text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
