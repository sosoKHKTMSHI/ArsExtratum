using System.Text.RegularExpressions;
using ArsExtractum.Core.Documents;

namespace ArsExtractum.Core.Sanitization;

public static partial class DocumentSanitizer
{
    private const string HeaderRule = "structure.header.standard.v1";
    private const string FooterRule = "structure.footer.standard.v1";
    private const string ReferenceRule = "content.reference-column.v1";
    private const string HistoryRule = "content.previous-results.v1";
    private const string MethodRule = "content.method.v1";
    private const string EmptyLabelRule = "content.empty-label.v1";
    private const string TextRule = "text.normalize-technical.v1";
    private const string TypographicRule = "text.attach-numeric-script.v1";
    private const string ParentheticalRule = "text.join-parenthetical-continuation.v1";
    private const string CalculationInputRule = "content.calculation-input.v1";

    public static SanitizedDocument Sanitize(ReconstructedDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var pages = source.Pages
            .OrderBy(static page => page.PageNumber)
            .Select(SanitizePage)
            .ToArray();

        return new SanitizedDocument(
            "1.0",
            SpuriousNoteCatalog.Version,
            source.DocumentId,
            source.FileName,
            pages);
    }

    private static SanitizedPage SanitizePage(ReconstructedPage page)
    {
        var lines = page.Lines.OrderBy(static line => line.Index).ToArray();
        var headerEnd = Array.FindIndex(
            lines,
            static line => StartsWithLabel(line.Text, "Data Col"));
        var footerStart = FindFooterStart(lines);
        var header = ParseHeader(lines, headerEnd);
        var output = new List<SanitizedLine>(lines.Length);
        SpuriousNoteRule? activeNote = null;
        var methodContinuation = false;
        var historyContinuation = false;
        double? referenceColumnLeft = null;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (index <= headerEnd)
            {
                output.Add(WholeLine(line, SanitizedDisposition.Header, HeaderRule));
                continue;
            }

            if (footerStart >= 0 && index >= footerStart)
            {
                output.Add(WholeLine(line, SanitizedDisposition.Footer, FooterRule));
                continue;
            }

            var normalized = NormalizeText(line.DisplayText);
            if (IsMajorSectionTitle(normalized) &&
                (referenceColumnLeft is null ||
                 LineLeft(line) < referenceColumnLeft.Value - 2d))
            {
                referenceColumnLeft = null;
            }

            if (IsPreviousResultsMarker(normalized))
            {
                historyContinuation = true;
                activeNote = null;
                methodContinuation = false;
                output.Add(WholeLine(line, SanitizedDisposition.History, HistoryRule));
                continue;
            }

            if (historyContinuation)
            {
                if (!IsStructuralBoundary(normalized))
                {
                    output.Add(WholeLine(line, SanitizedDisposition.History, HistoryRule));
                    continue;
                }

                historyContinuation = false;
            }

            var nextText = index + 1 < lines.Length
                ? NormalizeText(lines[index + 1].DisplayText)
                : null;
            var noteRule = SpuriousNoteCatalog.Match(normalized) ??
                           SpuriousNoteCatalog.MatchBlockStart(normalized, nextText);
            if (noteRule is not null)
            {
                activeNote = noteRule;
                methodContinuation = false;
                output.Add(WholeLine(
                    line,
                    SanitizedDisposition.BoilerplateNote,
                    noteRule.Id));
                continue;
            }

            if (activeNote is not null)
            {
                if (activeNote.ContinuesUntilFooter ||
                    (!IsStructuralBoundary(normalized) && !IsAnyNoteMarker(normalized)))
                {
                    output.Add(WholeLine(
                        line,
                        SanitizedDisposition.BoilerplateNote,
                        activeNote.Id));
                    continue;
                }

                activeNote = null;
            }

            if (IsMethodLine(normalized))
            {
                methodContinuation = ParenthesisBalance(normalized) > 0 ||
                                     normalized.EndsWith('-') ||
                                     normalized.EndsWith('=');
                output.Add(WholeLine(line, SanitizedDisposition.Method, MethodRule));
                continue;
            }

            if (CalculationInputRegex().IsMatch(normalized))
            {
                output.Add(WholeLine(line, SanitizedDisposition.Method, CalculationInputRule));
                continue;
            }

            if (methodContinuation)
            {
                if (!IsStructuralBoundary(normalized) && !IsAnyNoteMarker(normalized))
                {
                    output.Add(WholeLine(line, SanitizedDisposition.Method, MethodRule));
                    methodContinuation = ParenthesisBalance(normalized) > 0 ||
                                         normalized.EndsWith('-') ||
                                         normalized.EndsWith('=');
                    continue;
                }

                methodContinuation = false;
            }

            if (IsEmptyObservationLabel(normalized))
            {
                output.Add(WholeLine(line, SanitizedDisposition.EmptyLabel, EmptyLabelRule));
                continue;
            }

            if (IsReferenceFragment(normalized))
            {
                referenceColumnLeft = null;
                output.Add(WholeLine(line, SanitizedDisposition.Reference, ReferenceRule));
                continue;
            }

            // Material remains clinical content even when printed inside a reference band.
            if (StartsWithLabel(normalized, "Material"))
            {
                output.Add(SanitizeMaterialLine(line, referenceColumnLeft));
                continue;
            }

            // A left-side heading is a reference block, not a persistent right column.
            if (LooksLikeFieldLabel(normalized) &&
                referenceColumnLeft is not null &&
                referenceColumnLeft.Value < page.Width / 2d)
            {
                referenceColumnLeft = null;
            }

            output.Add(SanitizeActiveLine(line, page.Width, ref referenceColumnLeft));
        }

