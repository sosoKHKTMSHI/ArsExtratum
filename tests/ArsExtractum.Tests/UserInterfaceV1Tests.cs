using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArsExtractum.Core.Assembly;
using ArsExtractum.Core.DerivedMeasurements;
using ArsExtractum.Core.LaboratorySemantic;
using ArsExtractum.Core.OutputProjection;
using ArsExtractum.Core.Pipeline;
using ArsExtractum.Runtime;
using ArsExtractum.UserApp.ViewModels;
using Xunit;
using UserAboutWindow = ArsExtractum.UserApp.AboutWindow;
using UserCultureReviewWindow = ArsExtractum.UserApp.CultureReviewWindow;
using UserCurvesWindow = ArsExtractum.UserApp.LaboratoryCurvesWindow;
using UserMainWindow = ArsExtractum.UserApp.MainWindow;
using InspectorMainWindowViewModel = ArsExtractum.App.ViewModels.MainWindowViewModel;

namespace ArsExtractum.Tests;

public sealed class UserInterfaceV1Tests
{
    [Fact]
    public void ProductionRuntimeDefinesTheSingleOfficialDocumentPipeline()
    {
        var stages = ProductionRuntime.CreateDocumentPipeline().Stages;

        Assert.Equal(
            [StageIds.PdfPigCapture, StageIds.RawReconstruction, StageIds.Sanitization],
            stages.Select(static stage => stage.Id));
    }

