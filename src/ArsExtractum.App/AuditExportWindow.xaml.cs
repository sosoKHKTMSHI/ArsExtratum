using System.Windows;
using ArsExtractum.App.Services;

namespace ArsExtractum.App;

public partial class AuditExportWindow : Window
{
    public AuditExportWindow() => InitializeComponent();

    public AuditExportOptions Options => new(
        CaptureOption.IsChecked == true,
        ReconstructionOption.IsChecked == true,
        SanitizationOption.IsChecked == true,
        AssemblyOption.IsChecked == true,
        SemanticOption.IsChecked == true,
        ProjectionOption.IsChecked == true);

    private void SelectAll_Click(object sender, RoutedEventArgs e) => SetAll(true);

    private void ClearAll_Click(object sender, RoutedEventArgs e) => SetAll(false);

    private void SelectCompact_Click(object sender, RoutedEventArgs e)
    {
        SetAll(false);
        AssemblyOption.IsChecked = true;
        SemanticOption.IsChecked = true;
        ProjectionOption.IsChecked = true;
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!Options.AnySelected)
        {
            MessageBox.Show(this, "Selecione ao menos um output.", "Ars Extractum",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void SetAll(bool value)
    {
        CaptureOption.IsChecked = value;
        ReconstructionOption.IsChecked = value;
        SanitizationOption.IsChecked = value;
        AssemblyOption.IsChecked = value;
        SemanticOption.IsChecked = value;
        ProjectionOption.IsChecked = value;
    }
}
