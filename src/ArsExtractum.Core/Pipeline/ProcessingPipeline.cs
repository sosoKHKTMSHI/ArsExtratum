using System.Diagnostics;

namespace ArsExtractum.Core.Pipeline;

public sealed class ProcessingPipeline
{
    private readonly IReadOnlyList<IProcessingStage> _orderedStages;
    private readonly Dictionary<string, IProcessingStage> _stagesById;

    public ProcessingPipeline(IEnumerable<IProcessingStage> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        _orderedStages = stages.ToArray();
        _stagesById = _orderedStages.ToDictionary(
            static stage => stage.Descriptor.Id,
            StringComparer.Ordinal);

        foreach (var stage in _orderedStages)
        {
            foreach (var dependency in stage.Descriptor.Dependencies)
            {
                if (!_stagesById.ContainsKey(dependency))
                {
                    throw new ArgumentException(
                        $"A etapa '{stage.Descriptor.Id}' depende da etapa não registrada '{dependency}'.",
                        nameof(stages));
                }
            }
        }
    }

    public IReadOnlyList<StageDescriptor> Stages =>
        _orderedStages.Select(static stage => stage.Descriptor).ToArray();

    public async Task<DocumentExecution> ExecuteAsync(
        SourcePdf source,
        IEnumerable<string> requestedStageIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(requestedStageIds);

        var stagesToRun = ResolveStages(requestedStageIds);
        var results = new Dictionary<string, StageExecutionResult>(StringComparer.Ordinal);
        foreach (var stage in stagesToRun)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stopwatch = Stopwatch.StartNew();
            var output = await stage.ExecuteAsync(
                new StageContext(source, results),
                cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            results.Add(
                stage.Descriptor.Id,
                new StageExecutionResult(
                    stage.Descriptor,
                    output.Payload,
                    output.DisplayText,
                    output.Notices,
                    stopwatch.Elapsed));
        }

        return new DocumentExecution(source.FileName, results);
    }

    private IProcessingStage[] ResolveStages(IEnumerable<string> requestedStageIds)
    {
        var resolved = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in requestedStageIds.Distinct(StringComparer.Ordinal))
        {
            Visit(id, resolved, visiting);
        }

        return _orderedStages.Where(stage => resolved.Contains(stage.Descriptor.Id)).ToArray();
    }

    private void Visit(string id, ISet<string> resolved, ISet<string> visiting)
    {
        if (resolved.Contains(id))
        {
            return;
        }

        if (!_stagesById.TryGetValue(id, out var stage))
        {
            throw new ArgumentException($"A etapa '{id}' não está registrada.", nameof(id));
        }

        if (!visiting.Add(id))
        {
            throw new InvalidOperationException($"Dependência circular detectada na etapa '{id}'.");
        }

        foreach (var dependency in stage.Descriptor.Dependencies)
        {
            Visit(dependency, resolved, visiting);
        }

        visiting.Remove(id);
        resolved.Add(id);
    }
}
