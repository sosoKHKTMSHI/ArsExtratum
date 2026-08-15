using System.Text;

namespace ArsExtractum.Core.OutputProjection;

public static class ClinicalOutputTextFormatter
{
    public static string Format(ClinicalOutputBatch batch, string? patientKey = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var builder = new StringBuilder();
        foreach (var patient in batch.Patients.Where(patient =>
                     patientKey is null || patient.PatientKey == patientKey))
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append("Paciente: ").AppendLine(patient.DisplayIdentity.PatientName);
            foreach (var episode in patient.Episodes)
            {
                builder.AppendLine(episode.EditableClinicalText).AppendLine();
            }
        }

        return builder.ToString().TrimEnd();
    }

    internal static string FormatEpisode(
        string requestDate,
        string requestTime,
        IReadOnlyList<ClinicalProjectedOccurrence> occurrences)
    {
        var header = $"Laboratoriais ({requestDate} – {NormalizeTime(requestTime)}):";
        var scalarLines = occurrences.Where(static occurrence =>
                occurrence.Disposition is ProjectionDisposition.Projected or ProjectionDisposition.SafeFallback &&
                occurrence.Lines.Count == 1 && !IsOwnLine(occurrence.ConceptId))
            .SelectMany(static occurrence => occurrence.Lines).ToArray();
        var ownLines = occurrences.Where(static occurrence =>
                occurrence.Disposition is ProjectionDisposition.Projected or ProjectionDisposition.SafeFallback &&
                (occurrence.Lines.Count != 1 || IsOwnLine(occurrence.ConceptId)))
            .SelectMany(static occurrence => occurrence.Lines).ToArray();
        var hasHiddenCultures = occurrences.Any(static occurrence =>
            occurrence.Disposition == ProjectionDisposition.SuppressedByExplicitPolicy &&
            IsCulturalConcept(occurrence.ConceptId));
        var builder = new StringBuilder(header);
        if (scalarLines.Length > 0)
        {
            builder.Append(' ').Append(string.Join(" | ", scalarLines));
        }
        else if (ownLines.Length == 0)
        {
            builder.Append(hasHiddenCultures
                ? " Culturais"
                : " Sem resultados laboratoriais projetáveis.");
        }

        foreach (var line in ownLines)
        {
            builder.AppendLine().Append(line);
        }

        return builder.ToString();
    }

    private static bool IsOwnLine(string conceptId) => conceptId is
        "fsph-nh.exame-qualitativo-de-urina-equ" or
        "fsph-nh.gasometria-arterial" or
        "fsph-nh.gasometria-venosa" ||
        conceptId.Contains("cultura", StringComparison.Ordinal) ||
        conceptId.Contains("antibiograma", StringComparison.Ordinal) ||
        conceptId == "fsph-nh.cultural";

    private static bool IsCulturalConcept(string conceptId) =>
        conceptId.Contains("cultura", StringComparison.Ordinal) ||
        conceptId.Contains("hemocultura", StringComparison.Ordinal) ||
        conceptId.Contains("urocultura", StringComparison.Ordinal) ||
        conceptId.Contains("antibiograma", StringComparison.Ordinal) ||
        conceptId == "fsph-nh.cultural";

    private static string NormalizeTime(string value) =>
        value.Length >= 5 ? value[..5] : value;
}
