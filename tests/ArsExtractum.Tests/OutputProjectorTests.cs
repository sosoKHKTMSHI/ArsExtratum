using System.Text.Json;
using ArsExtractum.Core.Assembly;
using ArsExtractum.Core.DerivedMeasurements;
using ArsExtractum.Core.Documents;
using ArsExtractum.Core.LaboratorySemantic;
using ArsExtractum.Core.OutputProjection;
using Xunit;

namespace ArsExtractum.Tests;

public sealed class OutputProjectorTests
{
    [Fact]
    public void UnitsAreOptionalAndNeverRemovedFromSemanticSource()
    {
        var source = Enrich(Batch([Occurrence("fsph-nh.sodio", "SÓDIO", "SÓDIO", "139", "mEq/L")]));

        var compact = OutputProjector.Project(new OutputProjectionInput(source));
        var withUnits = OutputProjector.Project(new OutputProjectionInput(source, new OutputProjectionOptions(true)));

        Assert.Contains("Na 139", ClinicalOutputTextFormatter.Format(compact), StringComparison.Ordinal);
        Assert.DoesNotContain("mEq/L", ClinicalOutputTextFormatter.Format(compact), StringComparison.Ordinal);
        Assert.Contains("Na 139 mEq/L", ClinicalOutputTextFormatter.Format(withUnits), StringComparison.Ordinal);
        Assert.Equal("mEq/L", source.Patients[0].Episodes[0].LaboratoryOccurrences[0].Observations[0].RawUnit);
    }

