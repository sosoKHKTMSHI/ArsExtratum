using ArsExtractum.Core.Documents;

namespace ArsExtractum.Core.Reconstruction;

public static class RawDocumentReconstructor
{
    public static ReconstructedDocument Reconstruct(CaptureDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var pages = source.Pages
            .OrderBy(static page => page.PageNumber)
            .Select(ReconstructPage)
            .ToArray();

        return new ReconstructedDocument(
            "1.2",
            source.DocumentId,
            source.FileName,
            pages);
    }

    private static ReconstructedPage ReconstructPage(CapturePage page)
    {
        if (page.Words.Count == 0)
        {
            return new ReconstructedPage(
                page.PageNumber,
                page.Width,
                page.Height,
                [],
                [],
                page.Glyphs.Select(static glyph => glyph.Id).ToArray(),
                []);
        }

        var medianHeight = Median(page.Words
            .Select(static word => word.Bounds.Height)
            .Where(static height => height > 0d));
        var lineTolerance = Math.Clamp(medianHeight * 0.25d, 0.75d, 2.5d);
        var candidates = new List<LineCandidate>();

        foreach (var word in page.Words
                     .OrderByDescending(static word => word.BaselineY)
                     .ThenBy(static word => word.Bounds.Left)
                     .ThenBy(static word => word.Index))
        {
            var target = candidates
                .Select(candidate => new
                {
                    Candidate = candidate,
                    Distance = Math.Abs(candidate.BaselineY - word.BaselineY),
                })
                .Where(item => item.Distance <= lineTolerance)
                .OrderBy(static item => item.Distance)
                .ThenBy(static item => item.Candidate.FirstWordIndex)
                .Select(static item => item.Candidate)
                .FirstOrDefault();

            if (target is null)
            {
                candidates.Add(new LineCandidate(word));
            }
            else
            {
                target.Add(word);
            }
        }

        var typographicAttachments = AttachTypographicFragments(candidates);
        var baseWordIdByFragmentId = typographicAttachments.ToDictionary(
            static attachment => attachment.WordId,
            static attachment => attachment.BaseWordId,
            StringComparer.Ordinal);

        var ordered = candidates
            .OrderByDescending(static candidate => candidate.BaselineY)
            .ThenBy(static candidate => candidate.Left)
            .ToList();
        OrderStaggeredFields(ordered);
        OrderWrappedFieldLabels(ordered);
        OrderShortParentheticalContinuations(ordered);
        var lines = ordered
            .Select((candidate, index) => BuildLine(
                page,
                candidate,
                index,
                baseWordIdByFragmentId))
            .ToArray();
        var reconstructedWordIds = lines
            .SelectMany(static line => line.WordIds)
            .ToHashSet(StringComparer.Ordinal);
        var linkedGlyphIds = page.Words
            .SelectMany(static word => word.GlyphIds)
            .ToHashSet(StringComparer.Ordinal);

        return new ReconstructedPage(
            page.PageNumber,
            page.Width,
            page.Height,
            lines,
            page.Words
                .Where(word => !reconstructedWordIds.Contains(word.Id))
                .Select(static word => word.Id)
                .ToArray(),
            page.Glyphs
                .Where(glyph => !linkedGlyphIds.Contains(glyph.Id))
                .Select(static glyph => glyph.Id)
                .ToArray(),
            typographicAttachments);
    }

    private static void OrderStaggeredFields(List<LineCandidate> candidates)
    {
        for (var index = 1; index < candidates.Count; index++)
        {
            var label = candidates[index];
            var labelWord = FindExplicitFieldLabelWord(label);
            if (labelWord is null ||
                !IsRightHandCompanion(label, labelWord, candidates[index - 1]))
            {
                continue;
            }

            var trailingContent = SplitTrailingFieldContent(label, labelWord);
            // Alguns formulários desenham o valor ligeiramente acima do rótulo.
            (candidates[index - 1], candidates[index]) =
                (candidates[index], candidates[index - 1]);
            if (trailingContent is not null)
            {
                candidates.Insert(index + 1, trailingContent);
            }

        }
    }

