using System.Globalization;
using System.Text;

namespace ArsExtractum.Core.Assembly;

public static class PatientAssemblyTextFormatter
{
    public static string Format(PatientBatch batch, string? selectedPatientKey = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var patients = selectedPatientKey is null
            ? batch.Patients
            : batch.Patients.Where(patient =>
                string.Equals(patient.PatientKey, selectedPatientKey, StringComparison.Ordinal)).ToArray();
        var builder = new StringBuilder();
        builder.AppendLine("PACIENTES E EPISÓDIOS");
        builder.Append("Pacientes: ").Append(batch.Patients.Count)
            .Append(" | Episódios: ").Append(batch.EpisodeCount)
            .Append(" | Documentos não associados: ").AppendLine(
                batch.UnassignedDocuments.Count.ToString(CultureInfo.InvariantCulture));

        var failedCount = batch.Ledger.Count(static entry => entry.Disposition == "Failed");
        if (failedCount > 0)
        {
            builder.Append("Documentos com falha: ")
                .AppendLine(failedCount.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var patient in patients)
        {
            AppendPatient(builder, patient);
        }

        if (selectedPatientKey is null && batch.UnassignedDocuments.Count > 0)
        {
            builder.AppendLine().AppendLine("=== DOCUMENTOS NÃO ASSOCIADOS ===");
            foreach (var document in batch.UnassignedDocuments)
            {
                builder.Append(document.FileName).Append(": ").AppendLine(document.Reason);
            }
        }

        if (selectedPatientKey is null && batch.Ledger.Any(static entry =>
                entry.Disposition is "Failed" or "Rejected" or "AssignedWithUnassignedPages"))
        {
            builder.AppendLine().AppendLine("=== LEDGER DE MONTAGEM ===");
            foreach (var entry in batch.Ledger.Where(static entry =>
                         entry.Disposition is "Failed" or "Rejected" or "AssignedWithUnassignedPages"))
            {
                builder.Append(entry.FileName)
                    .Append(" | ").Append(entry.Disposition)
                    .Append(" | pÃ¡ginas origem: ")
                    .Append(entry.SourcePageCount?.ToString(CultureInfo.InvariantCulture) ?? "desconhecido")
                    .Append(" | atribuÃ­das: ").Append(entry.AssignedPageCount)
                    .Append(" | nÃ£o atribuÃ­das: ").Append(entry.UnassignedPageCount);
                if (!string.IsNullOrWhiteSpace(entry.Reason))
                {
                    builder.Append(" | ").Append(entry.Reason);
                }

                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private static void AppendPatient(StringBuilder builder, AssembledPatient patient)
    {
        builder.AppendLine()
            .Append("==================== PACIENTE: ")
            .Append(patient.Identity.PatientName)
            .AppendLine(" ====================");
        builder.Append("Nascimento: ").Append(patient.Identity.BirthDate)
            .Append(" | Sexo: ").AppendLine(patient.Identity.Sex ?? "[não identificado]");
        builder.Append("PDFs: ").AppendLine(string.Join(
            " | ",
            patient.SourceDocuments.Select(static document => document.FileName)));

        foreach (var episode in patient.Episodes)
        {
            builder.AppendLine()
                .Append("--- EPISÓDIO: ").Append(episode.RequestDate)
                .Append(' ').Append(episode.RequestTime)
                .Append(" | Requisição: ").Append(episode.RequestNumber)
                .AppendLine(" ---");
            builder.Append("Idade na requisição: ")
                .AppendLine(episode.AgeAtRequest.CompletedYears is { } age
                    ? $"{age} ano(s)"
                    : $"[não calculada: {episode.AgeAtRequest.Reason}]");
            if (episode.Origins.Count > 0)
            {
                builder.Append("Origem: ").AppendLine(string.Join(" | ", episode.Origins));
            }

            builder.AppendLine("[CONTEÚDO]");
            foreach (var block in episode.ContentBlocks)
            {
                if (block.Sources.Count == 1)
                {
                    var source = block.Sources[0];
                    builder.Append("--- ARQUIVO: ").Append(source.FileName)
                        .Append(" | PÁGINA ").Append(source.PageNumber.ToString("D4", CultureInfo.InvariantCulture))
                        .AppendLine(" ---");
                }
                else
                {
                    builder.AppendLine("--- CONTEÚDO DOCUMENTAL REPETIDO ---");
                    builder.AppendLine("Origens equivalentes:");
                    foreach (var source in block.Sources)
                    {
                        builder.Append("- ").Append(source.FileName)
                            .Append(" | página ").AppendLine(source.PageNumber.ToString("D4", CultureInfo.InvariantCulture));
                    }
                }

                foreach (var line in block.ActiveLines)
                {
                    builder.AppendLine(line.Text);
                }
            }

            builder.AppendLine("[/CONTEÚDO]");
        }

        if (patient.UnassignedPages.Count > 0)
        {
            builder.AppendLine().AppendLine("--- PÁGINAS SEM EPISÓDIO COMPLETO ---");
            foreach (var page in patient.UnassignedPages)
            {
                builder.Append(page.FileName).Append(" | página ")
                    .AppendLine(page.PageNumber.ToString(CultureInfo.InvariantCulture));
            }
        }
    }
}
