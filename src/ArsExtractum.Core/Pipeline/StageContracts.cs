namespace ArsExtractum.Core.Pipeline;

public static class StageIds
{
    public const string PdfPigCapture = "capture.pdfpig";
    public const string RawReconstruction = "document.raw-reconstruction";
    public const string Sanitization = "document.sanitization";
    public const string PatientEpisodeAssembly = "batch.patient-episode-assembly";
    public const string LaboratorySemanticExtraction = "batch.laboratory-semantic-extraction-v1";
    public const string DerivedMeasurementComputation = "batch.derived-measurement-computation-v1";
    public const string OutputProjection = "batch.output-projection-v1";
}

public sealed record SourcePdf(string FileName, byte[] Content);

public sealed record StageDescriptor(
    string Id,
    string Name,
    string Description,
    string Version,
    IReadOnlyList<string> Dependencies);

public enum ProcessingNoticeSeverity
{
    Information,
    Warning,
    Error,
}

public sealed record ProcessingNotice(
    string Code,
    ProcessingNoticeSeverity Severity,
    string Message,
    int? PageNumber = null);

public sealed record StageOutput(
    object Payload,
    string DisplayText,
    IReadOnlyList<ProcessingNotice> Notices);

public sealed record StageExecutionResult(
    StageDescriptor Descriptor,
    object Payload,
    string DisplayText,
    IReadOnlyList<ProcessingNotice> Notices,
    TimeSpan Duration);

public sealed record DocumentExecution(
    string FileName,
    IReadOnlyDictionary<string, StageExecutionResult> Stages);

public interface IProcessingStage
{
    StageDescriptor Descriptor { get; }

    Task<StageOutput> ExecuteAsync(StageContext context, CancellationToken cancellationToken);
}

public sealed class StageContext(
    SourcePdf source,
    IReadOnlyDictionary<string, StageExecutionResult> completedStages)
{
    public SourcePdf Source { get; } = source;

    public T RequirePayload<T>(string stageId)
        where T : class
    {
        if (!completedStages.TryGetValue(stageId, out var result))
        {
            throw new InvalidOperationException($"A etapa obrigatória '{stageId}' não foi executada.");
        }

        return result.Payload as T
            ?? throw new InvalidOperationException($"A etapa '{stageId}' produziu um contrato inesperado.");
    }
}
