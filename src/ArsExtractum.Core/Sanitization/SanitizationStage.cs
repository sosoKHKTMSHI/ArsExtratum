using ArsExtractum.Core.Documents;
using ArsExtractum.Core.Pipeline;

namespace ArsExtractum.Core.Sanitization;

public sealed class SanitizationStage : IProcessingStage
{
    public StageDescriptor Descriptor { get; } = new(
        StageIds.Sanitization,
        "Higienização documental",
        "Estrutura o cabeçalho e retira do corpo rodapés, referências, históricos, métodos e notas catalogadas.",
        "1.0",
        [StageIds.RawReconstruction]);

    public Task<StageOutput> ExecuteAsync(
        StageContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var reconstruction = context.RequirePayload<ReconstructedDocument>(StageIds.RawReconstruction);
        var document = DocumentSanitizer.Sanitize(reconstruction);
        return Task.FromResult(new StageOutput(
            document,
            SanitizationTextFormatter.Format(document),
            BuildNotices(document)));
    }

    private static List<ProcessingNotice> BuildNotices(SanitizedDocument document)
    {
        var notices = new List<ProcessingNotice>();
        foreach (var page in document.Pages)
        {
            if (!page.Header.IsComplete)
            {
                notices.Add(new ProcessingNotice(
                    "SANITIZATION.HEADER.INCOMPLETE",
                    ProcessingNoticeSeverity.Warning,
                    "O cabeçalho não contém todos os campos mínimos esperados.",
                    page.PageNumber));
            }

            if (page.Header.UnresolvedFragments.Count > 0)
            {
                notices.Add(new ProcessingNotice(
                    "SANITIZATION.HEADER.UNRESOLVED",
                    ProcessingNoticeSeverity.Warning,
                    $"{page.Header.UnresolvedFragments.Count} fragmento(s) do cabeçalho foram preservados sem associação.",
                    page.PageNumber));
            }

            if (!page.FooterRecognized)
            {
                notices.Add(new ProcessingNotice(
                    "SANITIZATION.FOOTER.UNRECOGNIZED",
                    ProcessingNoticeSeverity.Warning,
                    "O rodapé não apresentou a assinatura completa conhecida e foi preservado.",
                    page.PageNumber));
            }
        }

        return notices;
    }
}
