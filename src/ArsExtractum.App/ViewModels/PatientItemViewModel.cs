using ArsExtractum.Core.Assembly;

namespace ArsExtractum.App.ViewModels;

public sealed class PatientItemViewModel(AssembledPatient patient)
{
    public AssembledPatient Patient { get; } = patient;

    public string DisplayName => Patient.Identity.PatientName;

    public string Summary =>
        $"{Patient.Episodes.Count} episódio(s) | {Patient.SourceDocuments.Count} PDF(s)";
}
