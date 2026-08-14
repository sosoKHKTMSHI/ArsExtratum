using System.Globalization;
using System.Text;
using ArsExtractum.Core.DerivedMeasurements;

namespace ArsExtractum.Core.LaboratorySemantic;

public static class LaboratorySemanticTextFormatter
{
    public static string Format(SemanticPatientBatch batch, string? patientKey = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var builder = new StringBuilder();
        builder.Append("SEMANTIC PATIENT BATCH ").AppendLine(batch.SchemaVersion)
            .Append("Catálogo: ").Append(batch.CatalogVersion)
            .Append(" | Regras: ").AppendLine(batch.ExtractionRulesVersion)
            .Append("Cobertura: ").Append(batch.Coverage.OwnedActiveLineCount)
            .Append('/').Append(batch.Coverage.CanonicalActiveLineCount)
            .Append(" linhas | unsupported: ").Append(batch.Coverage.UnsupportedActiveLineCount)
            .Append(" | multiply-owned: ").Append(batch.Coverage.MultiplyOwnedActiveLineCount)
            .AppendLine();

        foreach (var patient in batch.Patients.Where(patient =>
                     patientKey is null || patient.PatientKey == patientKey))
        {
            builder.AppendLine().Append("PACIENTE: ").Append(patient.Identity.PatientName)
                .Append(" [").Append(patient.PatientKey).AppendLine("]");
            foreach (var episode in patient.Episodes)
            {
                builder.Append("  EPISÓDIO ").Append(episode.EpisodeKey)
                    .Append(" | ").Append(episode.DocumentaryEpisode.RequestDate)
                    .Append(' ').Append(episode.DocumentaryEpisode.RequestTime)
                    .Append(" | requisição ").AppendLine(episode.DocumentaryEpisode.RequestNumber);
                foreach (var occurrence in episode.LaboratoryOccurrences)
                {
                    builder.Append("    - ").Append(occurrence.DisplayName)
                        .Append(" [").Append(occurrence.StructuralForm).Append("] ")
                        .AppendLine(occurrence.OccurrenceId);
                    foreach (var observation in occurrence.Observations)
                    {
                        builder.Append("      ").Append(observation.Label).Append(": ")
                            .Append(observation.RawValue);
                        if (!string.IsNullOrWhiteSpace(observation.RawUnit))
                        {
                            builder.Append(' ').Append(observation.RawUnit);
                        }

                        builder.AppendLine();
                    }

                    foreach (var derived in occurrence.DerivedObservations)
                    {
                        builder.Append("      TFG CKD-EPI 2021: ");
                        if (derived.Status == DerivedObservationStatus.Computed)
                        {
                            builder.Append(derived.NumericValue!.Value.ToString("R", CultureInfo.InvariantCulture))
                                .Append(' ').AppendLine(derived.Unit);
                        }
                        else
                        {
                            builder.Append("NOT COMPUTED — ").AppendLine(derived.ReasonCode?.ToString());
                        }
                    }

                    foreach (var specimen in occurrence.Specimens)
                    {
                        builder.Append("      Material: ").AppendLine(specimen.RawSpecimen);
                    }

                    if (occurrence.Microbiology is { } microbiology)
                    {
                        foreach (var group in microbiology.SusceptibilityGroups)
                        {
                            builder.Append("      Organismo: ").AppendLine(group.RawOrganismName ?? "[não determinado]");
                            foreach (var entry in group.Entries)
                            {
                                builder.Append("        ").Append(entry.RawAntimicrobial)
                                    .Append(": ").AppendLine(entry.RawResult);
                            }
                        }
                    }

                    var sources = occurrence.SourceSegments.SelectMany(static item => item.SourceAppearances)
                        .Select(static source => $"{source.FileName}/p{source.PageNumber.ToString(CultureInfo.InvariantCulture)}")
                        .Distinct(StringComparer.Ordinal).ToArray();
                    builder.Append("      Fontes: ").AppendLine(string.Join("; ", sources));
                }
            }
        }

        return builder.ToString();
    }
}
