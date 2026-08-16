using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using ArsExtractum.Core.OutputProjection;
using ArsExtractum.UserApp.ViewModels;
using Microsoft.Win32;

namespace ArsExtractum.UserApp;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow() : this(new MainWindowViewModel())
    {
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void AddPdf_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Adicionar PDFs laboratoriais",
            Filter = "Arquivos PDF (*.pdf)|*.pdf",
            Multiselect = true,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.AddFiles(dialog.FileNames);
        }
    }

    private void RemovePdf_Click(object sender, RoutedEventArgs e) => _viewModel.RemoveSelected();

    private void ClearSession_Click(object sender, RoutedEventArgs e) => _viewModel.ClearSession();

    private async void Process_Click(object sender, RoutedEventArgs e) => await _viewModel.ProcessAsync();

    private void Cancel_Click(object sender, RoutedEventArgs e) => _viewModel.CancelProcessing();

    private void CopyOutput_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(OutputBox.Text))
        {
            Clipboard.SetText(OutputBox.Text);
        }
    }

    private void ReviewCultures_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanReviewCultures)
        {
            return;
        }

        new CultureReviewWindow(CultureReviewTextFormatter.WarningText, _viewModel.CultureReviewText)
        {
            Owner = this,
        }.ShowDialog();
    }

    private void LaboratoryCurves_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanOpenCurves || _viewModel.SemanticPatientBatch is null ||
            _viewModel.SelectedPatientKey is null || _viewModel.SelectedPatientName is null)
        {
            return;
        }

        new LaboratoryCurvesWindow(
            _viewModel.SemanticPatientBatch,
            _viewModel.SelectedPatientKey,
            _viewModel.SelectedPatientName)
        {
            Owner = this,
        }.ShowDialog();
    }

    private void About_Click(object sender, RoutedEventArgs e) =>
        new AboutWindow { Owner = this }.ShowDialog();

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = _viewModel.IsIdle && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (_viewModel.IsIdle && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            _viewModel.AddFiles(paths);
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e) => _viewModel.Dispose();
}
