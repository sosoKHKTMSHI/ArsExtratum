using System.IO;

namespace ArsExtractum.UserApp.ViewModels;

public sealed class UserPdfItem(string filePath) : ObservableObject
{
    private string _status = "Pronto para processar";

    public string FilePath { get; } = filePath;
    public string DisplayName { get; } = Path.GetFileName(filePath);

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }
}
