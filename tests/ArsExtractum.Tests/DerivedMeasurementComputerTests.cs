using System.Text.Json;
using ArsExtractum.Core.Assembly;
using ArsExtractum.Core.DerivedMeasurements;
using ArsExtractum.Core.Documents;
using ArsExtractum.Core.LaboratorySemantic;
using Xunit;

namespace ArsExtractum.Tests;

public sealed class DerivedMeasurementComputerTests
{
    [Theory]
    [InlineData("Feminino", 50, 0.7, 105.297601149144)]
    [InlineData("Feminino", 50, 1.4, 45.833442997059)]
    [InlineData("Masculino", 50, 0.9, 104.049012993225)]
    [InlineData("Masculino", 50, 1.8, 45.289963435829)]
    [InlineData("Feminino", 78, 2.6, 18.3206085451006)]
    public void PublishedFormulaVectorsKeepFullPrecision(
        string sex,
        int age,
        double creatinine,
        double expected)
    {
        var result = Compute(Batch(sex: sex, age: age, numericValue: (decimal)creatinine));
        var observation = SingleObservation(result);

        Assert.Equal(DerivedObservationStatus.Computed, observation.Status);
        Assert.Null(observation.ReasonCode);
        Assert.NotNull(observation.NumericValue);
        Assert.InRange(Math.Abs(observation.NumericValue.Value - expected), 0d, 1e-10);
        Assert.Equal(DerivedMeasurementComputer.OutputUnit, observation.Unit);
    }

    [Fact]
    public void AgeBoundaryAndReportedAgePolicyAreEnforced()
    {
        var adult = SingleObservation(Compute(Batch(age: 18, reportedAge: "2")));
        var minor = SingleObservation(Compute(Batch(age: 17, reportedAge: "99")));
        var unavailable = SingleObservation(Compute(Batch(age: null, ageStatus: "NotComputed")));

        Assert.Equal(DerivedObservationStatus.Computed, adult.Status);
        Assert.Equal(DerivedMeasurementReasonCode.AgeBelow18, minor.ReasonCode);
        Assert.Equal(DerivedMeasurementReasonCode.AgeAtRequestUnavailable, unavailable.ReasonCode);
    }

    [Theory]
    [InlineData(null, DerivedMeasurementReasonCode.SexUnavailable)]
    [InlineData("", DerivedMeasurementReasonCode.SexUnavailable)]
    [InlineData("Desconhecido", DerivedMeasurementReasonCode.SexUnsupported)]
    public void SexIsNeverInferred(string? sex, DerivedMeasurementReasonCode expected)
    {
        var observation = SingleObservation(Compute(Batch(sex: sex)));

        Assert.Equal(DerivedObservationStatus.NotComputed, observation.Status);
        Assert.Equal(expected, observation.ReasonCode);
    }

    [Theory]
    [InlineData(null, DerivedMeasurementReasonCode.CreatinineValueNotNumeric)]
    [InlineData(0d, DerivedMeasurementReasonCode.CreatinineValueNotPositive)]
    [InlineData(-1d, DerivedMeasurementReasonCode.CreatinineValueNotPositive)]
    public void InvalidCreatinineValuesAreExplicit(double? numericValue, DerivedMeasurementReasonCode expected)
    {
        var value = numericValue is null ? null : (decimal?)numericValue.Value;
        var observation = SingleObservation(Compute(Batch(numericValue: value)));

        Assert.Equal(DerivedObservationStatus.NotComputed, observation.Status);
        Assert.Equal(expected, observation.ReasonCode);
        Assert.Null(observation.NumericValue);
    }

