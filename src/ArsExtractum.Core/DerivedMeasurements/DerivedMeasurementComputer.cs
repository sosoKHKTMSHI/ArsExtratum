using System.Security.Cryptography;
using System.Text;
using ArsExtractum.Core.LaboratorySemantic;
using ArsExtractum.Core.Pipeline;

namespace ArsExtractum.Core.DerivedMeasurements;

public sealed class DerivedMeasurementComputer
{
    public const string CurrentRulesVersion = "ckd-epi-2021-creatinine-rules/1.0";
    public const string DerivedConceptId = "ars-extractum.egfr.ckd-epi-2021.creatinine";
    public const string MethodId = "CKD-EPI-2021-Creatinine-Race-Free";
    public const string OutputUnit = "mL/min/1.73 m²";
    public const string RequiredCatalogVersion = "1.0.0";
    private const string SerumCreatinineConceptId = "fsph-nh.creatinina";
    private const string LaboratoryReportedEgfrLabel = "TAXA DE FILTRAÇÃO GLOMERULAR ESTIMADA";

    public static StageDescriptor Descriptor { get; } = new(
        StageIds.DerivedMeasurementComputation,
        "Cálculo derivado CKD-EPI 2021",
        "Calcula TFG CKD-EPI 2021 para creatinina sérica adulta e registra falhas explicitamente.",
        LaboratorySemanticExtractor.CurrentSchemaVersion,
        [StageIds.LaboratorySemanticExtraction]);

    public static SemanticPatientBatch Enrich(DerivedMeasurementComputationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var semantic = input.SemanticPatientBatch;
        if (semantic.SchemaVersion != LaboratorySemanticExtractor.CurrentSchemaVersion ||
            semantic.ExtractionRulesVersion != LaboratorySemanticExtractor.CurrentRulesVersion ||
            semantic.CatalogVersion != RequiredCatalogVersion)
        {
            throw new InvalidOperationException(
                "Derived Measurement Computation v1 exige SemanticPatientBatch 1.1, regras semânticas v1 e catálogo 1.0.0.");
        }

        var patients = semantic.Patients.Select(EnrichPatient).ToArray();
        var records = patients.SelectMany(static patient => patient.Episodes)
            .SelectMany(static episode => episode.LaboratoryOccurrences)
            .SelectMany(static occurrence => occurrence.DerivedObservations).ToArray();
        var sourceOccurrenceIds = semantic.Patients.SelectMany(static patient => patient.Episodes)
            .SelectMany(static episode => episode.LaboratoryOccurrences)
            .Where(static occurrence => occurrence.ConceptId == SerumCreatinineConceptId)
            .Select(static occurrence => occurrence.OccurrenceId)
            .ToArray();
        var sourceSet = sourceOccurrenceIds.ToHashSet(StringComparer.Ordinal);
        var reasonCounts = Enum.GetValues<DerivedMeasurementReasonCode>()
            .ToDictionary(
                static reason => reason.ToString(),
                reason => records.Count(record => record.ReasonCode == reason),
                StringComparer.Ordinal);
        var coverage = new DerivedMeasurementCoverage(
            sourceOccurrenceIds.Length,
            records.Length,
            records.Count(static record => record.Status == DerivedObservationStatus.Computed),
            records.Count(static record => record.Status == DerivedObservationStatus.NotComputed),
            reasonCounts,
            records.Count(record => !sourceSet.Contains(record.SourceOccurrenceId)),
            records.GroupBy(static record => record.SourceOccurrenceId, StringComparer.Ordinal)
                .Count(static group => group.Count() > 1),
            0);
        var notices = coverage.IsComplete
            ? Array.Empty<DerivedMeasurementNotice>()
            :
            [
                new DerivedMeasurementNotice(
                    "derived.coverage-not-complete",
                    "A cobertura dos cálculos derivados não reconciliou as ocorrências-fonte de creatinina sérica."),
            ];

        return semantic with
        {
            Patients = patients,
            DerivedMeasurementRulesVersion = CurrentRulesVersion,
            DerivedMeasurementCoverage = coverage,
            DerivedMeasurementNotices = notices,
        };
    }

