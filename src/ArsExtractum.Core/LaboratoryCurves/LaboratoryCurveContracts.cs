using ArsExtractum.Core.LaboratorySemantic;

namespace ArsExtractum.Core.LaboratoryCurves;

public enum LaboratoryCurveFilterMode
{
    All,
    CustomRange,
    LastDays,
}

public sealed record LaboratoryCurveFilter(
    LaboratoryCurveFilterMode Mode,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    int? LastDays = null);

public sealed record LaboratoryCurveOption(
    string Key,
    string DisplayName,
    bool SupportsDelta,
    int Order);

public sealed record LaboratoryCurveProjectionInput(
    SemanticPatientBatch Batch,
    string PatientKey,
    IReadOnlyCollection<string> SelectedOptionKeys,
    LaboratoryCurveFilter Filter,
    bool IncludeDelta,
    DateOnly CurrentDate);

public sealed record LaboratoryCurveProjection(
    string PatientKey,
    IReadOnlyList<LaboratoryCurveSeries> Series,
    bool IncludeYear);

public sealed record LaboratoryCurveSeries(
    string Key,
    string Label,
    string? Unit,
    bool SupportsDelta,
    int Order,
    IReadOnlyList<LaboratoryCurvePoint> Points);

public sealed record LaboratoryCurvePoint(
    DateTime Timestamp,
    IReadOnlyList<LaboratoryCurveValue> Values,
    string EpisodeKey,
    string SourceFieldId);

public sealed record LaboratoryCurveValue(
    string Key,
    string Label,
    decimal NumericValue,
    string DisplayValue,
    string Unit,
    int? FixedDeltaDecimals = null);
