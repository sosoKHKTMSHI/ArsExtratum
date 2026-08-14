using System.Globalization;
using System.Text;
using ArsExtractum.Core.LaboratorySemantic;

namespace ArsExtractum.Core.DerivedMeasurements;

public static class DerivedMeasurementTextFormatter
{
    public static string Format(SemanticPatientBatch batch, string? patientKey = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var builder = new StringBuilder();
        var coverage = batch.DerivedMeasurementCoverage;
        builder.Append("SEMANTIC PATIENT BATCH ENRIQUECIDO ").AppendLine(batch.SchemaVersion)
            .Append("Regras derivadas: ").AppendLine(batch.DerivedMeasurementRulesVersion)
            .Append("Cobertura: ").Append(coverage?.DerivedRecordCount ?? 0)
            .Append('/').Append(coverage?.SourceCreatinineOccurrenceCount ?? 0)
            .Append(" registros | computed: ").Append(coverage?.ComputedCount ?? 0)
            .Append(" | not-computed: ").Append(coverage?.NotComputedCount ?? 0)
            .Append(" | TFG laboratorial usada: ").Append(coverage?.LabReportedEgfrInputUseCount ?? 0)
            .AppendLine();

        foreach (var patient in batch.Patients.Where(patient =>
                     patientKey is null || patient.PatientKey == patientKey))
        {
            builder.AppendLine().Append("PACIENTE: ").AppendLine(patient.PatientKey);
            foreach (var episode in patient.Episodes.Where(static episode =>
                         episode.LaboratoryOccurrences.Any(static occurrence => occurrence.DerivedObservations.Count > 0)))
            {
                builder.Append("  EPISÓDIO ").AppendLine(episode.EpisodeKey);
                foreach (var occurrence in episode.LaboratoryOccurrences.Where(static occurrence =>
                             occurrence.DerivedObservations.Count > 0))
                {
                    builder.Append("    - ").Append(occurrence.DisplayName)
                        .Append(" [").Append(occurrence.OccurrenceId).AppendLine("]");
                    foreach (var observation in occurrence.DerivedObservations)
                    {
                        builder.Append("      ");
                        if (observation.Status == DerivedObservationStatus.Computed)
                        {
                            builder.Append("TFG CKD-EPI 2021: ")
                                .Append(observation.NumericValue!.Value.ToString("R", CultureInfo.InvariantCulture))
                                .Append(' ').AppendLine(observation.Unit);
                        }
                        else
                        {
                            builder.Append("TFG CKD-EPI 2021 — NOT COMPUTED: ")
                                .AppendLine(observation.ReasonCode?.ToString());
                        }
                    }
                }
            }
        }

        return builder.ToString();
    }
}
