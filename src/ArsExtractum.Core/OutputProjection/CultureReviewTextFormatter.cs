using System.Text;
using ArsExtractum.Core.LaboratorySemantic;

namespace ArsExtractum.Core.OutputProjection;

public static class CultureReviewTextFormatter
{
    public const string WarningText =
        "ATENÇÃO: foram detectados exames de cultura/microbiologia. A extração desses resultados pode ser incompleta devido à variação documental. Confira os resultados no documento-fonte antes de utilização clínica.";

    public static string Format(SemanticPatientBatch batch, string? patientKey = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var builder = new StringBuilder();
        foreach (var patient in batch.Patients.Where(patient => patientKey is null || patient.PatientKey == patientKey))
        {
            var occurrences = patient.Episodes.SelectMany(episode => episode.LaboratoryOccurrences
                .Where(occurrence => OutputProjector.IsCultureOccurrence(occurrence) ||
                    occurrence.ConceptId == "fsph-nh.bacterioscopico-gram")
                .Select(occurrence => (Episode: episode, Occurrence: occurrence))).ToArray();
            if (occurrences.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0) builder.AppendLine().AppendLine();
            builder.AppendLine(WarningText).AppendLine();
            builder.Append("Paciente: ").AppendLine(patient.Identity.PatientName);
            foreach (var item in occurrences.OrderByDescending(static item => EpisodeDate(item.Episode)))
            {
                builder.AppendLine()
                    .Append(item.Episode.DocumentaryEpisode.RequestDate).Append(' ')
                    .Append(item.Episode.DocumentaryEpisode.RequestTime).Append(" — ")
                    .AppendLine(item.Occurrence.DisplayName);

                var appearances = item.Occurrence.FieldEvidence.SelectMany(static evidence => evidence.SourceAppearances)
                    .DistinctBy(static source => (source.DocumentId, source.InputIndex, source.PageNumber))
                    .OrderBy(static source => source.InputIndex).ThenBy(static source => source.PageNumber).ToArray();
                builder.Append("Fontes: ").AppendLine(string.Join("; ", appearances.Select(static source =>
                    $"{source.FileName} / página {source.PageNumber}")));
                builder.AppendLine("[CONTEÚDO HIGIENIZADO]");
                foreach (var line in item.Occurrence.FieldEvidence
                             .DistinctBy(static evidence => (evidence.BlockId, evidence.CanonicalLineId)))
                {
                    builder.AppendLine(line.SanitizedText);
                }
                builder.AppendLine("[/CONTEÚDO HIGIENIZADO]");
            }
        }

        return builder.Length == 0 ? "Nenhum exame de cultura/microbiologia foi detectado." : builder.ToString().TrimEnd();
    }

    private static DateTime EpisodeDate(SemanticEpisode episode) =>
        DateTime.TryParseExact($"{episode.DocumentaryEpisode.RequestDate} {episode.DocumentaryEpisode.RequestTime}",
            "dd/MM/yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var value) ? value : DateTime.MinValue;
}
