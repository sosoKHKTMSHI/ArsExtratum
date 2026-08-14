using System.Text.Json.Serialization;
using ArsExtractum.Core.Assembly;
using ArsExtractum.Core.DerivedMeasurements;
using ArsExtractum.Core.Documents;
using ArsExtractum.Core.Reconstruction;

namespace ArsExtractum.Core.LaboratorySemantic;

public sealed record LaboratorySemanticExtractionInput(
    PatientBatch PatientBatch,
    IReadOnlyDictionary<string, ReconstructedDocument>? ReconstructionByDocumentId = null);

public sealed record SemanticPatientBatch(
    string SchemaVersion,
    string ExtractionRulesVersion,
    string CatalogVersion,
    string ReferenceCorpusId,
    IReadOnlyList<SemanticPatient> Patients,
    LaboratorySemanticCoverage Coverage,
    IReadOnlyList<LaboratorySemanticNotice> Notices)
{
    public string? DerivedMeasurementRulesVersion { get; init; }

    public DerivedMeasurementCoverage? DerivedMeasurementCoverage { get; init; }

    public IReadOnlyList<DerivedMeasurementNotice> DerivedMeasurementNotices { get; init; } = [];
}

public sealed record SemanticPatient(
    string PatientKey,
    PatientIdentity Identity,
    IReadOnlyList<PatientSourceDocument> SourceDocuments,
    IReadOnlyList<SemanticEpisode> Episodes);

public sealed record SemanticEpisode(
    string EpisodeKey,
    AssembledEpisode DocumentaryEpisode,
    IReadOnlyList<LaboratoryOccurrence> LaboratoryOccurrences,
    IReadOnlyList<UnsupportedLaboratoryContent> UnsupportedContent,
    LaboratorySemanticEpisodeCoverage Coverage);

public sealed record LaboratoryOccurrence(
    string OccurrenceId,
    string EpisodeKey,
    string ConceptId,
    string DisplayName,
    string StructuralForm,
    LaboratoryRepresentationStatus Status,
    IReadOnlyList<LaboratoryObservation> Observations,
    IReadOnlyList<LaboratorySpecimen> Specimens,
    IReadOnlyList<LaboratoryAttributeValue> Attributes,
    IReadOnlyList<LaboratoryReference> References,
    IReadOnlyList<LaboratoryNarrative> Narratives,
    IReadOnlyList<LaboratoryRelationship> Relationships,
    LaboratoryMicrobiology? Microbiology,
    IReadOnlyList<OccurrenceSourceSegment> SourceSegments,
    IReadOnlyList<SemanticFieldEvidence> FieldEvidence,
    IReadOnlyList<string> AppliedRuleIds,
    IReadOnlyList<LaboratorySemanticAmbiguity> Ambiguities)
{
    public IReadOnlyList<DerivedObservation> DerivedObservations { get; init; } = [];
}

public sealed record LaboratoryObservation(
    string ObservationId,
    string Label,
    string RawValue,
    decimal? NumericValue,
    string? CodedValue,
    string? RawUnit,
    string? NormalizedUnit,
    SemanticFieldEvidence Evidence);

public sealed record LaboratorySpecimen(
    string RawSpecimen,
    SemanticFieldEvidence Evidence);

public sealed record LaboratoryAttributeValue(
    string Name,
    string RawValue,
    SemanticFieldEvidence Evidence);

public sealed record LaboratoryReference(
    string RawText,
    SemanticFieldEvidence Evidence);

public sealed record LaboratoryNarrative(
    string RawText,
    string ReasonCode,
    SemanticFieldEvidence Evidence);

public sealed record LaboratoryRelationship(
    string RelationshipId,
    string Relation,
    string SourceId,
    string TargetId,
    SemanticFieldEvidence Evidence);

public sealed record LaboratoryMicrobiology(
    IReadOnlyList<LaboratoryOrganism> Organisms,
    IReadOnlyList<LaboratorySusceptibilityGroup> SusceptibilityGroups);

public sealed record LaboratoryOrganism(
    string OrganismId,
    string RawName,
    SemanticFieldEvidence Evidence);

public sealed record LaboratorySusceptibilityGroup(
    string GroupId,
    string? OrganismId,
    string? RawOrganismName,
    IReadOnlyList<LaboratorySusceptibilityEntry> Entries,
    SemanticFieldEvidence Evidence);

public sealed record LaboratorySusceptibilityEntry(
    string RawAntimicrobial,
    string RawResult,
    string? Interpretation,
    SemanticFieldEvidence Evidence);

public sealed record OccurrenceSourceSegment(
    string BlockId,
    IReadOnlyList<string> CanonicalLineIds,
    IReadOnlyList<SemanticSourceAppearance> SourceAppearances);

public sealed record SemanticFieldEvidence(
    string FieldPath,
    string BlockId,
    string CanonicalLineId,
    string OriginalText,
    string SanitizedText,
    IReadOnlyList<string> SourceWordIds,
    IReadOnlyList<string> AppliedRuleIds,
    IReadOnlyList<SuppressedTextSegment> SuppressedSegments,
    IReadOnlyList<SemanticSourceAppearance> SourceAppearances);

public sealed record SemanticSourceAppearance(
    string DocumentId,
    string FileName,
    int InputIndex,
    int PageNumber,
    string LineId,
    IReadOnlyList<string> SourceWordIds);

public sealed record UnsupportedLaboratoryContent(
    string ReasonCode,
    string BlockId,
    IReadOnlyList<string> CanonicalLineIds,
    IReadOnlyList<SemanticFieldEvidence> Evidence);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LaboratoryRepresentationStatus
{
    FullyStructured,
    StructuredWithResidual,
    Unsupported,
    RepresentationFailure,
}

public sealed record LaboratorySemanticAmbiguity(
    string Code,
    string Message,
    IReadOnlyList<SemanticFieldEvidence> Evidence);

public sealed record LaboratorySemanticEpisodeCoverage(
    int CanonicalBlockCount,
    int CanonicalActiveLineCount,
    int OccurrenceCount,
    int OwnedActiveLineCount,
    int UnsupportedActiveLineCount,
    int MultiplyOwnedActiveLineCount,
    int RepresentationFailureCount,
    int KnownAnchorCount,
    int RecognizedAnchorCount)
{
    public bool IsLossless =>
        CanonicalActiveLineCount == OwnedActiveLineCount + UnsupportedActiveLineCount &&
        MultiplyOwnedActiveLineCount == 0 &&
        RepresentationFailureCount == 0;
}

public sealed record LaboratorySemanticCoverage(
    int PatientCount,
    int EpisodeCount,
    int CanonicalBlockCount,
    int CanonicalActiveLineCount,
    int OccurrenceCount,
    int OwnedActiveLineCount,
    int UnsupportedActiveLineCount,
    int MultiplyOwnedActiveLineCount,
    int RepresentationFailureCount,
    int KnownAnchorCount,
    int RecognizedAnchorCount)
{
    public bool IsLossless =>
        CanonicalActiveLineCount == OwnedActiveLineCount + UnsupportedActiveLineCount &&
        MultiplyOwnedActiveLineCount == 0 &&
        RepresentationFailureCount == 0;
}

public sealed record LaboratorySemanticNotice(
    string Code,
    string Message,
    string? EpisodeKey = null,
    string? OccurrenceId = null);