    [Fact]
    public void FileManagementRejectsInvalidAndDuplicateInputsAndClearsSession()
    {
        var directory = Directory.CreateTempSubdirectory("ars-extractum-ui-");
        try
        {
            var pdf = Path.Combine(directory.FullName, "laboratorio.pdf");
            var txt = Path.Combine(directory.FullName, "notas.txt");
            File.WriteAllBytes(pdf, [1, 2, 3]);
            File.WriteAllText(txt, "não é PDF");
            using var viewModel = new MainWindowViewModel(new NeverCalledProcessor());

            viewModel.AddFiles([pdf, pdf, txt]);

            Assert.Single(viewModel.Documents);
            Assert.True(viewModel.CanProcess);
            Assert.True(viewModel.CanRemoveDocument);
            Assert.Contains("ignorado", viewModel.NoticeText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("já constavam", viewModel.NoticeText, StringComparison.OrdinalIgnoreCase);

            viewModel.RemoveSelected();
            Assert.Empty(viewModel.Documents);
            Assert.False(viewModel.CanProcess);

            viewModel.AddFiles([pdf]);
            viewModel.ClearSession();
            Assert.Empty(viewModel.Documents);
            Assert.Empty(viewModel.Patients);
            Assert.Empty(viewModel.OutputText);
            Assert.Equal("Adicione PDFs para iniciar.", viewModel.StatusText);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task CancellationNeverPresentsAPartialResult()
    {
        var directory = Directory.CreateTempSubdirectory("ars-extractum-ui-cancel-");
        try
        {
            var pdf = Path.Combine(directory.FullName, "laboratorio.pdf");
            File.WriteAllBytes(pdf, [1, 2, 3]);
            using var viewModel = new MainWindowViewModel(new WaitForCancellationProcessor());
            viewModel.AddFiles([pdf]);

            var processing = viewModel.ProcessAsync();
            Assert.True(SpinWait.SpinUntil(() => viewModel.IsBusy, TimeSpan.FromSeconds(2)));
            viewModel.CancelProcessing();
            await processing;

            Assert.False(viewModel.IsBusy);
            Assert.Empty(viewModel.Patients);
            Assert.Empty(viewModel.OutputText);
            Assert.Contains("cancelado", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
            Assert.True(viewModel.CanProcess);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task ConfiguredReferencePdfCompletesTheRealUserFlow()
    {
        var pdf = Environment.GetEnvironmentVariable("ARS_EXTRACTUM_USER_UI_REFERENCE_PDF");
        if (string.IsNullOrWhiteSpace(pdf))
        {
            return;
        }

        using var viewModel = new MainWindowViewModel();
        viewModel.AddFiles([pdf]);
        await viewModel.ProcessAsync();

        Assert.Single(viewModel.Documents);
        Assert.Equal("Concluído", viewModel.Documents[0].Status);
        Assert.NotEmpty(viewModel.Patients);
        Assert.NotNull(viewModel.SelectedPatient);
        Assert.NotEmpty(viewModel.OutputText);
        Assert.True(viewModel.CanCopyOutput);
        Assert.True(viewModel.CanOpenCurves);
        Assert.Equal(100d, viewModel.ProgressPercent);
        Assert.Contains("Concluído", viewModel.StatusText, StringComparison.Ordinal);
        if (viewModel.HasSelectedPatientCultures)
        {
            Assert.True(viewModel.CanReviewCultures);
            Assert.Contains("cultur", viewModel.NoticeText, StringComparison.OrdinalIgnoreCase);
            Assert.NotEmpty(viewModel.CultureReviewText);
        }

        viewModel.ShowUnits = true;
        Assert.NotEmpty(viewModel.OutputText);
        viewModel.ShowUnits = false;

        var inspector = new InspectorMainWindowViewModel(ProductionRuntime.CreateDocumentPipeline());
        inspector.AddFiles([pdf]);
        await inspector.ProcessAsync();
        Assert.Equal(inspector.OutputText, viewModel.OutputText);
    }

    [Fact]
    public void MainWindowAndDialogsExposeTheContractedControlsAndModalConfiguration()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current is null)
                {
                    var app = new ArsExtractum.UserApp.App();
                    app.InitializeComponent();
                }

                var referencePdf = Environment.GetEnvironmentVariable("ARS_EXTRACTUM_USER_UI_REFERENCE_PDF");
                using var viewModel = string.IsNullOrWhiteSpace(referencePdf)
                    ? new MainWindowViewModel(new NeverCalledProcessor())
                    : new MainWindowViewModel();
                if (!string.IsNullOrWhiteSpace(referencePdf))
                {
                    viewModel.AddFiles([referencePdf]);
                    viewModel.ProcessAsync().GetAwaiter().GetResult();
                }
                var window = new UserMainWindow(viewModel);
                window.Show();
                window.UpdateLayout();

                Assert.NotNull(window.FindName("DropPanel"));
                Assert.NotNull(window.FindName("AddPdfButton"));
                Assert.NotNull(window.FindName("PdfList"));
                Assert.NotNull(window.FindName("PatientList"));
                Assert.NotNull(window.FindName("OutputBox"));
                Assert.NotNull(window.FindName("NoticePanel"));
                Assert.NotNull(window.FindName("ShowUnitsCheckBox"));
                Assert.NotNull(window.FindName("CultureReviewButton"));
                Assert.NotNull(window.FindName("CurvesButton"));
                Assert.NotNull(window.FindName("CopyOutputButton"));
                Assert.Equal(!string.IsNullOrWhiteSpace(referencePdf),
                    ((Button)window.FindName("ProcessButton")).IsEnabled);
                Assert.False(((Button)window.FindName("CancelButton")).IsEnabled);

                var snapshotPath = Environment.GetEnvironmentVariable("ARS_EXTRACTUM_USER_UI_SNAPSHOT_PATH");
                if (!string.IsNullOrWhiteSpace(snapshotPath))
                {
                    SaveWindowSnapshot(window, snapshotPath);
                }

                var minimumSnapshotPath = Environment.GetEnvironmentVariable(
                    "ARS_EXTRACTUM_USER_UI_MINIMUM_SNAPSHOT_PATH");
                if (!string.IsNullOrWhiteSpace(minimumSnapshotPath))
                {
                    window.Width = window.MinWidth;
                    window.Height = window.MinHeight;
                    window.UpdateLayout();
                    SaveWindowSnapshot(window, minimumSnapshotPath);
                }

                var about = new UserAboutWindow();
                var culture = new UserCultureReviewWindow("aviso", "conteúdo");
                var curves = new UserCurvesWindow(EmptySemanticBatch(), "patient-test", "PACIENTE");
                Assert.False(about.ShowInTaskbar);
                Assert.False(culture.ShowInTaskbar);
                Assert.False(curves.ShowInTaskbar);
                Assert.Equal(WindowStartupLocation.CenterOwner, about.WindowStartupLocation);
                Assert.Equal(WindowStartupLocation.CenterOwner, culture.WindowStartupLocation);
                Assert.Equal(WindowStartupLocation.CenterOwner, curves.WindowStartupLocation);
                about.Close();
                culture.Close();
                curves.Close();
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "A interface final não concluiu o smoke test WPF.");
        Assert.Null(failure);
    }

    private static void SaveWindowSnapshot(Window window, string path)
    {
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(window.ActualWidth)),
            Math.Max(1, (int)Math.Ceiling(window.ActualHeight)),
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static SemanticPatientBatch EmptySemanticBatch() =>
        new(
            LaboratorySemanticExtractor.CurrentSchemaVersion,
            LaboratorySemanticExtractor.CurrentRulesVersion,
            "1.0.0",
            "fixture",
            [new SemanticPatient(
                "patient-test",
                new PatientIdentity("PACIENTE", "01/01/1970", "Feminino"),
                [],
                [])],
            new LaboratorySemanticCoverage(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            [])
        {
            DerivedMeasurementRulesVersion = DerivedMeasurementComputer.CurrentRulesVersion,
            DerivedMeasurementCoverage = new DerivedMeasurementCoverage(
                0, 0, 0, 0, new Dictionary<string, int>(), 0, 0, 0),
        };

    private sealed class NeverCalledProcessor : IProductionSessionProcessor
    {
        public Task<ProductionSessionResult> ProcessAsync(
            IReadOnlyList<string> filePaths,
            OutputProjectionOptions options,
            IProgress<ProductionProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("O processador não deveria ser chamado neste teste.");
    }

    private sealed class WaitForCancellationProcessor : IProductionSessionProcessor
    {
        public async Task<ProductionSessionResult> ProcessAsync(
            IReadOnlyList<string> filePaths,
            OutputProjectionOptions options,
            IProgress<ProductionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Inalcançável.");
        }
    }
}