    private static SemanticPatient EnrichPatient(SemanticPatient patient) => patient with
    {
        Episodes = patient.Episodes.Select(episode => episode with
        {
            LaboratoryOccurrences = episode.LaboratoryOccurrences.Select(occurrence => occurrence with
            {
                DerivedObservations = occurrence.ConceptId == SerumCreatinineConceptId
                    ? [ComputeOccurrence(patient, episode, occurrence)]
                    : [],
            }).ToArray(),
        }).ToArray(),
    };

    private static DerivedObservation ComputeOccurrence(
        SemanticPatient patient,
        SemanticEpisode episode,
        LaboratoryOccurrence occurrence)
    {
        var candidates = occurrence.Observations.Where(static observation =>
            !string.Equals(observation.Label, LaboratoryReportedEgfrLabel, StringComparison.Ordinal)).ToArray();
        var candidate = candidates.Length == 1 ? candidates[0] : null;
        var specimen = occurrence.Specimens.Count == 1 ? occurrence.Specimens[0] : null;
        var normalizedSex = NormalizeSex(patient.Identity.Sex);
        var inputs = new DerivedMeasurementInputs(
            patient.Identity.BirthDate,
            episode.DocumentaryEpisode.RequestDate,
            episode.DocumentaryEpisode.AgeAtRequest.CompletedYears,
            patient.Identity.Sex,
            normalizedSex,
            candidate?.RawValue,
            candidate?.NumericValue,
            candidate?.RawUnit,
            NormalizeCreatinineUnit(candidate?.NormalizedUnit),
            specimen?.RawSpecimen);
        var provenance = new DerivedMeasurementProvenance(
            patient.PatientKey,
            episode.EpisodeKey,
            occurrence.OccurrenceId,
            candidates.Select(static item => item.ObservationId).ToArray(),
            candidates.Select(static item => item.Evidence).ToArray(),
            occurrence.Specimens.Select(static item => item.Evidence).ToArray(),
            episode.DocumentaryEpisode.Pages.Select(static page => new DerivedHeaderEvidence(
                page.DocumentId,
                page.FileName,
                page.InputIndex,
                page.PageNumber,
                page.Header.SourceLineIds)).ToArray());
        var reason = ResolveReason(patient, episode, occurrence, candidates, candidate, specimen);
        double? numericValue = null;
        if (reason is null)
        {
            numericValue = CalculateCkdEpi2021(
                (double)candidate!.NumericValue!.Value,
                episode.DocumentaryEpisode.AgeAtRequest.CompletedYears!.Value,
                normalizedSex!);
            if (!double.IsFinite(numericValue.Value) || numericValue.Value <= 0d)
            {
                numericValue = null;
                reason = DerivedMeasurementReasonCode.ComputationNonFinite;
            }
        }

        var status = reason is null
            ? DerivedObservationStatus.Computed
            : DerivedObservationStatus.NotComputed;
        var sourceObservationId = candidate?.ObservationId;
        return new DerivedObservation(
            StableId(
                "derived-observation",
                CurrentRulesVersion,
                patient.PatientKey,
                episode.EpisodeKey,
                occurrence.OccurrenceId,
                sourceObservationId ?? string.Empty),
            patient.PatientKey,
            episode.EpisodeKey,
            occurrence.OccurrenceId,
            sourceObservationId,
            DerivedConceptId,
            DerivedObservationKind.Derived,
            MethodId,
            status,
            numericValue,
            status == DerivedObservationStatus.Computed ? OutputUnit : null,
            reason,
            inputs,
            provenance,
            [CurrentRulesVersion]);
    }