    [Theory]
    [InlineData(null, DerivedMeasurementReasonCode.CreatinineUnitMissing)]
    [InlineData("µmol/L", DerivedMeasurementReasonCode.CreatinineUnitUnsupported)]
    public void OnlyObservedSerumCreatinineUnitIsAccepted(string? unit, DerivedMeasurementReasonCode expected)
    {
        var observation = SingleObservation(Compute(Batch(unit: unit)));

        Assert.Equal(expected, observation.ReasonCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("URINA")]
    public void SerumSpecimenMustBeExplicit(string? specimen)
    {
        var observation = SingleObservation(Compute(Batch(specimen: specimen)));

        Assert.Equal(DerivedMeasurementReasonCode.SerumSpecimenNotConfirmed, observation.ReasonCode);
    }

    [Fact]
    public void MissingAndAmbiguousPrincipalObservationsAreNotGuessed()
    {
        var missing = SingleObservation(Compute(Batch(principalObservationCount: 0)));
        var ambiguous = SingleObservation(Compute(Batch(principalObservationCount: 2)));

        Assert.Equal(DerivedMeasurementReasonCode.CreatinineObservationMissing, missing.ReasonCode);
        Assert.Equal(DerivedMeasurementReasonCode.CreatinineObservationAmbiguous, ambiguous.ReasonCode);
        Assert.Null(missing.SourceObservationId);
        Assert.Null(ambiguous.SourceObservationId);
    }

    [Fact]
    public void LaboratoryReportedEgfrDoesNotAffectCalculationAndRemainsUnused()
    {
        var withoutReported = Compute(Batch(includeLaboratoryEgfr: false));
        var lowReported = Compute(Batch(includeLaboratoryEgfr: true, laboratoryEgfr: 1m));
        var highReported = Compute(Batch(includeLaboratoryEgfr: true, laboratoryEgfr: 999m));

        Assert.Equal(
            SingleObservation(withoutReported).NumericValue,
            SingleObservation(lowReported).NumericValue);
        Assert.Equal(
            SingleObservation(withoutReported).NumericValue,
            SingleObservation(highReported).NumericValue);
        Assert.Equal(0, lowReported.DerivedMeasurementCoverage!.LabReportedEgfrInputUseCount);
        Assert.Equal(0, highReported.DerivedMeasurementCoverage!.LabReportedEgfrInputUseCount);
    }

    [Fact]
    public void UrineCreatinineDoesNotCreateDerivedRecord()
    {
        var result = Compute(Batch(conceptId: "fsph-nh.creatinina-na-urina"));

        Assert.Equal(0, result.DerivedMeasurementCoverage!.SourceCreatinineOccurrenceCount);
        Assert.Equal(0, result.DerivedMeasurementCoverage.DerivedRecordCount);
        Assert.Empty(result.Patients[0].Episodes[0].LaboratoryOccurrences[0].DerivedObservations);
        Assert.True(result.DerivedMeasurementCoverage.IsComplete);
    }

    [Fact]
    public void AssociationFailureHasHighestReasonPrecedence()
    {
        var result = Compute(Batch(occurrenceEpisodeKey: "episode-other", age: 10, sex: null));

        Assert.Equal(
            DerivedMeasurementReasonCode.UnsafeAssociation,
            SingleObservation(result).ReasonCode);
    }

    [Fact]
    public void IncompatibleSemanticCatalogIsRejected()
    {
        var source = Batch() with { CatalogVersion = "2.0.0" };

        var exception = Assert.Throws<InvalidOperationException>(() => Compute(source));
        Assert.Contains("catálogo 1.0.0", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProvenanceCoverageIdsAndSerializationAreDeterministic()
    {
        var source = Batch();
        var first = Compute(source);
        var second = Compute(source);
        var observation = SingleObservation(first);

        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(first), JsonSerializer.SerializeToUtf8Bytes(second));
        Assert.StartsWith("derived-observation-", observation.DerivedObservationId, StringComparison.Ordinal);
        Assert.Equal("patient-test", observation.Provenance.PatientKey);
        Assert.Equal("episode-test", observation.Provenance.EpisodeKey);
        Assert.Equal("occurrence-test", observation.Provenance.SourceOccurrenceId);
        Assert.Single(observation.Provenance.CandidateObservationEvidence);
        Assert.Single(observation.Provenance.SpecimenEvidence);
        Assert.Single(observation.Provenance.HeaderEvidence);
        Assert.Equal(["p0001-l0000"], observation.Provenance.HeaderEvidence[0].SourceLineIds);
        Assert.Equal(1, first.DerivedMeasurementCoverage!.SourceCreatinineOccurrenceCount);
        Assert.Equal(1, first.DerivedMeasurementCoverage.DerivedRecordCount);
        Assert.Equal(1, first.DerivedMeasurementCoverage.ComputedCount);
        Assert.Equal(0, first.DerivedMeasurementCoverage.NotComputedCount);
        Assert.True(first.DerivedMeasurementCoverage.IsComplete);
        var text = LaboratorySemanticTextFormatter.Format(first);
        Assert.Contains("CREATININA", text, StringComparison.Ordinal);
        Assert.Contains("TFG CKD-EPI 2021:", text, StringComparison.Ordinal);
    }

    private static SemanticPatientBatch Compute(SemanticPatientBatch batch) =>
        DerivedMeasurementComputer.Enrich(new DerivedMeasurementComputationInput(batch));

    private static DerivedObservation SingleObservation(SemanticPatientBatch batch) =>
        Assert.Single(Assert.Single(Assert.Single(Assert.Single(batch.Patients).Episodes)
            .LaboratoryOccurrences).DerivedObservations);

    private static SemanticPatientBatch Batch(
        string? sex = "Feminino",
        int? age = 50,
        string ageStatus = "Computed",
        string reportedAge = "50",
        string? specimen = "SORO",
        decimal? numericValue = 1m,
        string? unit = "mg/dl",
        int principalObservationCount = 1,
        bool includeLaboratoryEgfr = true,
        decimal laboratoryEgfr = 90m,
        string conceptId = "fsph-nh.creatinina",
        string occurrenceEpisodeKey = "episode-test")
    {
        var evidence = new SemanticFieldEvidence(
            "observations[0]",
            "block-test",
            "p0001-l0010",
            "CREATININA: 1,00 mg/dl",
            "CREATININA: 1,00 mg/dl",
            ["p0001-w000010"],
            ["test.fixture"],
            [],
            [new SemanticSourceAppearance("doc-test", "test.pdf", 0, 1, "p0001-l0010", ["p0001-w000010"])]);
        var observations = Enumerable.Range(0, principalObservationCount).Select(index => new LaboratoryObservation(
            $"observation-{index}",
            "CREATININA",
            numericValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "INVÁLIDO",
            numericValue,
            numericValue is null ? "INVÁLIDO" : null,
            unit,
            unit,
            evidence with { FieldPath = $"observations[{index}]" })).ToList();
        if (includeLaboratoryEgfr)
        {
            observations.Add(new LaboratoryObservation(
                "observation-lab-egfr",
                "TAXA DE FILTRAÇÃO GLOMERULAR ESTIMADA",
                laboratoryEgfr.ToString(System.Globalization.CultureInfo.InvariantCulture),
                laboratoryEgfr,
                null,
                "mL/min/1,73m²",
                "mL/min/1,73m²",
                evidence with { FieldPath = $"observations[{observations.Count}]" }));
        }

        var specimenEvidence = evidence with { FieldPath = "specimens[0]" };
        var specimens = specimen is null
            ? Array.Empty<LaboratorySpecimen>()
            : [new LaboratorySpecimen(specimen, specimenEvidence)];
        var occurrence = new LaboratoryOccurrence(
            "occurrence-test",
            occurrenceEpisodeKey,
            conceptId,
            "CREATININA",
            "scalar-related",
            LaboratoryRepresentationStatus.FullyStructured,
            observations,
            specimens,
            [],
            [],
            [],
            [],
            null,
            [new OccurrenceSourceSegment("block-test", ["p0001-l0010"], evidence.SourceAppearances)],
            [evidence],
            ["test.fixture"],
            []);
        var header = new SanitizedHeader(
            "issuer", "laboratory", "PACIENTE", sex, "01/01/1976", reportedAge,
            "requester", "1", "REQ", "01/01/2026", "08:00:00", "origin",
            "01/01/2026", "08:00:00", ["p0001-l0000"], []);
        var page = new AssembledPage("doc-test", "test.pdf", 0, 1, header, []);
        var documentaryEpisode = new AssembledEpisode(
            "episode-test", "REQ", "01/01/2026", "08:00:00",
            new EpisodeAgeAtRequest(age, ageStatus, ageStatus == "Computed" ? null : "fixture"),
            new EpisodeAssemblyCoverage(1, 1, 0, 1, 1, 1, 0, 0, 0, 0, true),
            ["origin"], [page], []);
        var semanticEpisode = new SemanticEpisode(
            "episode-test", documentaryEpisode, [occurrence], [],
            new LaboratorySemanticEpisodeCoverage(1, 1, 1, 1, 0, 0, 0, 1, 1));
        var patient = new SemanticPatient(
            "patient-test",
            new PatientIdentity("PACIENTE", "01/01/1976", sex),
            [new PatientSourceDocument("doc-test", "test.pdf", 0)],
            [semanticEpisode]);
        return new SemanticPatientBatch(
            LaboratorySemanticExtractor.CurrentSchemaVersion,
            LaboratorySemanticExtractor.CurrentRulesVersion,
            "1.0.0",
            "fixture",
            [patient],
            new LaboratorySemanticCoverage(1, 1, 1, 1, 1, 1, 0, 0, 0, 1, 1),
            []);
    }
}