    private static void OrderWrappedFieldLabels(List<LineCandidate> candidates)
    {
        for (var index = 0; index + 2 < candidates.Count; index++)
        {
            var firstPart = candidates[index];
            var rightHandContent = candidates[index + 1];
            var continuation = candidates[index + 2];
            var continuationLabel = FindExplicitFieldLabelWord(continuation);
            var horizontalTolerance = Math.Max(
                2d,
                Math.Max(firstPart.VisualHeight, continuation.VisualHeight) * 0.5d);
            var verticalTolerance = Math.Max(
                firstPart.VisualHeight,
                Math.Max(rightHandContent.VisualHeight, continuation.VisualHeight));

            if (FindExplicitFieldLabelWord(firstPart) is not null ||
                continuationLabel is null ||
                Math.Abs(firstPart.Left - continuation.Left) > horizontalTolerance ||
                Math.Abs(firstPart.Bounds.Right - continuation.Bounds.Right) > horizontalTolerance ||
                rightHandContent.Left <= continuation.Bounds.Right + 2d ||
                firstPart.BaselineY <= rightHandContent.BaselineY ||
                rightHandContent.BaselineY <= continuation.BaselineY ||
                firstPart.BaselineY - rightHandContent.BaselineY > verticalTolerance ||
                rightHandContent.BaselineY - continuation.BaselineY > verticalTolerance)
            {
                continue;
            }

            // Caixas alinhadas podem dividir um rótulo em torno da coluna de resultado.
            (candidates[index + 1], candidates[index + 2]) =
                (candidates[index + 2], candidates[index + 1]);
            index += 2;
        }
    }

    private static CapturedWord? FindExplicitFieldLabelWord(LineCandidate candidate) =>
        candidate.Words.FirstOrDefault(static word =>
            word.Text.EndsWith(':') && word.Text.Contains("...", StringComparison.Ordinal));

    private static LineCandidate? SplitTrailingFieldContent(
        LineCandidate candidate,
        CapturedWord labelWord)
    {
        var trailingWords = candidate.Words
            .Where(word => word.Bounds.Left > labelWord.Bounds.Right)
            .OrderBy(static word => word.Bounds.Left)
            .ThenBy(static word => word.Index)
            .ToArray();
        if (trailingWords.Length == 0)
        {
            return null;
        }

        foreach (var word in trailingWords)
        {
            candidate.Words.Remove(word);
        }

        var trailingContent = new LineCandidate(trailingWords[0]);
        foreach (var word in trailingWords.Skip(1))
        {
            trailingContent.Add(word);
        }

        return trailingContent;
    }

    private static bool IsRightHandCompanion(
        LineCandidate label,
        CapturedWord labelWord,
        LineCandidate companion)
    {
        if (companion.Left <= labelWord.Bounds.Right + 2d)
        {
            return false;
        }

        var trailingWords = label.Words
            .Where(word => word.Bounds.Left > labelWord.Bounds.Right)
            .ToArray();
        if (trailingWords.Length > 0)
        {
            var trailingLeft = trailingWords.Min(static word => word.Bounds.Left);
            var verticalGap = Math.Max(0d, companion.Bounds.Bottom - label.Bounds.Top);
            return Math.Abs(companion.Left - trailingLeft) <= 5d &&
                   verticalGap <= Math.Max(label.VisualHeight, companion.VisualHeight) * 0.5d;
        }

        var overlap = VerticalOverlap(labelWord.Bounds, companion.Bounds);
        var shorterHeight = Math.Min(labelWord.Bounds.Height, companion.VisualHeight);
        return overlap / Math.Max(shorterHeight, 0.001d) >= 0.15d &&
               Math.Abs(label.BaselineY - companion.BaselineY) <=
               Math.Max(label.VisualHeight, companion.VisualHeight);
    }