    private static DerivedMeasurementReasonCode? ResolveReason(
        SemanticPatient patient,
        SemanticEpisode episode,
        LaboratoryOccurrence occurrence,
        LaboratoryObservation[] candidates,
        LaboratoryObservation? candidate,
        LaboratorySpecimen? specimen)
    {
        if (!string.Equals(occurrence.EpisodeKey, episode.EpisodeKey, StringComparison.Ordinal))
        {
            return DerivedMeasurementReasonCode.UnsafeAssociation;
        }

        var age = episode.DocumentaryEpisode.AgeAtRequest;
        if (!string.Equals(age.Status, "Computed", StringComparison.Ordinal) ||
            age.CompletedYears is null or < 0)
        {
            return DerivedMeasurementReasonCode.AgeAtRequestUnavailable;
        }

        if (age.CompletedYears < 18)
        {
            return DerivedMeasurementReasonCode.AgeBelow18;
        }

        if (string.IsNullOrWhiteSpace(patient.Identity.Sex))
        {
            return DerivedMeasurementReasonCode.SexUnavailable;
        }

        if (NormalizeSex(patient.Identity.Sex) is null)
        {
            return DerivedMeasurementReasonCode.SexUnsupported;
        }

        if (specimen is null ||
            !string.Equals(specimen.RawSpecimen.Trim(), "SORO", StringComparison.OrdinalIgnoreCase))
        {
            return DerivedMeasurementReasonCode.SerumSpecimenNotConfirmed;
        }

        if (candidates.Length == 0)
        {
            return DerivedMeasurementReasonCode.CreatinineObservationMissing;
        }

        if (candidates.Length > 1)
        {
            return DerivedMeasurementReasonCode.CreatinineObservationAmbiguous;
        }

        if (candidate!.NumericValue is null)
        {
            return DerivedMeasurementReasonCode.CreatinineValueNotNumeric;
        }

        if (candidate.NumericValue <= 0m)
        {
            return DerivedMeasurementReasonCode.CreatinineValueNotPositive;
        }

        if (string.IsNullOrWhiteSpace(candidate.NormalizedUnit))
        {
            return DerivedMeasurementReasonCode.CreatinineUnitMissing;
        }

        if (NormalizeCreatinineUnit(candidate.NormalizedUnit) is null)
        {
            return DerivedMeasurementReasonCode.CreatinineUnitUnsupported;
        }

        return null;
    }

    private static string? NormalizeSex(string? rawSex)
    {
        if (string.IsNullOrWhiteSpace(rawSex))
        {
            return null;
        }

        return rawSex.Trim() switch
        {
            var value when value.Equals("Feminino", StringComparison.OrdinalIgnoreCase) => "female",
            var value when value.Equals("Masculino", StringComparison.OrdinalIgnoreCase) => "male",
            _ => null,
        };
    }

    private static string? NormalizeCreatinineUnit(string? rawUnit) =>
        !string.IsNullOrWhiteSpace(rawUnit) &&
        rawUnit.Trim().Equals("mg/dl", StringComparison.OrdinalIgnoreCase)
            ? "mg/dL"
            : null;

    private static double CalculateCkdEpi2021(double serumCreatinine, int age, string normalizedSex)
    {
        var female = normalizedSex == "female";
        var kappa = female ? 0.7d : 0.9d;
        var alpha = female ? -0.241d : -0.302d;
        var sexFactor = female ? 1.012d : 1d;
        var ratio = serumCreatinine / kappa;

        return 142d *
               Math.Pow(Math.Min(ratio, 1d), alpha) *
               Math.Pow(Math.Max(ratio, 1d), -1.200d) *
               Math.Pow(0.9938d, age) *
               sexFactor;
    }

    private static string StableId(string prefix, params string[] parts)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f', parts)));
        return $"{prefix}-{Convert.ToHexString(bytes).ToLowerInvariant()[..16]}";
    }
}
