using ArsExtractum.Core.Assembly;

namespace ArsExtractum.UserApp.ViewModels;

public sealed class UserPatientItem
{
    public UserPatientItem(AssembledPatient patient) => Patient = patient;

    public AssembledPatient Patient { get; }
    public string DisplayName => Patient.Identity.PatientName;
    public string Summary => $"{Patient.Episodes.Count} episódio(s) · {Patient.SourceDocuments.Count} PDF(s)";
}