    private static void OrderShortParentheticalContinuations(
        List<LineCandidate> candidates)
    {
        for (var openerIndex = 0; openerIndex < candidates.Count; openerIndex++)
        {
            var opener = candidates[openerIndex];
            if (opener.Words.Count > 5 || ParenthesisBalance(opener.PlainText) <= 0)
            {
                continue;
            }

            var continuationEnd = Math.Min(candidates.Count - 1, openerIndex + 3);
            for (var continuationIndex = openerIndex + 1;
                 continuationIndex <= continuationEnd;
                 continuationIndex++)
            {
                var continuation = candidates[continuationIndex];
                if (continuation.Words.Count > 2 ||
                    ParenthesisBalance(continuation.PlainText) >= 0)
                {
                    continue;
                }

                candidates.RemoveAt(continuationIndex);
                candidates.Insert(openerIndex + 1, continuation);
                return;
            }
        }
    }

    private static int ParenthesisBalance(string text) =>
        text.Count(static character => character == '(') -
        text.Count(static character => character == ')');

    private static List<TypographicAttachment> AttachTypographicFragments(
        List<LineCandidate> candidates)
    {
        var attachments = new List<TypographicAttachment>();
        var fragments = candidates
            .Where(static candidate => candidate.Words.Count == 1)
            .Select(static candidate => new
            {
                Candidate = candidate,
                Word = candidate.Words[0],
            })
            .Where(static item => IsShortNumericFragment(item.Word.Text))
            .ToArray();

        foreach (var fragment in fragments)
        {
            // O PdfPig pode separar índices tipográficos da palavra-base.
            var target = FindTypographicBase(fragment.Word, fragment.Candidate, candidates);
            if (target is null)
            {
                continue;
            }

            target.Candidate.Add(fragment.Word);
            candidates.Remove(fragment.Candidate);
            attachments.Add(new TypographicAttachment(
                fragment.Word.Id,
                target.BaseWord.Id,
                fragment.Word.BaselineY > target.BaseWord.BaselineY
                    ? "superscript"
                    : "subscript"));
        }

        return attachments;
    }

    private static TypographicBase? FindTypographicBase(
        CapturedWord fragment,
        LineCandidate fragmentCandidate,
        IEnumerable<LineCandidate> candidates)
    {
        var fragmentHeight = fragment.Bounds.Height;
        if (fragmentHeight <= 0d)
        {
            return null;
        }

        return candidates
            .Where(candidate => !ReferenceEquals(candidate, fragmentCandidate))
            .SelectMany(candidate => candidate.Words.Select(baseWord => new
            {
                Candidate = candidate,
                BaseWord = baseWord,
                Gap = fragment.Bounds.Left - baseWord.Bounds.Right,
                Overlap = VerticalOverlap(fragment.Bounds, candidate.Bounds),
                // A altura da linha é mais estável que a caixa de letras como "mm".
                LineHeight = candidate.VisualHeight,
                BaselineDistance = Math.Abs(fragment.BaselineY - baseWord.BaselineY),
            }))
            .Where(item =>
                item.Gap >= -0.5d &&
                item.Gap <= Math.Max(4d, item.LineHeight * 0.75d) &&
                item.Overlap / fragmentHeight >= 0.4d &&
                fragmentHeight / Math.Max(item.LineHeight, 0.001d) <= 0.75d &&
                item.BaselineDistance >= 1d)
            .OrderBy(static item => item.Gap)
            .ThenByDescending(static item => item.Overlap)
            .ThenBy(static item => item.BaselineDistance)
            .Select(static item => new TypographicBase(item.Candidate, item.BaseWord))
            .FirstOrDefault();
    }

    private static bool IsShortNumericFragment(string text) =>
        text.Length is 1 or 2 && text.All(char.IsAsciiDigit);