        AttachSeparatedNumericScripts(output, lines);
        JoinParentheticalContinuations(output);

        return new SanitizedPage(
            page.PageNumber,
            header,
            output,
            footerStart >= 0);
    }

    private static void JoinParentheticalContinuations(List<SanitizedLine> lines)
    {
        for (var index = 0; index + 1 < lines.Count; index++)
        {
            var opener = lines[index];
            var continuation = lines[index + 1];
            if (opener.Disposition != SanitizedDisposition.Active ||
                continuation.Disposition != SanitizedDisposition.Active ||
                continuation.SourceIndex != opener.SourceIndex + 1 ||
                ParenthesisBalance(opener.Text) <= 0 ||
                ParenthesisBalance(continuation.Text) >= 0)
            {
                continue;
            }

            lines[index] = opener with
            {
                Text = opener.Text + " " + continuation.Text,
                AppliedRuleIds = opener.AppliedRuleIds.Append(ParentheticalRule).Distinct().ToArray(),
            };
            lines[index + 1] = continuation with
            {
                Disposition = SanitizedDisposition.TextContinuation,
                AppliedRuleIds = continuation.AppliedRuleIds.Append(ParentheticalRule).Distinct().ToArray(),
                SuppressedSegments = continuation.SuppressedSegments.Append(
                    new SuppressedTextSegment(
                        continuation.Text,
                        SanitizedDisposition.TextContinuation,
                        ParentheticalRule)).ToArray(),
            };
        }
    }

    private static int ParenthesisBalance(string text) =>
        text.Count(static character => character == '(') -
        text.Count(static character => character == ')');

    private static double LineLeft(ReconstructedLine line) =>
        line.Cells.Count == 0
            ? line.Bounds.Left
            : line.Cells.Min(static cell => cell.Bounds.Left);

    private static void AttachSeparatedNumericScripts(
        List<SanitizedLine> sanitized,
        ReconstructedLine[] source)
    {
        for (var index = 0; index + 1 < sanitized.Count; index++)
        {
            var script = sanitized[index];
            if (script.Disposition != SanitizedDisposition.Active ||
                script.Text is not ("2" or "3"))
            {
                continue;
            }

            var target = sanitized[index + 1];
            if (target.Disposition != SanitizedDisposition.Active ||
                target.SourceIndex != script.SourceIndex + 1)
            {
                continue;
            }

            var sourceScript = source.First(line => line.Index == script.SourceIndex);
            var sourceTarget = source.First(line => line.Index == target.SourceIndex);
            var scriptCell = sourceScript.Cells.FirstOrDefault(cell =>
                NormalizeText(cell.Text) == script.Text);
            if (scriptCell is null)
            {
                continue;
            }

            var baseCell = sourceTarget.Cells
                .Where(cell => Math.Abs(cell.Bounds.Right - scriptCell.Bounds.Left) <= 4d)
                .OrderBy(cell => Math.Abs(cell.Bounds.Right - scriptCell.Bounds.Left))
                .FirstOrDefault();
            if (baseCell is null ||
                !target.Text.EndsWith(NormalizeText(baseCell.Text), StringComparison.Ordinal))
            {
                continue;
            }

            var superscript = script.Text == "2" ? "²" : "³";
            sanitized[index] = script with
            {
                Disposition = SanitizedDisposition.TypographicAttachment,
                AppliedRuleIds = script.AppliedRuleIds.Append(TypographicRule).Distinct().ToArray(),
                SuppressedSegments = script.SuppressedSegments.Append(
                    new SuppressedTextSegment(
                        script.Text,
                        SanitizedDisposition.TypographicAttachment,
                        TypographicRule)).ToArray(),
            };
            sanitized[index + 1] = target with
            {
                Text = target.Text + superscript,
                AppliedRuleIds = target.AppliedRuleIds.Append(TypographicRule).Distinct().ToArray(),
            };
        }
    }

    private static SanitizedLine SanitizeActiveLine(
        ReconstructedLine line,
        double pageWidth,
        ref double? referenceColumnLeft)
    {
        var activeParts = new List<string>();
        var suppressed = new List<SuppressedTextSegment>();
        var rules = new List<string>();
        foreach (var cell in line.Cells.OrderBy(static cell => cell.Index))
        {
            var text = NormalizeText(cell.Text);
            if (IsEmptyObservationLabel(text) && cell.Bounds.Left >= pageWidth / 2d)
            {
                referenceColumnLeft = cell.Bounds.Left;
                suppressed.Add(new SuppressedTextSegment(
                    text,
                    SanitizedDisposition.EmptyLabel,
                    EmptyLabelRule));
                rules.Add(EmptyLabelRule);
                continue;
            }

            var referenceMatch = ReferenceMarkerRegex().Match(text);
            if (referenceMatch.Success)
            {
                referenceColumnLeft = cell.Bounds.Left;
                var before = text[..referenceMatch.Index].TrimEnd(' ', '|');
                if (before.Length > 0)
                {
                    activeParts.Add(before);
                }

                suppressed.Add(new SuppressedTextSegment(
                    text[referenceMatch.Index..].Trim(),
                    SanitizedDisposition.Reference,
                    ReferenceRule));
                rules.Add(ReferenceRule);
                continue;
            }

            if (referenceColumnLeft is not null &&
                cell.Bounds.Left >= referenceColumnLeft.Value - 2d)
            {
                suppressed.Add(new SuppressedTextSegment(
                    text,
                    SanitizedDisposition.Reference,
                    ReferenceRule));
                rules.Add(ReferenceRule);
                continue;
            }

            activeParts.Add(text);
        }

        var activeText = JoinActiveParts(activeParts);
        var disposition = activeText.Length == 0 && suppressed.Count > 0
            ? SanitizedDisposition.Reference
            : SanitizedDisposition.Active;
        if (!string.Equals(activeText, line.DisplayText, StringComparison.Ordinal))
        {
            rules.Add(TextRule);
        }

        return new SanitizedLine(
            line.Id,
            line.Index,
            line.DisplayText,
            activeText.Length == 0 ? NormalizeText(line.DisplayText) : activeText,
            disposition,
            rules.Distinct(StringComparer.Ordinal).ToArray(),
            line.WordIds,
            suppressed);
    }

    private static SanitizedLine SanitizeMaterialLine(
        ReconstructedLine line,
        double? referenceColumnLeft)
    {
        var activeParts = new List<string>();
        var suppressed = new List<SuppressedTextSegment>();
        foreach (var cell in line.Cells.OrderBy(static cell => cell.Index))
        {
            var text = NormalizeText(cell.Text);
            if (StartsWithLabel(text, "Material") ||
                referenceColumnLeft is null ||
                cell.Bounds.Left < referenceColumnLeft.Value - 2d)
            {
                activeParts.Add(text);
                continue;
            }

            suppressed.Add(new SuppressedTextSegment(
                text,
                SanitizedDisposition.Reference,
                ReferenceRule));
        }

        return new SanitizedLine(
            line.Id,
            line.Index,
            line.DisplayText,
            JoinActiveParts(activeParts),
            SanitizedDisposition.Active,
            [TextRule],
            line.WordIds,
            suppressed);
    }

    private static string JoinActiveParts(IEnumerable<string> parts)
    {
        var joined = string.Join(" | ", parts.Where(static part => part.Length > 0));
        return FieldValueSeparatorRegex().Replace(joined, ": ");
    }

    private static SanitizedLine WholeLine(
        ReconstructedLine line,
        SanitizedDisposition disposition,
        string ruleId) =>
        new(
            line.Id,
            line.Index,
            line.DisplayText,
            NormalizeText(line.DisplayText),
            disposition,
            [ruleId],
            line.WordIds,
            []);

    private static SanitizedHeader ParseHeader(ReconstructedLine[] lines, int headerEnd)
    {
        if (headerEnd < 0)
        {
            return new SanitizedHeader(
                null, null, null, null, null, null, null, null,
                null, null, null, null, null, null, [], ["Cabeçalho sem marcador Data Col."]);
        }

        var headerLines = lines.Take(headerEnd + 1).ToArray();
        var texts = headerLines.Select(static line => NormalizeText(line.Text)).ToArray();
        string? patient = null;
        string? sex = null;
        string? birthDate = null;
        string? age = null;
        string? requester = null;
        string? registration = null;
        string? requestNumber = null;
        string? requestDate = null;
        string? requestTime = null;
        string? origin = null;
        string? collectionDate = null;
        string? collectionTime = null;
        var unresolved = new List<string>();

        var requesterIndex = -1;
        var requestIndex = -1;
        var originIndex = -1;
        for (var index = 0; index < texts.Length; index++)
        {
            var text = texts[index];
            if (TryMatch(PatientRegex(), text, out var match))
            {
                patient = Value(match, "patient");
                sex = Value(match, "sex");
            }
            else if (TryMatch(BirthRegex(), text, out match))
            {
                birthDate = Value(match, "date");
                age = Value(match, "age");
            }
            else if (StartsWithLabel(text, "Solicitante"))
            {
                requesterIndex = index;
                var content = RemoveLabel(text, "Solicitante");
                SplitTrailingLabel(content, "Registro", out requester, out registration);
            }
            else if (StartsWithLabel(text, "Registro"))
            {
                registration = RemoveLabel(text, "Registro");
            }
            else if (TryMatch(RequestRegex(), text, out match))
            {
                requestIndex = index;
                requestNumber = Value(match, "number");
                requestDate = Value(match, "date");
            }
            else if (StartsWithLabel(text, "Origem"))
            {
                originIndex = index;
                var content = RemoveLabel(text, "Origem");
                SplitTrailingLabel(content, "Hora Req", out origin, out requestTime);
            }
            else if (StartsWithLabel(text, "Hora Req"))
            {
                requestTime = RemoveLabel(text, "Hora Req");
            }
            else if (TryMatch(CollectionRegex(), text, out match))
            {
                collectionDate = Value(match, "date");
                collectionTime = Value(match, "time");
            }
        }

        if (requesterIndex >= 0 && requestIndex > requesterIndex)
        {
            requester = AppendContinuations(
                requester,
                texts,
                requesterIndex + 1,
                requestIndex,
                static text => StartsWithLabel(text, "Registro"));
        }

        if (originIndex >= 0 && headerEnd > originIndex)
        {
            origin = AppendContinuations(
                origin,
                texts,
                originIndex + 1,
                headerEnd,
                static text => StartsWithLabel(text, "Hora Req"));
        }

        var recognizedIndexes = new HashSet<int> { 0, 1, 2, 3 };
        for (var index = 4; index < texts.Length; index++)
        {
            if (IsKnownHeaderLine(texts[index]) ||
                IsHeaderContinuation(index, requesterIndex, requestIndex, originIndex, headerEnd))
            {
                recognizedIndexes.Add(index);
            }
        }

        unresolved.AddRange(texts
            .Where((_, index) => !recognizedIndexes.Contains(index))
            .Where(static text => text.Length > 0));

        return new SanitizedHeader(
            texts.ElementAtOrDefault(0),
            texts.ElementAtOrDefault(1),
            patient,
            sex,
            birthDate,
            age,
            requester,
            registration,
            requestNumber,
            requestDate,
            requestTime,
            origin,
            collectionDate,
            collectionTime,
            headerLines.Select(static line => line.Id).ToArray(),
            unresolved);
    }

    private static int FindFooterStart(ReconstructedLine[] lines)
    {
        for (var index = 0; index < lines.Length; index++)
        {
            if (!NormalizeText(lines[index].Text).StartsWith(
                    "Laudo conferido e liberado eletronicamente",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var tail = lines.Skip(index).Select(static line => NormalizeText(line.Text)).ToArray();
            if (tail.Any(static text => text.StartsWith("Responsável Técnico:", StringComparison.OrdinalIgnoreCase)) &&
                tail.Any(static text => text.StartsWith("Data e hora liberação:", StringComparison.OrdinalIgnoreCase)) &&
                tail.Any(static text => text.StartsWith("Impresso por:", StringComparison.OrdinalIgnoreCase)) &&
                tail.Any(static text => text.StartsWith("Este laudo possui caráter", StringComparison.OrdinalIgnoreCase)))
            {
                return index;
            }
        }

        return -1;
    }

    public static string NormalizeText(string text)
    {
        var normalized = text
            .Replace("\uFB01", "fi", StringComparison.Ordinal)
            .Replace("\uFB02", "fl", StringComparison.Ordinal)
            .Replace('\u00A0', ' ')
            .Replace("\u00AD", string.Empty, StringComparison.Ordinal);
        normalized = WhitespaceRegex().Replace(normalized, " ").Trim();
        normalized = DottedLabelRegex().Replace(normalized, "${label}:");
        normalized = SpaceBeforeColonRegex().Replace(normalized, ":");
        return normalized;
    }

    private static bool IsStructuralBoundary(string text) =>
        IsMethodLine(text) ||
        StartsWithLabel(text, "Material") ||
        IsPreviousResultsMarker(text) ||
        ReferenceMarkerRegex().IsMatch(text) ||
        IsReferenceFragment(text) ||
        LooksLikeFieldLabel(text) ||
        LooksLikeAnyFieldLabel(text) ||
        IsMajorSectionTitle(text);

    private static bool IsReferenceFragment(string text)
    {
        var key = SpuriousNoteCatalog.ComparisonKey(text).TrimEnd(':');
        return key is "VALOR DE" or "VALORES DE" or "REFERENCIA" or "REFERENCIAS";
    }

    private static bool LooksLikeFieldLabel(string text)
    {
        var colon = text.IndexOf(':');
        if (colon is < 2 or > 90)
        {
            return false;
        }

        var prefix = text[..colon];
        if (!char.IsLetter(prefix[0]))
        {
            return false;
        }

        var letters = prefix.Where(char.IsLetter).ToArray();
        return letters.Length >= 2 && letters.All(char.IsUpper);
    }

    private static bool LooksLikeAnyFieldLabel(string text)
    {
        var colon = text.IndexOf(':');
        if (colon is < 2 or > 90)
        {
            return false;
        }

        var prefix = text[..colon];
        var letters = prefix.Where(char.IsLetter).ToArray();
        var words = prefix.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return letters.Length >= 2 &&
               char.IsLetter(prefix[0]) &&
               words.Length <= 12 &&
               !prefix.Contains('.', StringComparison.Ordinal);
    }

    private static bool IsMajorSectionTitle(string text)
    {
        if (text.Length is 0 or > 100 || text.Contains(':'))
        {
            return false;
        }

        var letters = text.Where(char.IsLetter).ToArray();
        return letters.Length >= 4 && letters.All(char.IsUpper);
    }

    private static bool IsMethodLine(string text) =>
        StartsWithLabel(text, "Método") || StartsWithLabel(text, "Metodo");

    private static bool IsPreviousResultsMarker(string text) =>
        text.StartsWith("Resultados Anteriores", StringComparison.OrdinalIgnoreCase) ||
        text.StartsWith("Resultado Anterior", StringComparison.OrdinalIgnoreCase);

    private static bool IsAnyNoteMarker(string text)
    {
        var key = SpuriousNoteCatalog.ComparisonKey(text);
        return key.StartsWith("NOTA", StringComparison.Ordinal) ||
               key.StartsWith("ATENCAO", StringComparison.Ordinal) ||
               key.StartsWith("OBSERVACAO", StringComparison.Ordinal) ||
               key.StartsWith("OBSERVACOES", StringComparison.Ordinal);
    }

    private static bool IsEmptyObservationLabel(string text)
    {
        var key = SpuriousNoteCatalog.ComparisonKey(text).Replace(".", string.Empty, StringComparison.Ordinal);
        return key is "OBSERVACAO:" or "OBSERVACOES:";
    }

    private static bool IsKnownHeaderLine(string text) =>
        StartsWithLabel(text, "Nome do Paciente") ||
        StartsWithLabel(text, "Data Nascimento") ||
        StartsWithLabel(text, "Solicitante") ||
        StartsWithLabel(text, "Registro") ||
        StartsWithLabel(text, "Nr Requisição") ||
        StartsWithLabel(text, "Origem") ||
        StartsWithLabel(text, "Hora Req") ||
        StartsWithLabel(text, "Data Col");

    private static bool IsHeaderContinuation(
        int index,
        int requesterIndex,
        int requestIndex,
        int originIndex,
        int headerEnd) =>
        (requesterIndex >= 0 && index > requesterIndex && index < requestIndex) ||
        (originIndex >= 0 && index > originIndex && index < headerEnd);

    private static string? AppendContinuations(
        string? value,
        string[] texts,
        int start,
        int endExclusive,
        Func<string, bool> ignore)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add(value);
        }

        for (var index = start; index < endExclusive; index++)
        {
            if (!ignore(texts[index]) && !IsKnownHeaderLine(texts[index]))
            {
                parts.Add(texts[index]);
            }
        }

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private static void SplitTrailingLabel(
        string content,
        string trailingLabel,
        out string? left,
        out string? right)
    {
        var marker = trailingLabel + ":";
        var index = content.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            left = EmptyToNull(content);
            right = null;
            return;
        }

        left = EmptyToNull(content[..index]);
        right = EmptyToNull(content[(index + marker.Length)..]);
    }

    private static string RemoveLabel(string text, string label)
    {
        var colon = text.IndexOf(':');
        return colon < 0 ? string.Empty : text[(colon + 1)..].Trim();
    }

    private static bool StartsWithLabel(string text, string label) =>
        text.StartsWith(label, StringComparison.OrdinalIgnoreCase);

    private static bool TryMatch(Regex regex, string text, out Match match)
    {
        match = regex.Match(text);
        return match.Success;
    }

    private static string? Value(Match match, string group) =>
        EmptyToNull(match.Groups[group].Value);

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"^Nome do Paciente\.*:\s*(?<patient>.*?)\s+Sexo\.*:\s*(?<sex>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PatientRegex();

    [GeneratedRegex(@"^Data Nascimento\.*:\s*(?<date>\S*)\s+Idade\.*:\s*(?<age>.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BirthRegex();

    [GeneratedRegex(@"^Nr Requisi[cç][aã]o\.*:\s*(?<number>\S*)\s+Data Req:\s*(?<date>.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RequestRegex();

    [GeneratedRegex(@"^Data Col\.*:\s*(?<date>\S*)\s+Hora Col:\s*(?<time>.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CollectionRegex();

    [GeneratedRegex(@"\b(?:Valores?|Valor)\s+de\s+Refer[eê]ncia\s*:?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReferenceMarkerRegex();

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"(?<label>[\p{L}\p{M}\p{N}\)])(?:\s*\.)+\s*:")]
    private static partial Regex DottedLabelRegex();

    [GeneratedRegex(@"\s+:")]
    private static partial Regex SpaceBeforeColonRegex();

    [GeneratedRegex(@":\s*\|\s*")]
    private static partial Regex FieldValueSeparatorRegex();

    [GeneratedRegex(@"^Idade:\s*\d+\s+Sexo:\s*\S+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CalculationInputRegex();
}
