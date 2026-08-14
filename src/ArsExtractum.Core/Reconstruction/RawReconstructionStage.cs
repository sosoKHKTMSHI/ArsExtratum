using System.Text;
using System.Globalization;
using ArsExtractum.Core.Documents;
using ArsExtractum.Core.Pipeline;

namespace ArsExtractum.Core.Reconstruction;

public sealed class RawReconstructionStage : IProcessingStage
{
    public StageDescriptor Descriptor { get; } = new(
        StageIds.RawReconstruction,
        "Reconstrução bruta",
        "Agrupa as palavras capturadas em páginas, linhas e células sem interpretação clínica.",
        "1.2",
        [StageIds.PdfPigCapture]);

    public Task<StageOutput> ExecuteAsync(
        StageContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var capture = context.RequirePayload<CaptureDocument>(StageIds.PdfPigCapture);
        var document = RawDocumentReconstructor.Reconstruct(capture);
        var notices = BuildNotices(document);
        return Task.FromResult(new StageOutput(
            document,
            BuildDisplayText(document),
            notices));
    }

    private static List<ProcessingNotice> BuildNotices(ReconstructedDocument document)
    {
        var notices = new List<ProcessingNotice>();
        foreach (var page in document.Pages)
        {
            if (page.Lines.Count == 0)
            {
                notices.Add(new ProcessingNotice(
                    "RECONSTRUCTION.PAGE.EMPTY",
                    ProcessingNoticeSeverity.Warning,
                    "A página não produziu linhas reconstruídas.",
                    page.PageNumber));
            }

            if (page.UnresolvedWordIds.Count > 0)
            {
                notices.Add(new ProcessingNotice(
                    "RECONSTRUCTION.WORD.UNRESOLVED",
                    ProcessingNoticeSeverity.Error,
                    $"{page.UnresolvedWordIds.Count} palavra(s) não foram incorporadas às linhas.",
                    page.PageNumber));
            }
        }

        return notices;
    }

    private static string BuildDisplayText(ReconstructedDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("RECONSTRUÇÃO DOCUMENTAL BRUTA");
        builder.Append("Arquivo: ").AppendLine(document.FileName);
        builder.Append("Páginas: ").Append(document.Pages.Count)
            .Append(" | Linhas: ").Append(document.LineCount)
            .Append(" | Células: ").Append(document.CellCount)
            .Append(" | Palavras não resolvidas: ")
            .Append(document.UnresolvedWordCount.ToString(CultureInfo.InvariantCulture))
            .Append(" | Glifos fora de palavras: ")
            .Append(document.UnassignedGlyphCount.ToString(CultureInfo.InvariantCulture))
            .Append(" | Fragmentos tipográficos reunidos: ")
            .AppendLine(document.TypographicAttachmentCount.ToString(CultureInfo.InvariantCulture));

        foreach (var page in document.Pages)
        {
            builder.AppendLine();
            builder.Append("--- PÁGINA ")
                .Append(page.PageNumber.ToString("D4", CultureInfo.InvariantCulture))
                .AppendLine(" ---");
            foreach (var line in page.Lines)
            {
                builder.AppendLine(line.DisplayText);
            }
        }

        return builder.ToString();
    }
}