    [Fact]
    public void CreatinineShowsOnlyComputedEgfrTruncatedWithoutRounding()
    {
        var creatinine = Occurrence("fsph-nh.creatinina", "CREATININA", "CREATININA", "2,60", "mg/dl") with
        {
            StructuralForm = "scalar-related",
            Specimens = [new LaboratorySpecimen("SORO", Evidence("specimens[0]"))],
        };
        var result = OutputProjector.Project(new OutputProjectionInput(Enrich(Batch([creatinine], sex: "Feminino", age: 78))));
        var text = ClinicalOutputTextFormatter.Format(result);

        Assert.Contains("Cr 2,60 | TFG 18,3", text, StringComparison.Ordinal);
        Assert.DoesNotContain("FILTRAÇÃO GLOMERULAR ESTIMADA", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mL/min", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MinorKeepsCreatinineWithoutEgfr()
    {
        var creatinine = Occurrence("fsph-nh.creatinina", "CREATININA", "CREATININA", "0,72", "mg/dl") with
        {
            StructuralForm = "scalar-related",
            Specimens = [new LaboratorySpecimen("SORO", Evidence("specimens[0]"))],
        };
        var text = ClinicalOutputTextFormatter.Format(OutputProjector.Project(new OutputProjectionInput(
            Enrich(Batch([creatinine], age: 14)))));

        Assert.Contains("Cr 0,72", text, StringComparison.Ordinal);
        Assert.DoesNotContain("TFG", text, StringComparison.Ordinal);
    }

    [Fact]
    public void EpisodesAreNewestFirstAndCoverageIsCompleteAndDeterministic()
    {
        var older = Episode("episode-old", "01/01/2026", [Occurrence("fsph-nh.sodio", "SÓDIO", "SÓDIO", "138", "mEq/L", "episode-old")]);
        var newer = Episode("episode-new", "02/01/2026", [Occurrence("fsph-nh.potassio", "POTÁSSIO", "POTÁSSIO", "4,2", "mEq/L", "episode-new")]);
        var source = Enrich(BatchFromEpisodes([older, newer]));

        var first = OutputProjector.Project(new OutputProjectionInput(source));
        var second = OutputProjector.Project(new OutputProjectionInput(source));

        Assert.Equal("episode-new", first.Patients[0].Episodes[0].EpisodeKey);
        Assert.True(first.Coverage.IsComplete);
        Assert.Equal(2, first.Coverage.ProjectedOccurrenceCount);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
    }

    [Fact]
    public void MultiOrganismSusceptibilityStaysSeparated()
    {
        var evidence = Evidence("microbiology");
        var occurrence = Occurrence("fsph-nh.antibiograma", "ANTIBIOGRAMA", "RESULTADO", "POSITIVO", null) with
        {
            StructuralForm = "susceptibility-panel",
            Microbiology = new LaboratoryMicrobiology(
                [new LaboratoryOrganism("org-1", "Escherichia coli", evidence), new LaboratoryOrganism("org-2", "Enterococcus faecalis", evidence)],
                [
                    new LaboratorySusceptibilityGroup("group-1", "org-1", "Escherichia coli", [new LaboratorySusceptibilityEntry("Ampicilina", "RESISTENTE", "RESISTENTE", evidence)], evidence),
                    new LaboratorySusceptibilityGroup("group-2", "org-2", "Enterococcus faecalis", [new LaboratorySusceptibilityEntry("Ampicilina", "SENSÍVEL", "SENSIVEL", evidence)], evidence),
                ]),
        };
        var text = ClinicalOutputTextFormatter.Format(OutputProjector.Project(new OutputProjectionInput(
            Enrich(Batch([occurrence])), new OutputProjectionOptions(ShowCultures: true))));

        Assert.Contains("Organismo 1: Escherichia coli", text, StringComparison.Ordinal);
        Assert.Contains("Antibiograma: Ampicilina R", text, StringComparison.Ordinal);
        Assert.Contains("Organismo 2: Enterococcus faecalis", text, StringComparison.Ordinal);
        Assert.Contains("Antibiograma: Ampicilina S", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RelatedSusceptibilityIsRenderedInsideCultureWithoutDuplicateOrganismAndKeepsMicUnit()
    {
        var evidence = Evidence("microbiology");
        var culture = Occurrence("fsph-nh.cultural-de-aspirado-traqueal", "CULTURAL DE ASPIRADO TRAQUEAL", "CULTURAL", "Pseudomonas aeruginosa", null) with
        {
            StructuralForm = "microbiology-culture",
            Microbiology = new LaboratoryMicrobiology(
                [new LaboratoryOrganism("culture-org", "Pseudomonas aeruginosa", evidence)], []),
        };
        var susceptibility = Occurrence("fsph-nh.antibiograma-de-aspirado-traqueal", "ANTIBIOGRAMA DE ASPIRADO TRAQUEAL", "RESULTADO", "SENSÍVEL", null) with
        {
            StructuralForm = "susceptibility-panel",
            Relationships = [new LaboratoryRelationship("relationship-test", "culture-has-susceptibility",
                culture.OccurrenceId, "susceptibility-test", evidence)],
            Microbiology = new LaboratoryMicrobiology(
                [new LaboratoryOrganism("susceptibility-org", "Pseudomonas aeruginosa", evidence)],
                [new LaboratorySusceptibilityGroup("group-test", "susceptibility-org", "Pseudomonas aeruginosa",
                    [new LaboratorySusceptibilityEntry("Ceftriaxona", "MIC ≤1 mg/L (S)", "SENSIVEL", evidence)], evidence)]),
        } with { OccurrenceId = "susceptibility-test" };

        var result = OutputProjector.Project(new OutputProjectionInput(Enrich(Batch([culture, susceptibility])),
            new OutputProjectionOptions(ShowCultures: true)));
        var text = ClinicalOutputTextFormatter.Format(result);

        Assert.Equal(1, result.Coverage.SuppressedByExplicitPolicyCount);
        Assert.Equal(1, text.Split("Organismo 1:", StringSplitOptions.None).Length - 1);
        Assert.Contains("Antibiograma: Ceftriaxona MIC ≤1 mg/L (S)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PlateletScalarUsesPlaqAbbreviation()
    {
        var text = ClinicalOutputTextFormatter.Format(OutputProjector.Project(new OutputProjectionInput(
            Enrich(Batch([Occurrence("fsph-nh.plaquetas", "PLAQUETAS", "PLAQUETAS", "304.000", "/mm3")])))));

        Assert.Contains("Plaq 304.000", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Plaquetas 304.000", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CulturesAreHiddenByDefaultButWarnedAndFieldAccounted()
    {
        var evidence = Evidence("microbiology");
        var culture = Occurrence("fsph-nh.cultura-de-swab-retal", "CULTURA DE SWAB RETAL", "CULTURAL", "NEGATIVO", null) with
        {
            StructuralForm = "microbiology-culture",
            Narratives = [new LaboratoryNarrative("Negativo para Acinetobacter resistente aos carbapenêmicos", "fixture", evidence)],
            Specimens = [new LaboratorySpecimen("SWAB RETAL", evidence)],
            Microbiology = new LaboratoryMicrobiology([], []),
        };

        var source = Enrich(Batch([culture]));
        var result = OutputProjector.Project(new OutputProjectionInput(source));
        var projected = Assert.Single(Assert.Single(result.Patients).Episodes[0].ProjectedOccurrences);

        Assert.False(result.Options.ShowCultures);
        Assert.Equal(ProjectionDisposition.SuppressedByExplicitPolicy, projected.Disposition);
        var text = ClinicalOutputTextFormatter.Format(result);
        Assert.Contains("Laboratoriais (01/01/2026 – 08:00): Culturais", text, StringComparison.Ordinal);
        Assert.DoesNotContain("NEGATIVO", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Acinetobacter", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.Notices, static notice => notice.Code == "output-projection.culture-verification-required");
        Assert.NotEmpty(projected.FieldProjectionRecords);
        Assert.All(projected.FieldProjectionRecords, static field => Assert.Equal(FieldProjectionDisposition.AuditOnly, field.Disposition));
        Assert.True(result.Coverage.IsComplete);
        Assert.Equal(result.Coverage.SourceFieldCount, result.Coverage.AccountedFieldCount);
    }

    [Fact]
    public void CoagulationUsesFrozenTpRniAndTtpaEditorialForms()
    {
        var evidence = Evidence("observations");
        var tp = Occurrence("fsph-nh.tempo-de-protrombina-tp", "TEMPO DE PROTROMBINA (TP)", "ATIVIDADE", "86,0", "%") with
        {
            StructuralForm = "key-value-panel",
            Observations =
            [
                new("activity", "ATIVIDADE", "86,0", 86m, null, "%", "%", evidence),
                new("rni", "RNI", "1,09", 1.09m, null, null, null, evidence),
                new("control", "CONTROLE NORMAL", "10,8", 10.8m, null, "segundos", "segundos", evidence),
            ],
        };
        var ttpa = Occurrence("fsph-nh.tempo-de-tromboplastina-parcial-ativada-ttpa", "TTPA", "AMOSTRA", "22,0", "segundos") with
        {
            StructuralForm = "key-value-panel",
        };

        var text = ClinicalOutputTextFormatter.Format(OutputProjector.Project(new OutputProjectionInput(Enrich(Batch([tp, ttpa])))));

        Assert.Contains("TP: Atividade 86,0 | RNI 1,09 | TTPa 22,0", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Controle Normal", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Amostra 22,0", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HemogramOpensMainLineAndPairsDifferentialPercentageWithAbsoluteCount()
    {
        var evidence = Evidence("observations");
        LaboratoryObservation Observation(string id, string label, string value, string? unit) =>
            new(id, label, value, null, null, unit, unit, evidence);
        var hemogram = Occurrence("fsph-nh.hemograma-completo", "HEMOGRAMA COMPLETO", "HEMOGLOBINA", "10,8", "g/dL") with
        {
            StructuralForm = "sectioned-panel",
            Observations =
            [
                Observation("hb", "HEMOGLOBINA", "10,8", "g/dL"),
                Observation("leuco", "LEUCÓCITOS", "14.260", "/mm3"),
                Observation("neut-percent", "SEGMENTADOS (NEUTRÓFILOS)", "82", "%"),
                Observation("neut-absolute", "SEGMENTADOS (NEUTRÓFILOS)", "11.693", "/mm3"),
                Observation("meta-percent", "METAMIELÓCITOS", "4,00", "%"),
                Observation("meta-absolute", "METAMIELÓCITOS", "786", "mm3"),
                Observation("myelo-percent", "MIELÓCITOS", "3,00", "%"),
                Observation("myelo-absolute", "MIELÓCITOS", "284", "mm3"),
                Observation("blasts-percent", "BLASTOS", "6,00", "%"),
                Observation("blasts-absolute", "BLASTOS", "1.179", "mm3"),
                Observation("linf", "LINFÓCITOS", "10", "%"),
                Observation("erythroblasts", "ERITROBLASTOS/100 LEUCÓCITOS", "18,00", null),
                Observation("morphology", "OBSERVAÇÃO", "Anisocitose", null),
                Observation("platelets", "PLAQUETAS", "186.000", "/mm3"),
            ],
        };
        var text = ClinicalOutputTextFormatter.Format(OutputProjector.Project(new OutputProjectionInput(Enrich(Batch([hemogram])))));

        Assert.Contains("Hb 10,8 | Leuco 14.260 (Neut 82% [11.693] | Meta 4,00% [786] | Mielo 3,00% [284] | Blastos 6,00% [1.179] | Linf 10% | Eritroblastos/100 Leuco 18,00) | Observação laboratorial: Anisocitose | Plaq 186.000", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Hemograma:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BloodGasUsesCanonicalOrderInsteadOfDocumentOrder()
    {
        var evidence = Evidence("observations");
        var gas = Occurrence("fsph-nh.gasometria-arterial", "GASOMETRIA ARTERIAL", "pO2", "72", "mmHg") with
        {
            StructuralForm = "key-value-panel",
            Observations =
            [
                new("po2", "pO2", "72", 72, null, "mmHg", "mmHg", evidence),
                new("ph", "pH", "7,31", 7.31m, null, null, null, evidence),
                new("pco2", "pCO2", "48", 48, null, "mmHg", "mmHg", evidence),
            ],
        };
        var text = ClinicalOutputTextFormatter.Format(OutputProjector.Project(new OutputProjectionInput(Enrich(Batch([gas])))));

        Assert.Contains("Gasometria arterial: pH 7,31 | pCO2 48 | pO2 72", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ErythroblastsAreNotMisclassifiedAsBlasts()
    {
        var evidence = Evidence("observations");
        var hemogram = Occurrence("fsph-nh.hemograma-completo", "HEMOGRAMA COMPLETO",
            "LEUCÓCITOS", "13.440", "/mm3") with
        {
            StructuralForm = "sectioned-panel",
            Observations =
            [
                new("leuco", "LEUCÓCITOS", "13.440", null, null, "/mm3", "/mm3", evidence),
                new("erythroblasts", "ERITROBLASTOS/100 LEUCÓCITOS", "1,00", 1m, null, null, null, evidence),
            ],
        };

        var text = ClinicalOutputTextFormatter.Format(OutputProjector.Project(
            new OutputProjectionInput(Enrich(Batch([hemogram])))));

        Assert.Contains("Leuco 13.440 (Eritroblastos/100 Leuco 1,00)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Blastos", text.Replace("Eritroblastos", string.Empty, StringComparison.Ordinal),
            StringComparison.Ordinal);
    }

    [Fact]
    public void BacterioscopyPreservesSecondaryObservationLabels()
    {
        var evidence = Evidence("observations");
        var bacterioscopy = Occurrence("fsph-nh.bacterioscopico-gram", "BACTERIOSCÓPICO (GRAM)",
            "RESULTADO", "PRESENÇA DE COCOS GRAM POSITIVOS AGLOMERADOS", null) with
        {
            StructuralForm = "qualitative-narrative",
            Observations =
            [
                new("result", "RESULTADO", "PRESENÇA DE COCOS GRAM POSITIVOS AGLOMERADOS", null, null, null, null, evidence),
                new("positivity", "TEMPO DE POSITIVIDADE", "25,00", 25m, null, "horas", "horas", evidence),
            ],
        };

        var result = OutputProjector.Project(new OutputProjectionInput(Enrich(Batch([bacterioscopy]))));
        var projected = Assert.Single(Assert.Single(result.Patients).Episodes[0].ProjectedOccurrences);

        var text = ClinicalOutputTextFormatter.Format(result);
        Assert.Contains("PRESENÇA DE COCOS GRAM POSITIVOS AGLOMERADOS", text, StringComparison.Ordinal);
        Assert.Contains("Tempo de positividade 25,00", text, StringComparison.Ordinal);
        Assert.All(projected.FieldProjectionRecords.Where(static field =>
                field.Disposition == FieldProjectionDisposition.Projected),
            static field =>
            {
                Assert.NotNull(field.OutputLineIndex);
                Assert.False(string.IsNullOrWhiteSpace(field.OutputFragment));
            });
    }

    [Fact]
    public void EveryProjectedFieldHasVerifiableOutputLocator()
    {
        var source = Enrich(Batch([
            Occurrence("fsph-nh.sodio", "SÓDIO", "SÓDIO", "139", "mEq/L"),
            Occurrence("fsph-nh.potassio", "POTÁSSIO", "POTÁSSIO", "4,2", "mEq/L"),
        ]));

        var result = OutputProjector.Project(new OutputProjectionInput(source));
        var projectedFields = result.Patients.SelectMany(static patient => patient.Episodes)
            .SelectMany(static episode => episode.ProjectedOccurrences)
            .SelectMany(static occurrence => occurrence.FieldProjectionRecords)
            .Where(static field => field.Disposition == FieldProjectionDisposition.Projected)
            .ToArray();

        Assert.NotEmpty(projectedFields);
        Assert.All(projectedFields, static field =>
        {
            Assert.NotNull(field.OutputLineIndex);
            Assert.False(string.IsNullOrWhiteSpace(field.OutputFragment));
        });
        Assert.Equal(0, result.Coverage.FieldProjectionFailureCount);
    }

    private static SemanticPatientBatch Enrich(SemanticPatientBatch batch) =>
        DerivedMeasurementComputer.Enrich(new DerivedMeasurementComputationInput(batch));

    private static SemanticPatientBatch Batch(
        IReadOnlyList<LaboratoryOccurrence> occurrences,
        string? sex = "Feminino",
        int age = 50) => BatchFromEpisodes([Episode("episode-test", "01/01/2026", occurrences, age)], sex);

    private static SemanticPatientBatch BatchFromEpisodes(
        IReadOnlyList<SemanticEpisode> episodes,
        string? sex = "Feminino")
    {
        var occurrenceCount = episodes.Sum(static item => item.LaboratoryOccurrences.Count);
        return new SemanticPatientBatch(
            LaboratorySemanticExtractor.CurrentSchemaVersion,
            LaboratorySemanticExtractor.CurrentRulesVersion,
            "1.0.0", "fixture",
            [new SemanticPatient("patient-test", new PatientIdentity("PACIENTE", "01/01/1976", sex), [new PatientSourceDocument("doc-test", "test.pdf", 0)], episodes)],
            new LaboratorySemanticCoverage(1, episodes.Count, occurrenceCount, occurrenceCount, occurrenceCount, occurrenceCount, 0, 0, 0, occurrenceCount, occurrenceCount),
            []);
    }

    private static SemanticEpisode Episode(
        string episodeKey,
        string date,
        IReadOnlyList<LaboratoryOccurrence> occurrences,
        int age = 50)
    {
        var page = new AssembledPage("doc-test", "test.pdf", 0, 1,
            new SanitizedHeader("issuer", "laboratory", "PACIENTE", "Feminino", "01/01/1976", age.ToString(System.Globalization.CultureInfo.InvariantCulture), "requester", "1", "REQ", date, "08:00:00", "origin", date, "08:00:00", [], []), []);
        var documentary = new AssembledEpisode(episodeKey, "REQ", date, "08:00:00",
            new EpisodeAgeAtRequest(age, "Computed", null),
            new EpisodeAssemblyCoverage(1, 1, 0, occurrences.Count, occurrences.Count, occurrences.Count, 0, 0, 0, 0, true),
            ["origin"], [page], []);
        return new SemanticEpisode(episodeKey, documentary, occurrences, [],
            new LaboratorySemanticEpisodeCoverage(occurrences.Count, occurrences.Count, occurrences.Count, occurrences.Count, 0, 0, 0, occurrences.Count, occurrences.Count));
    }

    private static LaboratoryOccurrence Occurrence(
        string conceptId,
        string displayName,
        string label,
        string value,
        string? unit,
        string episodeKey = "episode-test")
    {
        var evidence = Evidence("observations[0]");
        return new LaboratoryOccurrence(
            $"occurrence-{conceptId}-{episodeKey}", episodeKey, conceptId, displayName, "scalar",
            LaboratoryRepresentationStatus.FullyStructured,
            [new LaboratoryObservation($"observation-{conceptId}-{episodeKey}", label, value,
                decimal.TryParse(value.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.'),
                    System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var numeric) ? numeric : null,
                null, unit, unit, evidence)],
            [], [], [], [], [], null,
            [new OccurrenceSourceSegment("block-test", ["p0001-l0010"], evidence.SourceAppearances)],
            [evidence], ["fixture"], []);
    }

    private static SemanticFieldEvidence Evidence(string fieldPath) => new(
        fieldPath, "block-test", "p0001-l0010", "original", "sanitized", ["word-1"], ["fixture"], [],
        [new SemanticSourceAppearance("doc-test", "test.pdf", 0, 1, "p0001-l0010", ["word-1"])]);
}
