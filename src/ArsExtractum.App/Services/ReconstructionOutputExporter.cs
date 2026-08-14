using System.IO;
using System.IO.Compression;
using System.Text.Json;
using ArsExtractum.App.ViewModels;
using ArsExtractum.Core.Documents;
using ArsExtractum.Core.Pipeline;

namespace ArsExtractum.App.Services;

public static class ReconstructionOutputExporter
{
    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task ExportZipAsync(
        string destinationPath,
        IReadOnlyList<DocumentRunRecord> runs,
        DateTimeOffset generatedAt,
        CancellationToken cancellationToken = default)
    {
        var documents = GetReconstructions(runs);
        var timestamp = generatedAt.ToString("ddMMHHmm", System.Globalization.CultureInfo.InvariantCulture);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var output = CreateOutput(destinationPath);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stem = SanitizeName(Path.GetFileNameWithoutExtension(document.FileName));
            var entryName = BuildUniqueName(stem, timestamp, usedNames);
            var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
            await using var stream = entry.Open();
            await JsonSerializer.SerializeAsync(
                stream,
                document.Reconstruction,
                IndentedJson,
                cancellationToken);
        }
    }

    public static async Task ExportSingleFileAsync(
        string destinationPath,
        IReadOnlyList<DocumentRunRecord> runs,
        DateTimeOffset generatedAt,
        CancellationToken cancellationToken = default)
    {
        var documents = GetReconstructions(runs);
        var bundle = new
        {
            schemaVersion = "1.0",
            type = "ars-extractum.reconstruction-bundle",
            generatedAt,
            documentCount = documents.Length,
            documents = documents.Select((document, index) => new
            {
                documentNumber = index + 1,
                sourceFileName = document.FileName,
                reconstruction = document.Reconstruction,
            }),
        };

        await using var output = CreateOutput(destinationPath);
        await JsonSerializer.SerializeAsync(output, bundle, IndentedJson, cancellationToken);
    }

    private static ReconstructionExportItem[] GetReconstructions(
        IReadOnlyList<DocumentRunRecord> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        var documents = runs
            .Where(static run => run.Execution is not null)
            .Select(run => new
            {
                run.FileName,
                Stage = run.Execution!.Stages.GetValueOrDefault(StageIds.RawReconstruction),
            })
            .Where(static item => item.Stage?.Payload is ReconstructedDocument)
            .Select(static item => new ReconstructionExportItem(
                item.FileName,
                (ReconstructedDocument)item.Stage!.Payload))
            .ToArray();
        return documents.Length == 0
            ? throw new InvalidOperationException("Nenhuma reconstrução concluída está disponível para exportação.")
            : documents;
    }

    private static FileStream CreateOutput(string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        return new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous);
    }

    private static string BuildUniqueName(
        string stem,
        string timestamp,
        HashSet<string> usedNames)
    {
        for (var suffix = 1; ; suffix++)
        {
            var uniqueStem = suffix == 1 ? stem : $"{stem}-{suffix:D2}";
            var name = $"{uniqueStem}-reconstruction-{timestamp}.json";
            if (usedNames.Add(name))
            {
                return name;
            }
        }
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "document" : sanitized;
    }

    private sealed record ReconstructionExportItem(
        string FileName,
        ReconstructedDocument Reconstruction);
}
