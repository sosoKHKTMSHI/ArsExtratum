using System.Windows;
using ArsExtractum.App.ViewModels;
using ArsExtractum.Core.LaboratorySemantic;

namespace ArsExtractum.App;

public partial class LaboratoryCurvesWindow : Window
{
    private readonly LaboratoryCurvesViewModel _viewModel;

    public LaboratoryCurvesWindow(
        SemanticPatientBatch batch,
        string patientKey,
        string patientDisplayName)
    {
        InitializeComponent();
        _viewModel = new LaboratoryCurvesViewModel(batch, patientKey, patientDisplayName);
        DataContext = _viewModel;
    }

    private void Generate_Click(object sender, RoutedEventArgs e) => _viewModel.Generate();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(CurvesOutputBox.Text))
        {
            Clipboard.SetText(CurvesOutputBox.Text);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
