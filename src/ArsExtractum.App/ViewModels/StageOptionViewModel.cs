using ArsExtractum.Core.Pipeline;

namespace ArsExtractum.App.ViewModels;

public sealed class StageOptionViewModel(StageDescriptor descriptor) : ObservableObject
{
    private bool _isEnabled = true;

    public StageDescriptor Descriptor { get; } = descriptor;

    public string Id => Descriptor.Id;

    public string Name => Descriptor.Name;

    public string Description => Descriptor.Description;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }
}
