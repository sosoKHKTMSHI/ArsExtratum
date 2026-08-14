using System.IO.Compression;
using System.IO;
using System.Text;
using System.Text.Json;
using ArsExtractum.App.ViewModels;
using ArsExtractum.Core.Assembly;
using ArsExtractum.Core.Documents;
using ArsExtractum.Core.Pipeline;
using ArsExtractum.Core.Sanitization;
using ArsExtractum.Core.LaboratorySemantic;
using ArsExtractum.Core.OutputProjection;

namespace ArsExtractum.App.Services;

public static class DetailedReportExporter
{
    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly JsonSerializerOptions CompactJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task ExportAsync(
        string destinationPath,
        IReadOnlyList<DocumentRunRecord> runs,
        PatientBatch? patientBatch = null,
        SemanticPatientBatch? semanticPatientBatch = null,
        ClinicalOutputBatch? clinicalOutputBatch = null,
        AuditExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(runs);
        options ??= AuditExportOptions.All;

        await using var output = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        await WriteTextAsync(
            archive,
            "README.txt",
            "ARS EXTRACTUM — RELATÓRIO TÉCNICO\n\n" +
            "Conteúdo gerado localmente por solicitação explícita do usuário.\n" +
            "Pacote seletivo para rastrear cada transformação até a projeção clínica final.\n" +
            $"Seleção: {options}.\n",
            cancellationToken);

        var summary = new
        {
            schemaVersion = "1.0",
            tool = "Ars Extractum",
            documentCount = runs.Count,
            successfulDocuments = runs.Count(static run => run.Execution is not null),
            failedDocuments = runs.Count(static run => run.Execution is null),
            patientCount = patientBatch?.Patients.Count,
            episodeCount = patientBatch?.EpisodeCount,
            semanticOccurrenceCount = semanticPatientBatch?.Coverage.OccurrenceCount,
            projectionCoverage = clinicalOutputBatch?.Coverage,
            exportOptions = options,
            assemblyLedgerCount = patientBatch?.Ledger.Count,
            assemblyLedgerFailures = patientBatch?.Ledger.Count(static entry => entry.Disposition == "Failed"),
            assemblyLedgerRejected = patientBatch?.Ledger.Count(static entry => entry.Disposition == "Rejected"),
            documents = runs.Select(run => new
            {
                fileName = run.FileName,
                success = run.Execution is not null,
                error = run.ErrorMessage,
                stages = run.Execution?.Stages.Values.Select(stage => new
                {
                    id = stage.Descriptor.Id,
                    name = stage.Descriptor.Name,
                    version = stage.Descriptor.Version,
                    durationMilliseconds = Math.Round(stage.Duration.TotalMilliseconds, 3),
                    notices = stage.Notices,
                }),
            }),
        };
        await WriteJsonAsync(archive, "00-report.json", summary, cancellationToken);

        if (options.PatientEpisodeAssembly && patientBatch is not null)
        {
            await WriteJsonAsync(
                archive,
                "batch/08-patient-assembly.json",
                patientBatch,
                cancellationToken);
            await WriteTextAsync(
                archive,
                "batch/09-patient-assembly.txt",
                PatientAssemblyTextFormatter.Format(patientBatch),
                cancellationToken);
        }

        if (options.LaboratorySemanticExtraction && semanticPatientBatch is not null)
        {
            await WriteJsonAsync(archive, "batch/10-semantic-patient-batch.json", semanticPatientBatch, cancellationToken);
            await WriteTextAsync(archive, "batch/11-semantic-patient-batch.txt",
                LaboratorySemanticTextFormatter.Format(semanticPatientBatch), cancellationToken);
            await WriteTextAsync(archive, "batch/11a-culture-review.txt",
                CultureReviewTextFormatter.Format(semanticPatientBatch), cancellationToken);
        }

        if (options.OutputProjection && clinicalOutputBatch is not null)
        {
            await WriteJsonAsync(archive, "batch/12-clinical-output-batch.json", clinicalOutputBatch, cancellationToken);
            await WriteTextAsync(archive, "batch/13-clinical-output.txt",
                ClinicalOutputTextFormatter.Format(clinicalOutputBatch), cancellationToken);
            var cultureNotices = clinicalOutputBatch.Notices.Where(static notice =>
                notice.Code == "output-projection.culture-verification-required");
            await WriteTextAsync(archive, "batch/14-projection-notices.txt",
                string.Join(Environment.NewLine, cultureNotices.Select(static notice => notice.Message)), cancellationToken);
        }

        for (var index = 0; index < runs.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var run = runs[index];
            var directory = $"documents/{index + 1:D3}-{SanitizeName(Path.GetFileNameWithoutExtension(run.FileName))}";
            if (run.Execution is null)
            {
                await WriteTextAsync(
                    archive,
                    $"{directory}/error.txt",
                    run.ErrorMessage ?? "Falha sem mensagem disponível.",
                    cancellationToken);
                continue;
            }

            if (options.Capture && run.Execution.Stages.TryGetValue(StageIds.PdfPigCapture, out var captureResult) &&
                captureResult.Payload is CaptureDocument capture)
            {
                var manifest = new
                {
                    capture.SchemaVersion,
                    capture.DocumentId,
                    capture.FileName,
                    capture.ByteLength,
                    capture.Sha256,
                    capture.PdfPigVersion,
                    capture.PageCount,
                    capture.GlyphCount,
                    capture.WordCount,
                    pages = capture.Pages.Select(static page => new
                    {
                        page.PageNumber,
                        page.Width,
                        page.Height,
                        page.RotationDegrees,
                        glyphCount = page.Glyphs.Count,
                        wordCount = page.Words.Count,
                    }),
                };
                await WriteJsonAsync(archive, $"{directory}/00-manifest.json", manifest, cancellationToken);
                await WriteJsonLinesAsync(
                    archive,
                    $"{directory}/01-glyphs.jsonl",
                    capture.Pages.SelectMany(static page => page.Glyphs.Select(glyph => new
                    {
                        page = page.PageNumber,
                        glyph,
                    })),
                    cancellationToken);
                await WriteJsonLinesAsync(
                    archive,
                    $"{directory}/02-words.jsonl",
                    capture.Pages.SelectMany(static page => page.Words.Select(word => new
                    {
                        page = page.PageNumber,
                        word,
                    })),
                    cancellationToken);
                await WriteTextAsync(
                    archive,
                    $"{directory}/03-capture-readable.txt",
                    captureResult.DisplayText,
                    cancellationToken);
            }

            if (options.Reconstruction && run.Execution.Stages.TryGetValue(StageIds.RawReconstruction, out var reconstructionResult) &&
                reconstructionResult.Payload is ReconstructedDocument reconstruction)
            {
                await WriteJsonAsync(
                    archive,
                    $"{directory}/04-reconstruction.json",
                    reconstruction,
                    cancellationToken);
                await WriteTextAsync(
                    archive,
                    $"{directory}/05-reconstruction.txt",
                    reconstructionResult.DisplayText,
                    cancellationToken);
            }

            if (options.Sanitization && run.Execution.Stages.TryGetValue(StageIds.Sanitization, out var sanitizationResult) &&
                sanitizationResult.Payload is SanitizedDocument sanitization)
            {
                await WriteJsonAsync(
                    archive,
                    $"{directory}/06-sanitization.json",
                    sanitization,
                    cancellationToken);
                await WriteTextAsync(
                    archive,
                    $"{directory}/07-sanitization.txt",
                    sanitizationResult.DisplayText,
                    cancellationToken);
            }
        }
    }

    private static async Task WriteJsonAsync<T>(
        ZipArchive archive,
        string entryName,
        T value,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, IndentedJson, cancellationToken);
    }

    private static async Task WriteJsonLinesAsync<T>(
        ZipArchive archive,
        string entryName,
        IEnumerable<T> values,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        foreach (var value in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(JsonSerializer.Serialize(value, CompactJson));
        }
    }

    private static async Task WriteTextAsync(
        ZipArchive archive,
        string entryName,
        string text,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(text.AsMemory(), cancellationToken);
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "document" : sanitized;
    }
}

public sealed record AuditExportOptions(
    bool Capture,
    bool Reconstruction,
    bool Sanitization,
    bool PatientEpisodeAssembly,
    bool LaboratorySemanticExtraction,
    bool OutputProjection)
{
    public static AuditExportOptions All { get; } = new(true, true, true, true, true, true);

    public bool AnySelected => Capture || Reconstruction || Sanitization || PatientEpisodeAssembly ||
                               LaboratorySemanticExtraction || OutputProjection;
}
