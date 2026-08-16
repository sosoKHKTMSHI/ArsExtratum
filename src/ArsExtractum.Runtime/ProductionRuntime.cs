using ArsExtractum.Core.Assembly;
using ArsExtractum.Core.DerivedMeasurements;
using ArsExtractum.Core.Documents;
using ArsExtractum.Core.LaboratorySemantic;
using ArsExtractum.Core.OutputProjection;
using ArsExtractum.Core.Pipeline;
using ArsExtractum.Core.Reconstruction;
using ArsExtractum.Core.Sanitization;
using ArsExtractum.PdfPig;

namespace ArsExtractum.Runtime;

public sealed record ProductionProgress(
    int CompletedDocuments,
    int TotalDocuments,
    string CurrentFilePath,
    string Message,
    bool IsCurrentDocumentComplete)
{
    public double Percent => TotalDocuments == 0 ? 0d : CompletedDocuments * 100d / TotalDocuments;
}

public sealed record ProductionDocumentResult(
    string FilePath,
    string FileName,
    DocumentExecution? Execution,
    string? ErrorMessage)
{
    public bool Succeeded => Execution is not null;
}

public sealed record ProductionSessionResult(
    IReadOnlyList<ProductionDocumentResult> Documents,
    PatientBatch PatientBatch,
    SemanticPatientBatch SemanticPatientBatch,
    ClinicalOutputBatch ClinicalOutputBatch);

public interface IProductionSessionProcessor
{
    Task<ProductionSessionResult> ProcessAsync(
        IReadOnlyList<string> filePaths,
        OutputProjectionOptions options,
        IProgress<ProductionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class ProductionSessionProcessor : IProductionSessionProcessor
{
    private readonly ProcessingPipeline _pipeline;

    public ProductionSessionProcessor() : this(ProductionRuntime.CreateDocumentPipeline())
    {
    }

    public ProductionSessionProcessor(ProcessingPipeline pipeline) =>
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));

    public async Task<ProductionSessionResult> ProcessAsync(
        IReadOnlyList<string> filePaths,
        OutputProjectionOptions options,
        IProgress<ProductionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        ArgumentNullException.ThrowIfNull(options);
        if (filePaths.Count == 0)
        {
            throw new ArgumentException("Adicione ao menos um PDF antes de processar.", nameof(filePaths));
        }

        var documents = new List<ProductionDocumentResult>(filePaths.Count);
        for (var index = 0; index < filePaths.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filePath = filePaths[index];
            var fileName = Path.GetFileName(filePath);
            progress?.Report(new ProductionProgress(
                index, filePaths.Count, filePath, $"Processando {fileName}...", false));
            try
            {
                var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
                var execution = await _pipeline.ExecuteAsync(
                    new SourcePdf(fileName, bytes),
                    [StageIds.Sanitization],
                    cancellationToken).ConfigureAwait(false);
                documents.Add(new ProductionDocumentResult(filePath, fileName, execution, null));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                documents.Add(new ProductionDocumentResult(
                    filePath,
                    fileName,
                    null,
                    exception.GetBaseException().Message));
            }

            progress?.Report(new ProductionProgress(
                index + 1,
                filePaths.Count,
                filePath,
                $"{index + 1} de {filePaths.Count} PDF(s) processado(s).",
                true));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (documents.All(static document => !document.Succeeded))
        {
            throw new InvalidOperationException("Nenhum PDF pôde ser processado.");
        }

        var assemblyInputs = documents
            .Select(static (document, inputIndex) => new { document, inputIndex })
            .Where(static item => item.document.Execution is not null)
            .Select(static item => new PatientAssemblyInput(
                (SanitizedDocument)item.document.Execution!.Stages[StageIds.Sanitization].Payload,
                item.inputIndex))
            .ToArray();
        var patientBatch = PatientEpisodeAssembler.Assemble(assemblyInputs);
        var failedEntries = documents
            .Select(static (document, inputIndex) => new { document, inputIndex })
            .Where(static item => !item.document.Succeeded)
            .Select(static item => new AssemblyLedgerEntry(
                $"failed-{item.inputIndex:D4}",
                item.document.FileName,
                item.inputIndex,
                "Failed",
                null,
                0,
                0,
                0,
                0,
                item.document.ErrorMessage ?? "Falha sem mensagem disponível."))
            .ToArray();
        if (failedEntries.Length > 0)
        {
            patientBatch = patientBatch with
            {
                Ledger = patientBatch.Ledger.Concat(failedEntries)
                    .OrderBy(static entry => entry.InputIndex)
                    .ToArray(),
            };
        }

        cancellationToken.ThrowIfCancellationRequested();
        var semantic = new LaboratorySemanticExtractor().Extract(
            new LaboratorySemanticExtractionInput(patientBatch));
        semantic = DerivedMeasurementComputer.Enrich(new DerivedMeasurementComputationInput(semantic));
        var clinical = OutputProjector.Project(new OutputProjectionInput(semantic, options));
        return new ProductionSessionResult(documents, patientBatch, semantic, clinical);
    }
}

public static class ProductionRuntime
{
    public static ProcessingPipeline CreateDocumentPipeline() => new(
    [
        new PdfPigCaptureStage(),
        new RawReconstructionStage(),
        new SanitizationStage(),
    ]);

    public static ClinicalOutputBatch ProjectClinicalOutput(
        SemanticPatientBatch semanticPatientBatch,
        OutputProjectionOptions options) =>
        OutputProjector.Project(new OutputProjectionInput(semanticPatientBatch, options));
}
