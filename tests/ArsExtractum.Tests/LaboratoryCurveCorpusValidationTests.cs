using ArsExtractum.App;
using ArsExtractum.App.ViewModels;
using ArsExtractum.Core.LaboratoryCurves;
using ArsExtractum.Core.LaboratorySemantic;
using ArsExtractum.Core.Pipeline;
using ArsExtractum.Core.Reconstruction;
using ArsExtractum.Core.Sanitization;
using ArsExtractum.PdfPig;
using Xunit;
using Xunit.Abstractions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ArsExtractum.Tests;

public sealed class LaboratoryCurveCorpusValidationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ConfiguredReferencePdfsCoverEveryAuthorizedCurveOption()
    {
        var configured = Environment.GetEnvironmentVariable("ARS_EXTRACTUM_CURVE_REFERENCE_PDFS");
        if (string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        var paths = configured.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var union = new HashSet<string>(StringComparer.Ordinal);
        var youngFormProjected = false;
        foreach (var path in paths)
        {
            var pipeline = new ProcessingPipeline(
            [
                new PdfPigCaptureStage(),
                new RawReconstructionStage(),
                new SanitizationStage(),
            ]);
            var viewModel = new MainWindowViewModel(pipeline);
            viewModel.AddFiles([path]);
            await viewModel.ProcessAsync();
            Assert.NotNull(viewModel.SemanticPatientBatch);
            Assert.True(viewModel.SemanticPatientBatch.DerivedMeasurementCoverage?.IsComplete);

            var fileOptions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var patient in viewModel.SemanticPatientBatch.Patients)
            {
                var available = LaboratoryCurveProjector.AvailableOptions(
                    viewModel.SemanticPatientBatch, patient.PatientKey);
                output.WriteLine($"PATIENT {patient.PatientKey}: {string.Join(",", available.Select(static option => option.Key))}");
                foreach (var option in available)
                {
                    fileOptions.Add(option.Key);
                    union.Add(option.Key);
                }

                if (available.Count > 0)
                {
                    var projection = LaboratoryCurveProjector.Project(new LaboratoryCurveProjectionInput(
                        viewModel.SemanticPatientBatch, patient.PatientKey,
                        available.Select(static option => option.Key).ToArray(),
                        new LaboratoryCurveFilter(LaboratoryCurveFilterMode.All), true,
                        new DateOnly(2026, 8, 16)));
                    Assert.All(projection.Series, static series => Assert.NotEmpty(series.Points));
                    if (projection.Series.Where(static series =>
                            series.Key == LaboratoryCurveDefinitions.LeukogramFractions)
                        .SelectMany(static series => series.Points)
                        .SelectMany(static point => point.Values)
                        .Any(static value => value.Label is "Mielócitos" or "Metamielócitos" or "Blastos"))
                    {
                        youngFormProjected = true;
                        output.WriteLine($"YOUNG-FORMS {Path.GetFileName(path)}");
                    }
                }
            }

            output.WriteLine($"{Path.GetFileName(path)}: {string.Join(",", fileOptions.Order(StringComparer.Ordinal))}");

            if (string.Equals(Path.GetFileName(path), "Teste01.pdf", StringComparison.OrdinalIgnoreCase))
            {
                var referencePatient = viewModel.SemanticPatientBatch.Patients.Single(static patient =>
                    string.Equals(patient.Identity.PatientName, "SENHORINHA ANTUNES", StringComparison.Ordinal));
                var referenceOptions = LaboratoryCurveProjector.AvailableOptions(
                    viewModel.SemanticPatientBatch, referencePatient.PatientKey);
                var referenceProjection = LaboratoryCurveProjector.Project(new LaboratoryCurveProjectionInput(
                    viewModel.SemanticPatientBatch, referencePatient.PatientKey,
                    referenceOptions.Select(static option => option.Key).ToArray(),
                    new LaboratoryCurveFilter(LaboratoryCurveFilterMode.All), true,
                    new DateOnly(2026, 8, 16)));
                var referenceText = LaboratoryCurveTextFormatter.Format(referenceProjection, true);

                Assert.Contains("#PCR (mg/L): 19/01/26 - 236,6 | 18/07/26 - 64,9 (-171,7)",
                    referenceText, StringComparison.Ordinal);
                Assert.Contains("#TGO (U/L): 13/01/15 - 20 | 26/01/23 - 29,1 (+9,1)",
                    referenceText, StringComparison.Ordinal);
                Assert.Contains("19/01/16 - 0,9 (-0,7) | 07/07/17 - 1,01 (+0,11) | 23/08/17 - 0,97 (-0,04)",
                    referenceText, StringComparison.Ordinal);
                Assert.Contains("19/01/16 - 66,6 (+33,0) | 07/07/17 - 57,6 (-9,0) | 23/08/17 - 60,5 (+2,9)",
                    referenceText, StringComparison.Ordinal);
                Assert.Contains("18/06/25 - Leuco 9.470 (N 62,1% | L 15,1% | B 4,0% | Mielócitos 3,0% | Metamielócitos 3,0%)",
                    referenceText, StringComparison.Ordinal);
                Assert.Contains("#Bilirrubinas (mg/dL): 19/12/23 - (BT 0,8 | BD 0,2 | BI 0,6)",
                    referenceText, StringComparison.Ordinal);

                var snapshotPath = Environment.GetEnvironmentVariable("ARS_EXTRACTUM_CURVE_UI_SNAPSHOT_PATH");
                if (!string.IsNullOrWhiteSpace(snapshotPath))
                {
                    RenderReferenceWindow(viewModel.SemanticPatientBatch, referencePatient.PatientKey,
                        referencePatient.Identity.PatientName, snapshotPath);
                }
            }
        }

        var expected = LaboratoryCurveDefinitions.Options.Select(static option => option.Key)
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(expected.SetEquals(union),
            $"Opções ausentes: {string.Join(", ", expected.Except(union).Order(StringComparer.Ordinal))}");
        Assert.True(youngFormProjected, "Os PDFs configurados não projetam formas jovens do leucograma.");
    }

    private static void RenderReferenceWindow(
        SemanticPatientBatch batch,
        string patientKey,
        string patientName,
        string outputPath)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new LaboratoryCurvesWindow(batch, patientKey, patientName);
                var curves = Assert.IsType<LaboratoryCurvesViewModel>(window.DataContext);
                foreach (var option in curves.Options)
                {
                    option.IsSelected = true;
                }

                curves.IncludeDelta = true;
                Assert.True(curves.Generate());
                window.Show();
                window.UpdateLayout();
                var bitmap = new RenderTargetBitmap(
                    (int)Math.Ceiling(window.ActualWidth),
                    (int)Math.Ceiling(window.ActualHeight),
                    96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(outputPath);
                encoder.Save(stream);
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "A captura visual da janela de curvas excedeu o prazo.");
        Assert.Null(failure);
    }
}
