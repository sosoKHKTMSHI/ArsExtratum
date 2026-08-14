using System.IO;

namespace ArsExtractum.App.ViewModels;

public sealed class InputPdfItem(string filePath) : ObservableObject
{
    private string _status = "Aguardando processamento";

    public string FilePath { get; } = filePath;

    public string DisplayName { get; } = Path.GetFileName(filePath);

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }
}
