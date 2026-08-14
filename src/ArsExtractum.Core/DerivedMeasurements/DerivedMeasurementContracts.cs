using System.Text.Json.Serialization;
using ArsExtractum.Core.LaboratorySemantic;

namespace ArsExtractum.Core.DerivedMeasurements;

public sealed record DerivedMeasurementComputationInput(SemanticPatientBatch SemanticPatientBatch);

public sealed record DerivedObservation(
    string DerivedObservationId,
    string PatientKey,
    string EpisodeKey,
    string SourceOccurrenceId,
    string? SourceObservationId,
    string ConceptId,
    DerivedObservationKind Kind,
    string MethodId,
    DerivedObservationStatus Status,
    double? NumericValue,
    string? Unit,
    DerivedMeasurementReasonCode? ReasonCode,
    DerivedMeasurementInputs Inputs,
    DerivedMeasurementProvenance Provenance,
    IReadOnlyList<string> AppliedRuleIds);

public sealed record DerivedMeasurementInputs(
    string RawBirthDate,
    string RawRequestDate,
    int? AgeAtRequestYears,
    string? RawSex,
    string? NormalizedSex,
    string? RawCreatinineValue,
    decimal? NumericCreatinineValue,
    string? RawCreatinineUnit,
    string? NormalizedCreatinineUnit,
    string? RawSpecimen);

public sealed record DerivedMeasurementProvenance(
    string PatientKey,
    string EpisodeKey,
    string SourceOccurrenceId,
    IReadOnlyList<string> CandidateObservationIds,
    IReadOnlyList<SemanticFieldEvidence> CandidateObservationEvidence,
    IReadOnlyList<SemanticFieldEvidence> SpecimenEvidence,
    IReadOnlyList<DerivedHeaderEvidence> HeaderEvidence);

public sealed record DerivedHeaderEvidence(
    string DocumentId,
    string FileName,
    int InputIndex,
    int PageNumber,
    IReadOnlyList<string> SourceLineIds);

public sealed record DerivedMeasurementCoverage(
    int SourceCreatinineOccurrenceCount,
    int DerivedRecordCount,
    int ComputedCount,
    int NotComputedCount,
    IReadOnlyDictionary<string, int> ReasonCodeCounts,
    int OrphanDerivedRecordCount,
    int MultiplyMappedSourceOccurrenceCount,
    int LabReportedEgfrInputUseCount)
{
    public bool IsComplete =>
        DerivedRecordCount == SourceCreatinineOccurrenceCount &&
        ComputedCount + NotComputedCount == DerivedRecordCount &&
        OrphanDerivedRecordCount == 0 &&
        MultiplyMappedSourceOccurrenceCount == 0 &&
        LabReportedEgfrInputUseCount == 0;
}

public sealed record DerivedMeasurementNotice(
    string Code,
    string Message,
    string? PatientKey = null,
    string? EpisodeKey = null,
    string? SourceOccurrenceId = null);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DerivedObservationKind
{
    Derived,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DerivedObservationStatus
{
    Computed,
    NotComputed,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DerivedMeasurementReasonCode
{
    UnsafeAssociation,
    AgeAtRequestUnavailable,
    AgeBelow18,
    SexUnavailable,
    SexUnsupported,
    SerumSpecimenNotConfirmed,
    CreatinineObservationMissing,
    CreatinineObservationAmbiguous,
    CreatinineValueNotNumeric,
    CreatinineValueNotPositive,
    CreatinineUnitMissing,
    CreatinineUnitUnsupported,
    ComputationNonFinite,
}
