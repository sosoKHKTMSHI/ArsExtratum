using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ArsExtractum.Core.Documents;
using ArsExtractum.Core.Pipeline;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace ArsExtractum.PdfPig;

public sealed class PdfPigCaptureStage : IProcessingStage
{
    private const long MaximumPdfBytes = 256L * 1024L * 1024L;

    public StageDescriptor Descriptor { get; } = new(
        StageIds.PdfPigCapture,
        "Captura PdfPig",
        "Captura páginas, glifos, palavras padrão, coordenadas e vínculos de origem.",
        "1.0",
        []);

    public Task<StageOutput> ExecuteAsync(
        StageContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateInput(context.Source);

        var hash = Convert.ToHexString(SHA256.HashData(context.Source.Content))
            .ToLowerInvariant();
        var documentId = $"doc-{hash[..12]}";
        var notices = new List<ProcessingNotice>();
        var pages = new List<CapturePage>();

        try
        {
            using var document = PdfDocument.Open(context.Source.Content);
            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                pages.Add(CapturePage(page, notices));
            }
        }
        catch (PdfDocumentFormatException exception)
        {
            throw new InvalidDataException(
                "O PDF está corrompido, protegido ou usa uma estrutura incompatível.",
                exception);
        }

        if (pages.Count == 0)
        {
            throw new InvalidDataException("O PDF não contém páginas legíveis.");
        }

