using System.IO.Compression;
using ArsExtractum.App.Services;
using ArsExtractum.App.ViewModels;
using ArsExtractum.Core.Assembly;
using ArsExtractum.Core.Documents;
using ArsExtractum.Core.Pipeline;
using ArsExtractum.Core.DerivedMeasurements;
using ArsExtractum.Core.LaboratorySemantic;
using ArsExtractum.Core.OutputProjection;
using Xunit;

namespace ArsExtractum.Tests;

public sealed class DetailedReportExporterTests
{
    [Fact]
    public async Task ExportCreatesCanonicalStageArtifacts()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "ArsExtractum.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var destination = Path.Combine(temporaryDirectory, "report.zip");
            var capture = new CaptureDocument(
                "1.0",
                "doc-test",
                "synthetic.pdf",
                10,
                "hash",
                "test",
                [new CapturePage(1, 600, 800, 0, [], [])]);
            var reconstruction = new ReconstructedDocument(
                "1.0",
                "doc-test",
                "synthetic.pdf",
                [new ReconstructedPage(1, 600, 800, [], [], [], [])]);
            var sanitization = new SanitizedDocument(
                "1.0",
                "1.0",
                "doc-test",
                "synthetic.pdf",
                []);
            var stages = new Dictionary<string, StageExecutionResult>(StringComparer.Ordinal)
            {
                [StageIds.PdfPigCapture] = new(
                    new StageDescriptor(StageIds.PdfPigCapture, "Captura", "", "1.0", []),
                    capture,
                    "captura",
                    [],
                    TimeSpan.FromMilliseconds(10)),
                [StageIds.RawReconstruction] = new(
                    new StageDescriptor(
                        StageIds.RawReconstruction,
                        "Reconstrução",
                        "",
                        "1.0",
                        [StageIds.PdfPigCapture]),
                    reconstruction,
                    "reconstrução",
                    [],
                    TimeSpan.FromMilliseconds(5)),
                [StageIds.Sanitization] = new(
                    new StageDescriptor(
                        StageIds.Sanitization,
                        "Higienização",
                        "",
                        "1.0",
                        [StageIds.RawReconstruction]),
                    sanitization,
                    "higienização",
                    [],
                    TimeSpan.FromMilliseconds(2)),
            };
            var runs = new[]
            {
                new DocumentRunRecord(
                    "synthetic.pdf",
                    new DocumentExecution("synthetic.pdf", stages),
                    null),
            };
            var patientBatch = new PatientBatch(
                PatientEpisodeAssembler.CurrentSchemaVersion,
                PatientEpisodeAssembler.CurrentRulesVersion,
                [],
                []);

            await DetailedReportExporter.ExportAsync(destination, runs, patientBatch);

            using var archive = ZipFile.OpenRead(destination);
            var names = archive.Entries.Select(static entry => entry.FullName).ToHashSet();
            Assert.Contains("00-report.json", names);
            Assert.Contains("documents/001-synthetic/00-manifest.json", names);
            Assert.Contains("documents/001-synthetic/01-glyphs.jsonl", names);
            Assert.Contains("documents/001-synthetic/02-words.jsonl", names);
            Assert.Contains("documents/001-synthetic/04-reconstruction.json", names);
            Assert.Contains("documents/001-synthetic/05-reconstruction.txt", names);
            Assert.Contains("documents/001-synthetic/06-sanitization.json", names);
            Assert.Contains("documents/001-synthetic/07-sanitization.txt", names);
            Assert.Contains("batch/08-patient-assembly.json", names);
            Assert.Contains("batch/09-patient-assembly.txt", names);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SelectiveExportIncludesFinalProjectionAndExcludesUnselectedDocumentStages()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "ArsExtractum.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var destination = Path.Combine(temporaryDirectory, "selected.zip");
            var patientBatch = new PatientBatch(PatientEpisodeAssembler.CurrentSchemaVersion,
                PatientEpisodeAssembler.CurrentRulesVersion, [], []);
            var semantic = new SemanticPatientBatch(
                LaboratorySemanticExtractor.CurrentSchemaVersion,
                LaboratorySemanticExtractor.CurrentRulesVersion,
                "1.0.0", "fixture", [],
                new LaboratorySemanticCoverage(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), [])
            {
                DerivedMeasurementRulesVersion = DerivedMeasurementComputer.CurrentRulesVersion,
                DerivedMeasurementCoverage = new DerivedMeasurementCoverage(0, 0, 0, 0,
                    new Dictionary<string, int>(), 0, 0, 0),
            };
            var projection = OutputProjector.Project(new OutputProjectionInput(semantic));
            var options = new AuditExportOptions(false, false, false, true, true, true);

            await DetailedReportExporter.ExportAsync(destination, [], patientBatch, semantic, projection, options);

            using var archive = ZipFile.OpenRead(destination);
            var names = archive.Entries.Select(static entry => entry.FullName).ToHashSet(StringComparer.Ordinal);
            Assert.Contains("batch/08-patient-assembly.json", names);
            Assert.Contains("batch/10-semantic-patient-batch.json", names);
            Assert.Contains("batch/11-semantic-patient-batch.txt", names);
            Assert.Contains("batch/12-clinical-output-batch.json", names);
            Assert.Contains("batch/13-clinical-output.txt", names);
            Assert.DoesNotContain(names, static name => name.StartsWith("documents/", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
