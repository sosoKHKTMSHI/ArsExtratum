using System.Text.Json.Serialization;
using ArsExtractum.Core.Assembly;
using ArsExtractum.Core.LaboratorySemantic;

namespace ArsExtractum.Core.OutputProjection;

public sealed record OutputProjectionInput(
    SemanticPatientBatch SemanticPatientBatch,
    OutputProjectionOptions? Options = null);

public sealed record OutputProjectionOptions(
    bool ShowUnits = false,
    bool ShowCultures = false);

public sealed record ClinicalOutputBatch(
    string SchemaVersion,
    string ProjectionRulesVersion,
    string SourceSemanticSchemaVersion,
    OutputProjectionOptions Options,
    IReadOnlyList<ClinicalOutputPatient> Patients,
    OutputProjectionCoverage Coverage,
    IReadOnlyList<OutputProjectionNotice> Notices);

public sealed record ClinicalOutputPatient(
    string PatientKey,
    PatientIdentity DisplayIdentity,
    IReadOnlyList<ClinicalOutputEpisode> Episodes);

public sealed record ClinicalOutputEpisode(
    string EpisodeKey,
    string RequestNumber,
    string RequestDate,
    string RequestTime,
    IReadOnlyList<PatientSourceDocument> SourceDocuments,
    IReadOnlyList<ClinicalProjectedOccurrence> ProjectedOccurrences,
    string EditableClinicalText);

public sealed record ClinicalProjectedOccurrence(
    string ProjectionId,
    string SourceOccurrenceId,
    string ConceptId,
    ProjectionDisposition Disposition,
    IReadOnlyList<string> Lines,
    IReadOnlyList<string> SourceObservationIds,
    IReadOnlyList<string> SourceDerivedObservationIds,
    IReadOnlyList<FieldProjectionRecord> FieldProjectionRecords);

public sealed record FieldProjectionRecord(
    string FieldKey,
    string FieldKind,
    FieldProjectionDisposition Disposition,
    string ReasonCode,
    int? OutputLineIndex = null,
    string? OutputFragment = null);

public sealed record OutputProjectionCoverage(
    int SourcePatientCount,
    int ProjectedPatientCount,
    int SourceEpisodeCount,
    int ProjectedEpisodeCount,
    int SourceOccurrenceCount,
    int ProjectedOccurrenceCount,
    int SuppressedByExplicitPolicyCount,
    int SafeFallbackCount,
    int ProjectionFailureCount,
    int UnmappedOccurrenceCount,
    int SourceFieldCount,
    int AccountedFieldCount,
    int UnmappedFieldCount,
    int FieldProjectionFailureCount)
{
    public bool IsComplete =>
        SourcePatientCount == ProjectedPatientCount &&
        SourceEpisodeCount == ProjectedEpisodeCount &&
        SourceOccurrenceCount == ProjectedOccurrenceCount + SuppressedByExplicitPolicyCount + SafeFallbackCount &&
        ProjectionFailureCount == 0 &&
        UnmappedOccurrenceCount == 0 &&
        SourceFieldCount == AccountedFieldCount &&
        UnmappedFieldCount == 0 &&
        FieldProjectionFailureCount == 0;
}

public sealed record OutputProjectionNotice(
    string Code,
    string Message,
    string? PatientKey = null,
    string? EpisodeKey = null,
    string? OccurrenceId = null);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProjectionDisposition
{
    Projected,
    SuppressedByExplicitPolicy,
    SafeFallback,
    ProjectionFailure,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FieldProjectionDisposition
{
    Projected,
    SuppressedByExplicitPolicy,
    AuditOnly,
    SafeFallback,
    ProjectionFailure,
}