        var capture = new CaptureDocument(
            "1.0",
            documentId,
            context.Source.FileName,
            context.Source.Content.LongLength,
            hash,
            GetPdfPigVersion(),
            pages);
        return Task.FromResult(new StageOutput(
            capture,
            BuildDisplayText(capture),
            notices));
    }

    private static CapturePage CapturePage(
        Page page,
        List<ProcessingNotice> notices)
    {
        var letters = page.Letters.ToArray();
        var glyphIdsByReference = new Dictionary<Letter, string>(ReferenceEqualityComparer.Instance);
        var glyphIdsByGeometry = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var glyphs = new List<CapturedGlyph>(letters.Length);

        for (var index = 0; index < letters.Length; index++)
        {
            var letter = letters[index];
            var id = $"p{page.Number:D4}-g{index:D6}";
            var captured = new CapturedGlyph(
                id,
                index,
                letter.Value,
                ToBounds(letter.BoundingBox),
                ToPoint(letter.StartBaseLine),
                ToPoint(letter.EndBaseLine),
                letter.FontName,
                FiniteOrNull(letter.FontSize),
                letter.TextOrientation.ToString());
            glyphs.Add(captured);
            glyphIdsByReference[letter] = id;

            var key = GlyphKey(letter);
            if (!glyphIdsByGeometry.TryGetValue(key, out var ids))
            {
                ids = [];
                glyphIdsByGeometry.Add(key, ids);
            }

            ids.Add(id);
        }

        var words = page.GetWords().ToArray();
        var capturedWords = new List<CapturedWord>(words.Length);
        var missingGlyphLinks = 0;
        for (var index = 0; index < words.Length; index++)
        {
            var word = words[index];
            var glyphIds = new List<string>(word.Letters.Count);
            foreach (var letter in word.Letters)
            {
                if (glyphIdsByReference.TryGetValue(letter, out var glyphId))
                {
                    glyphIds.Add(glyphId);
                    continue;
                }

                if (glyphIdsByGeometry.TryGetValue(GlyphKey(letter), out var ids) && ids.Count > 0)
                {
                    glyphIds.Add(ids[0]);
                    continue;
                }

                missingGlyphLinks++;
            }

            var baselineValues = word.Letters
                .Select(static letter => letter.StartBaseLine.Y)
                .Where(double.IsFinite)
                .Order()
                .ToArray();
            var baseline = baselineValues.Length == 0
                ? word.BoundingBox.Bottom
                : baselineValues[baselineValues.Length / 2];
            var firstLetter = word.Letters.Count == 0 ? null : word.Letters[0];
            capturedWords.Add(new CapturedWord(
                $"p{page.Number:D4}-w{index:D6}",
                index,
                word.Text,
                ToBounds(word.BoundingBox),
                Round(baseline),
                firstLetter?.FontName,
                glyphIds,
                word.TextOrientation.ToString()));
        }

        if (missingGlyphLinks > 0)
        {
            notices.Add(new ProcessingNotice(
                "PDFPIG.WORD.GLYPH_LINK_MISSING",
                ProcessingNoticeSeverity.Warning,
                $"{missingGlyphLinks} vínculo(s) palavra-glifo não puderam ser resolvidos.",
                page.Number));
        }

        if (glyphs.Count > 0 && capturedWords.Count == 0)
        {
            notices.Add(new ProcessingNotice(
                "PDFPIG.WORDS.EMPTY",
                ProcessingNoticeSeverity.Warning,
                "A página possui glifos, mas o extrator padrão não produziu palavras.",
                page.Number));
        }

        return new CapturePage(
            page.Number,
            Round(page.Width),
            Round(page.Height),
            page.Rotation.Value,
            glyphs,
            capturedWords);
    }

    private static string BuildDisplayText(CaptureDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("CAPTURA PDFPIG");
        builder.Append("Arquivo: ").AppendLine(document.FileName);
        builder.Append("SHA-256: ").AppendLine(document.Sha256);
        builder.Append("PdfPig: ").AppendLine(document.PdfPigVersion);
        builder.Append("Páginas: ").Append(document.PageCount)
            .Append(" | Glifos: ").Append(document.GlyphCount)
            .Append(" | Palavras: ").AppendLine(document.WordCount.ToString(CultureInfo.InvariantCulture));

        foreach (var page in document.Pages)
        {
            builder.AppendLine();
            builder.Append("--- PÁGINA ").Append(page.PageNumber.ToString("D4", CultureInfo.InvariantCulture))
                .Append(" | ").Append(page.Width.ToString("0.####", CultureInfo.InvariantCulture))
                .Append('x').Append(page.Height.ToString("0.####", CultureInfo.InvariantCulture))
                .Append(" | rotação ").Append(page.RotationDegrees)
                .AppendLine("° ---");
            foreach (var word in page.Words)
            {
                builder.Append(word.Id).Append('\t')
                    .Append('[')
                    .Append(word.Bounds.Left.ToString("0.####", CultureInfo.InvariantCulture)).Append(',')
                    .Append(word.Bounds.Bottom.ToString("0.####", CultureInfo.InvariantCulture)).Append(',')
                    .Append(word.Bounds.Right.ToString("0.####", CultureInfo.InvariantCulture)).Append(',')
                    .Append(word.Bounds.Top.ToString("0.####", CultureInfo.InvariantCulture)).Append(']')
                    .Append('\t').AppendLine(EscapeInline(word.Text));
            }
        }

        return builder.ToString();
    }

    private static void ValidateInput(SourcePdf source)
    {
        if (source.Content.LongLength == 0 || source.Content.LongLength > MaximumPdfBytes)
        {
            throw new InvalidDataException("O arquivo está vazio ou excede o limite de 256 MB.");
        }

        if (source.Content.Length < 5 ||
            source.Content[0] != '%' ||
            source.Content[1] != 'P' ||
            source.Content[2] != 'D' ||
            source.Content[3] != 'F' ||
            source.Content[4] != '-')
        {
            throw new InvalidDataException("O arquivo não possui uma assinatura PDF válida.");
        }
    }

    private static PdfBounds ToBounds(PdfRectangle rectangle) => new(
        Round(rectangle.Left),
        Round(rectangle.Bottom),
        Round(rectangle.Right),
        Round(rectangle.Top));

    private static Core.Documents.PdfPoint ToPoint(UglyToad.PdfPig.Core.PdfPoint point) =>
        new(Round(point.X), Round(point.Y));

    private static string GlyphKey(Letter letter)
    {
        var bounds = letter.BoundingBox;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{letter.Value}|{Round(bounds.Left):0.####}|{Round(bounds.Bottom):0.####}|{Round(bounds.Right):0.####}|{Round(bounds.Top):0.####}");
    }

    private static double Round(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new InvalidDataException("O PDF contém geometria não finita.");
        }

        return Math.Round(value, 4, MidpointRounding.ToEven);
    }

    private static double? FiniteOrNull(double value) =>
        double.IsFinite(value) ? Round(value) : null;

    private static string GetPdfPigVersion() =>
        typeof(PdfDocument).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? typeof(PdfDocument).Assembly.GetName().Version?.ToString()
        ?? "desconhecida";

    private static string EscapeInline(string text) => text
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\t", "\\t", StringComparison.Ordinal);
}