    private static double VerticalOverlap(PdfBounds first, PdfBounds second) =>
        Math.Max(0d, Math.Min(first.Top, second.Top) - Math.Max(first.Bottom, second.Bottom));

    private static ReconstructedLine BuildLine(
        CapturePage page,
        LineCandidate candidate,
        int lineIndex,
        IReadOnlyDictionary<string, string> baseWordIdByFragmentId)
    {
        var words = candidate.Words
            .OrderBy(static word => word.Bounds.Left)
            .ThenBy(static word => word.Index)
            .ToArray();
        var medianHeight = Median(words
            .Select(static word => word.Bounds.Height)
            .Where(static height => height > 0d));
        var cellGapThreshold = Math.Max(page.Width * 0.02d, medianHeight * 1.8d);
        var cellWordGroups = new List<List<CapturedWord>>
        {
            new() { words[0] },
        };

        for (var index = 1; index < words.Length; index++)
        {
            var gap = words[index].Bounds.Left - words[index - 1].Bounds.Right;
            if (gap > cellGapThreshold)
            {
                cellWordGroups.Add([]);
            }

            cellWordGroups[^1].Add(words[index]);
        }

        var lineId = $"p{page.PageNumber:D4}-l{lineIndex:D4}";
        var cells = cellWordGroups
            .Select((group, index) => new ReconstructedCell(
                $"{lineId}-c{index:D2}",
                index,
                JoinWords(group, baseWordIdByFragmentId),
                PdfBounds.Union(group.Select(static word => word.Bounds)),
                group.Select(static word => word.Id).ToArray()))
            .ToArray();

        return new ReconstructedLine(
            lineId,
            lineIndex,
            JoinWords(words, baseWordIdByFragmentId),
            string.Join(" | ", cells.Select(static cell => cell.Text)),
            PdfBounds.Union(words.Select(static word => word.Bounds)),
            candidate.BaselineY,
            words.Select(static word => word.Id).ToArray(),
            cells);
    }

    private static string JoinWords(
        IEnumerable<CapturedWord> source,
        IReadOnlyDictionary<string, string> baseWordIdByFragmentId)
    {
        var words = source.ToArray();
        if (words.Length == 0)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(words[0].Text);
        for (var index = 1; index < words.Length; index++)
        {
            if (!baseWordIdByFragmentId.TryGetValue(words[index].Id, out var baseWordId) ||
                !string.Equals(baseWordId, words[index - 1].Id, StringComparison.Ordinal))
            {
                builder.Append(' ');
            }

            builder.Append(words[index].Text);
        }

        return builder.ToString();
    }

    private static double Median(IEnumerable<double> source)
    {
        var values = source.Order().ToArray();
        if (values.Length == 0)
        {
            return 1d;
        }

        var middle = values.Length / 2;
        return values.Length % 2 == 0
            ? (values[middle - 1] + values[middle]) / 2d
            : values[middle];
    }

    private sealed class LineCandidate
    {
        public LineCandidate(CapturedWord firstWord)
        {
            Words.Add(firstWord);
        }

        public List<CapturedWord> Words { get; } = [];

        public double BaselineY => Median(Words.Select(static word => word.BaselineY));

        public double Left => Words.Min(static word => word.Bounds.Left);

        public string PlainText => string.Join(
            " ",
            Words
                .OrderBy(static word => word.Bounds.Left)
                .ThenBy(static word => word.Index)
                .Select(static word => word.Text));

        public double VisualHeight =>
            Words.Max(static word => word.Bounds.Top) -
            Words.Min(static word => word.Bounds.Bottom);

        public PdfBounds Bounds => PdfBounds.Union(
            Words.Select(static word => word.Bounds));

        public int FirstWordIndex => Words.Min(static word => word.Index);

        public void Add(CapturedWord word) => Words.Add(word);
    }

    private sealed record TypographicBase(LineCandidate Candidate, CapturedWord BaseWord);
}
