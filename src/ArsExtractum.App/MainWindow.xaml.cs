using System.IO;
using System.Text;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using ArsExtractum.App.Services;
using ArsExtractum.App.ViewModels;
using ArsExtractum.Core.Pipeline;
using ArsExtractum.Runtime;
using Microsoft.Win32;

namespace ArsExtractum.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        var pipeline = ProductionRuntime.CreateDocumentPipeline();
        _viewModel = new MainWindowViewModel(pipeline);
        DataContext = _viewModel;
    }

    private void AddPdf_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecionar PDFs",
            Filter = "Arquivos PDF (*.pdf)|*.pdf",
            Multiselect = true,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.AddFiles(dialog.FileNames);
        }
    }

    private void RemovePdf_Click(object sender, RoutedEventArgs e) =>
        _viewModel.RemoveSelected();

    private void Clear_Click(object sender, RoutedEventArgs e) =>
        _viewModel.Clear();

    private async void Process_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.ProcessAsync();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_viewModel.OutputText))
        {
            Clipboard.SetText(_viewModel.OutputText);
        }
    }

    private void ReviewCultures_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.HasSelectedPatientCultures) return;
        new CultureReviewWindow(_viewModel.CultureWarningText, _viewModel.CultureReviewText)
        {
            Owner = this,
        }.ShowDialog();
    }

    private void LaboratoryCurves_Click(object sender, RoutedEventArgs e)
    {
        var patient = _viewModel.SelectedSemanticPatientForCurves;
        if (!_viewModel.CanOpenLaboratoryCurves || patient is null)
        {
            return;
        }

        new LaboratoryCurvesWindow(
            _viewModel.SemanticPatientBatch!,
            patient.PatientKey,
            patient.Identity.PatientName)
        {
            Owner = this,
        }.ShowDialog();
    }

    private async void SaveText_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_viewModel.OutputText))
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Salvar saída visível",
            Filter = "Arquivo de texto UTF-8 (*.txt)|*.txt",
            FileName = "ars-extractum-output.txt",
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) == true)
        {
            await File.WriteAllTextAsync(dialog.FileName, _viewModel.OutputText, new UTF8Encoding(false));
        }
    }

    private async void SaveReport_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CompletedRuns.Count == 0)
        {
            MessageBox.Show(
                this,
                "Processe ao menos um PDF antes de gerar o relatório.",
                "Ars Extractum",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var selection = new AuditExportWindow { Owner = this };
        if (selection.ShowDialog() != true)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Salvar pacote de auditoria",
            Filter = "Relatório ZIP (*.zip)|*.zip",
            FileName = "ars-extractum-audit.zip",
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await DetailedReportExporter.ExportAsync(
                dialog.FileName,
                _viewModel.CompletedRuns,
                _viewModel.PatientBatch,
                _viewModel.SemanticPatientBatch,
                _viewModel.ClinicalOutputBatch,
                selection.Options);
            MessageBox.Show(
                this,
                "Pacote de auditoria salvo com sucesso.",
                "Ars Extractum",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (IOException exception)
        {
            MessageBox.Show(
                this,
                $"Não foi possível salvar o relatório: {exception.Message}",
                "Ars Extractum",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void SaveReconstructionZip_Click(object sender, RoutedEventArgs e)
    {
        if (!HasCompletedRuns())
        {
            return;
        }

        var generatedAt = DateTimeOffset.Now;
        var timestamp = generatedAt.ToString("ddMMHHmm", CultureInfo.InvariantCulture);
        var dialog = new SaveFileDialog
        {
            Title = "Salvar outputs de reconstrução",
            Filter = "Arquivo ZIP (*.zip)|*.zip",
            FileName = $"ars-extractum-reconstructions-{timestamp}.zip",
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await ExportReconstructionAsync(
            () => ReconstructionOutputExporter.ExportZipAsync(
                dialog.FileName,
                _viewModel.CompletedRuns,
                generatedAt),
            "Outputs JSON salvos com sucesso.");
    }

    private async void SaveReconstructionBundle_Click(object sender, RoutedEventArgs e)
    {
        if (!HasCompletedRuns())
        {
            return;
        }

        var generatedAt = DateTimeOffset.Now;
        var timestamp = generatedAt.ToString("ddMMHHmm", CultureInfo.InvariantCulture);
        var dialog = new SaveFileDialog
        {
            Title = "Salvar reconstruções em arquivo único",
            Filter = "Arquivo JSON (*.json)|*.json",
            FileName = $"ars-extractum-reconstructions-{timestamp}.json",
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await ExportReconstructionAsync(
            () => ReconstructionOutputExporter.ExportSingleFileAsync(
                dialog.FileName,
                _viewModel.CompletedRuns,
                generatedAt),
            "Arquivo consolidado salvo com sucesso.");
    }

    private bool HasCompletedRuns()
    {
        if (_viewModel.CompletedRuns.Count > 0)
        {
            return true;
        }

        MessageBox.Show(
            this,
            "Processe ao menos um PDF antes de exportar os resultados.",
            "Ars Extractum",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return false;
    }

    private async Task ExportReconstructionAsync(Func<Task> export, string successMessage)
    {
        try
        {
            await export();
            MessageBox.Show(
                this,
                successMessage,
                "Ars Extractum",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            MessageBox.Show(
                this,
                $"Não foi possível salvar o arquivo: {exception.Message}",
                "Ars Extractum",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            _viewModel.AddFiles(paths);
        }
    }
}
