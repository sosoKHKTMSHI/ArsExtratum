using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ArsExtractum.App.ViewModels;
using ArsExtractum.Core.Assembly;
using ArsExtractum.Core.LaboratorySemantic;
using ArsExtractum.Core.OutputProjection;

namespace ArsExtractum.App.Services;

public static class CurrentOutputsExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task ExportAsync(
        string destinationPath,
        IReadOnlyList<DocumentRunRecord> runs,
        PatientBatch? patientBatch,
        SemanticPatientBatch? semanticPatientBatch,
        ClinicalOutputBatch? clinicalOutputBatch = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(runs);

        var documentRuns = runs.Select(run => new
        {
            run.FileName,
            Success = run.Execution is not null,
            Error = run.ErrorMessage,
        }).ToArray();
        var payload = new
        {
            SchemaVersion = "current-outputs/1.2",
            PatientEpisodeAssembly = patientBatch,
            LaboratorySemanticExtraction = semanticPatientBatch,
            OutputProjection = clinicalOutputBatch,
            DocumentRuns = documentRuns,
        };

        await using var output = new FileStream(
            destinationPath, FileMode.Create, FileAccess.Write, FileShare.None,
            128 * 1024, FileOptions.Asynchronous);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        await WriteTextAsync(
            archive,
            "README.txt",
            "ARS EXTRACTUM — OUTPUTS ATUAIS\n\n" +
            "Pacote compacto para auditoria da montagem documental, extração semântica laboratorial v1 " +
            "cálculo derivado CKD-EPI 2021 e Output Projection v1.\n" +
            "Capture, reconstrução e higienização não são repetidos neste arquivo.\n",
            cancellationToken);

        var jsonEntry = archive.CreateEntry("current-output.json", CompressionLevel.Fastest);
        await using (var stream = jsonEntry.Open())
        {
            await JsonSerializer.SerializeAsync(stream, payload, JsonOptions, cancellationToken);
        }

        var text = new StringBuilder();
        if (patientBatch is not null)
        {
            text.AppendLine(PatientAssemblyTextFormatter.Format(patientBatch).TrimEnd());
        }

        if (semanticPatientBatch is not null)
        {
            if (text.Length > 0)
            {
                text.AppendLine().AppendLine();
            }

            text.AppendLine(LaboratorySemanticTextFormatter.Format(semanticPatientBatch).TrimEnd());
            await WriteTextAsync(archive, "culture-review.txt",
                CultureReviewTextFormatter.Format(semanticPatientBatch), cancellationToken);
        }

        if (clinicalOutputBatch is not null)
        {
            if (text.Length > 0)
            {
                text.AppendLine().AppendLine();
            }

            text.AppendLine("OUTPUT CLÍNICO FINAL");
            text.AppendLine(ClinicalOutputTextFormatter.Format(clinicalOutputBatch));
        }

        await WriteTextAsync(archive, "current-output.txt", text.ToString(), cancellationToken);
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
}
