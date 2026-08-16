using System.Windows;

namespace ArsExtractum.UserApp;

public partial class CultureReviewWindow : Window
{
    public CultureReviewWindow(string warningText, string reviewText)
    {
        InitializeComponent();
        DataContext = new { WarningText = warningText, ReviewText = reviewText };
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ReviewBox.Text))
        {
            Clipboard.SetText(ReviewBox.Text);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
