using ArsExtractum.App.ViewModels;
using ArsExtractum.Core.Assembly;
using ArsExtractum.Core.Pipeline;
using ArsExtractum.Core.Reconstruction;
using ArsExtractum.Core.Sanitization;
using ArsExtractum.PdfPig;
using Xunit;

namespace ArsExtractum.Tests;

public sealed class PatientAssemblyIntegrationSmokeTests
{
    [Fact]
    public async Task FailedDocumentIsRetainedInAssemblyLedger()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "ArsExtractum.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var invalidPdf = Path.Combine(temporaryDirectory, "invalid.pdf");
            await File.WriteAllTextAsync(invalidPdf, "not a PDF");
            var pipeline = new ProcessingPipeline(
            [
                new PdfPigCaptureStage(),
                new RawReconstructionStage(),
                new SanitizationStage(),
            ]);
            var viewModel = new MainWindowViewModel(pipeline);
            viewModel.AddFiles([invalidPdf]);
            await viewModel.ProcessAsync();

            var batch = Assert.IsType<PatientBatch>(viewModel.PatientBatch);
            var entry = Assert.Single(batch.Ledger);
            Assert.Equal("Failed", entry.Disposition);
            Assert.Equal("invalid.pdf", entry.FileName);
            Assert.Null(entry.SourcePageCount);
            Assert.Contains("assinatura PDF", entry.Reason, StringComparison.OrdinalIgnoreCase);
            viewModel.SelectedStage = viewModel.Stages.Single(static stage =>
                stage.Id == StageIds.PatientEpisodeAssembly);
            Assert.Contains("Documentos com falha: 1", viewModel.OutputText, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ProvidedMultiPatientBatchCreatesTwoPatientGroups()
    {
        var configured = Environment.GetEnvironmentVariable("ARS_EXTRACTUM_PATIENT_BATCH_PDFS");
        if (string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        var paths = configured.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(7, paths.Length);
        var pipeline = new ProcessingPipeline(
        [
            new PdfPigCaptureStage(),
            new RawReconstructionStage(),
            new SanitizationStage(),
        ]);
        var viewModel = new MainWindowViewModel(pipeline);
        viewModel.AddFiles(paths);
        await viewModel.ProcessAsync();
        var batch = Assert.IsType<PatientBatch>(viewModel.PatientBatch);

        Assert.Equal(2, batch.Patients.Count);
        Assert.Empty(batch.UnassignedDocuments);
        Assert.Equal(7, viewModel.CompletedRuns.Count);
        Assert.Equal(2, viewModel.Patients.Count);
        Assert.NotNull(viewModel.SelectedPatient);
        Assert.Equal(7, batch.Patients.Sum(static patient => patient.SourceDocuments.Count));
        Assert.Equal([3, 4], batch.Patients
            .Select(static patient => patient.SourceDocuments.Count)
            .Order()
            .ToArray());
        Assert.All(batch.Patients, static patient => Assert.NotEmpty(patient.Episodes));
        viewModel.SelectedStage = viewModel.Stages.Single(static stage =>
            stage.Id == StageIds.PatientEpisodeAssembly);
        foreach (var patient in batch.Patients)
        {
            viewModel.SelectedPatient = viewModel.Patients.Single(item =>
                item.Patient.PatientKey == patient.PatientKey);
            var output = viewModel.OutputText;
            Assert.Contains("[CONTEÚDO]", output, StringComparison.Ordinal);
            Assert.All(
                patient.SourceDocuments,
                source => Assert.Contains(source.FileName, output, StringComparison.Ordinal));
            Assert.DoesNotContain(
                batch.Patients.Where(other => other.PatientKey != patient.PatientKey)
                    .SelectMany(static other => other.SourceDocuments),
                source => output.Contains(source.FileName, StringComparison.Ordinal));
        }
    }
}
