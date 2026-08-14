using System.Globalization;
using System.Text;
using ArsExtractum.Core.Documents;

namespace ArsExtractum.Core.Sanitization;

public static class SanitizationTextFormatter
{
    public static string Format(SanitizedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var builder = new StringBuilder();
        builder.AppendLine("TEXTO HIGIENIZADO");
        builder.Append("Arquivo: ").AppendLine(document.FileName);
        builder.Append("Páginas: ").Append(document.Pages.Count)
            .Append(" | Linhas ativas: ").Append(document.ActiveLineCount)
            .Append(" | Linhas retiradas do corpo: ").Append(document.SuppressedLineCount)
            .Append(" | Fragmentos de cabeçalho não resolvidos: ")
            .AppendLine(document.UnresolvedHeaderFragmentCount.ToString(CultureInfo.InvariantCulture));

        foreach (var page in document.Pages)
        {
            builder.AppendLine();
            builder.Append("--- PÁGINA ")
                .Append(page.PageNumber.ToString("D4", CultureInfo.InvariantCulture))
                .AppendLine(" ---");
            AppendHeader(builder, page.Header);
            builder.AppendLine("[CONTEÚDO]");
            var activeLines = page.Lines
                .Where(static line => line.Disposition == SanitizedDisposition.Active)
                .OrderBy(static line => line.SourceIndex)
                .ToArray();
            if (activeLines.Length == 0)
            {
                builder.AppendLine("[sem conteúdo laboratorial ativo]");
            }
            else
            {
                foreach (var line in activeLines)
                {
                    builder.AppendLine(line.Text);
                }
            }

            builder.AppendLine("[/CONTEÚDO]");
        }

        return builder.ToString();
    }

    private static void AppendHeader(StringBuilder builder, SanitizedHeader header)
    {
        builder.AppendLine("[CABEÇALHO]");
        Append(builder, "Paciente", header.PatientName);
        Append(builder, "Sexo", header.Sex);
        Append(builder, "Nascimento", header.BirthDate);
        Append(builder, "Idade informada", header.ReportedAge);
        Append(builder, "Solicitante", header.Requester);
        Append(builder, "Registro", header.RequesterRegistration);
        Append(builder, "Requisição", header.RequestNumber);
        AppendPair(builder, "Data/Hora da requisição", header.RequestDate, header.RequestTime);
        Append(builder, "Origem", header.Origin);
        AppendPair(builder, "Data/Hora da coleta", header.CollectionDate, header.CollectionTime);
        if (header.UnresolvedFragments.Count > 0)
        {
            builder.Append("Cabeçalho não resolvido: ")
                .AppendLine(string.Join(" | ", header.UnresolvedFragments));
        }

        builder.AppendLine("[/CABEÇALHO]");
    }

    private static void Append(StringBuilder builder, string label, string? value) =>
        builder.Append(label).Append(": ").AppendLine(value ?? "[não identificado]");

    private static void AppendPair(
        StringBuilder builder,
        string label,
        string? first,
        string? second)
    {
        var value = string.Join(
            " ",
            new[] { first, second }.Where(static part => !string.IsNullOrWhiteSpace(part)));
        Append(builder, label, value.Length == 0 ? null : value);
    }
}
