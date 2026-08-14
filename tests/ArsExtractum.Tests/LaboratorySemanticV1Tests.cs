using System.Security.Cryptography;
using System.Text.Json;
using ArsExtractum.Core.Assembly;
using ArsExtractum.Core.Documents;
using ArsExtractum.Core.LaboratorySemantic;
using Xunit;

namespace ArsExtractum.Tests;

public sealed class LaboratorySemanticV1Tests
{
    private static readonly JsonSerializerOptions CaseInsensitiveJson = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    [Fact]
    public void BuiltInCatalogIsClosedAndEveryEntryHasCorpusEvidence()
    {
        var catalog = ReferenceLaboratoryCatalog.LoadBuiltIn();

        Assert.Equal("1.0.0", catalog.Document.CatalogVersion);
        Assert.Equal("fsph-nh-laboratory-catalog", catalog.Document.CatalogId);
        Assert.Equal(87, catalog.Concepts.Count);
        Assert.Equal(98, catalog.Concepts.Sum(static item => item.ObservedAliases.Count));
        Assert.Equal(9, catalog.Concepts.Select(static item => item.StructuralFormId)
            .Distinct(StringComparer.Ordinal).Count());
        Assert.All(catalog.Concepts, static concept =>
        {
            Assert.NotEmpty(concept.ObservedAliases);
            Assert.NotEmpty(concept.EvidenceLocators);
            Assert.All(concept.EvidenceLocators, static evidence =>
            {
                Assert.NotEmpty(evidence.BlockId);
                Assert.NotEmpty(evidence.LineId);
            });
        });
        Assert.True(catalog.TryMatch("CREATININA: 2,20 mg/dL", out _));
        Assert.False(catalog.TryMatch("GLUCOSE: 100 mg/dL", out _));
    }

    [Fact]
    public void ExtractionPreservesAssemblyIdentityRawValuesAndEquivalentSources()
    {
        var first = Document("a.pdf", "doc-a", 0,
            "CREATININA: 2,20 mg/dL", "Material: SORO");
        var second = Document("b.pdf", "doc-b", 1,
            "CREATININA: 2,20 mg/dL", "Material: SORO");
        var batch = PatientEpisodeAssembler.Assemble(
        [
            new PatientAssemblyInput(first, 0),
            new PatientAssemblyInput(second, 1),
        ]);

        var semantic = new LaboratorySemanticExtractor().Extract(new LaboratorySemanticExtractionInput(batch));
        var assembledPatient = Assert.Single(batch.Patients);
        var assembledEpisode = Assert.Single(assembledPatient.Episodes);
        var semanticPatient = Assert.Single(semantic.Patients);
        var semanticEpisode = Assert.Single(semanticPatient.Episodes);
        var occurrence = Assert.Single(semanticEpisode.LaboratoryOccurrences);

        Assert.Equal(assembledPatient.PatientKey, semanticPatient.PatientKey);
        Assert.Equal(assembledEpisode.EpisodeKey, semanticEpisode.EpisodeKey);
        Assert.Equal(assembledEpisode.EpisodeKey, occurrence.EpisodeKey);
        Assert.Equal("2,20", occurrence.Observations[0].RawValue);
        Assert.Equal(2.20m, occurrence.Observations[0].NumericValue);
        Assert.Equal("mg/dL", occurrence.Observations[0].RawUnit);
        Assert.Equal("SORO", Assert.Single(occurrence.Specimens).RawSpecimen);
        Assert.Equal(2, occurrence.FieldEvidence[0].SourceAppearances.Count);
        Assert.True(semantic.Coverage.IsLossless);
        Assert.Equal(0, semantic.Coverage.UnsupportedActiveLineCount);
    }

    [Fact]
    public void HemogramComponentsRemainInsideOnePanelOccurrence()
    {
        var batch = PatientEpisodeAssembler.Assemble([
            Document("hemogram.pdf", "doc-h", 0,
                "HEMOGRAMA COMPLETO",
                "ERITRÓCITOS",
                "Hemácias: 4,35 p/mm3 /milhoes",
                "Hemoglobina: 12,10 g/dl",
                "Hematócrito: 39,60 %",
                "LEUCÓCITOS",
                "Leucócitos (p/mm3): 12.650",
                "PLAQUETAS: 317.000/mm³",
                "Material: SANGUE/EDTA")
        ]);

        var semantic = new LaboratorySemanticExtractor().Extract(new LaboratorySemanticExtractionInput(batch));
        var occurrence = Assert.Single(Assert.Single(Assert.Single(semantic.Patients).Episodes).LaboratoryOccurrences);

        Assert.Equal("sectioned-panel", occurrence.StructuralForm);
        Assert.Contains(occurrence.Observations, static item => item.Label == "PLAQUETAS");
        Assert.True(semantic.Coverage.IsLossless);
    }

    [Fact]
    public void UrinalysisFieldsRemainInsideOnePanelOccurrence()
    {
        var batch = PatientEpisodeAssembler.Assemble([
            Document("equ.pdf", "doc-equ", 0,
                "EXAME QUALITATIVO DE URINA (EQU)",
                "EXAME QUÍMICO",
                "pH: 5,0",
                "Proteínas: AUSENTE",
                "PESQUISA DE ELEMENTOS FIGURADOS",
                "Leucócitos: 3 p/campo",
                "Material: URINA")
        ]);

        var semantic = new LaboratorySemanticExtractor().Extract(new LaboratorySemanticExtractionInput(batch));
        var occurrence = Assert.Single(Assert.Single(Assert.Single(semantic.Patients).Episodes).LaboratoryOccurrences);

        Assert.Equal("sectioned-panel", occurrence.StructuralForm);
        Assert.Contains(occurrence.Observations, static item => item.Label == "pH");
        Assert.Contains(occurrence.Observations, static item => item.Label == "Proteínas");
        Assert.True(semantic.Coverage.IsLossless);
    }

    [Fact]
    public void SusceptibilityKeepsEachColumnBoundToItsOrganism()
    {
        var batch = PatientEpisodeAssembler.Assemble([
            Document("antibiogram.pdf", "doc-m", 0,
                "ANTIBIOGRAMA",
                "Germe 1 | Germe 2",
                "Escherichia coli | Klebsiella pneumoniae ssp pneumoniae",
                "Ampicilina:RESISTENTE | Ampicilina:SENSÍVEL",
                "Nitrofurantoína:SENSÍVEL | Nitrofurantoína:INTERMEDIÁRIO")
        ]);

        var semantic = new LaboratorySemanticExtractor().Extract(new LaboratorySemanticExtractionInput(batch));
        var occurrence = Assert.Single(Assert.Single(Assert.Single(semantic.Patients).Episodes).LaboratoryOccurrences);
        var microbiology = Assert.IsType<LaboratoryMicrobiology>(occurrence.Microbiology);

        Assert.Equal(2, microbiology.Organisms.Count);
        Assert.Equal(2, microbiology.SusceptibilityGroups.Count);
        Assert.Equal("RESISTENTE", microbiology.SusceptibilityGroups[0].Entries[0].Interpretation);
        Assert.Equal("SENSIVEL", microbiology.SusceptibilityGroups[1].Entries[0].Interpretation);
        Assert.Equal("SENSIVEL", microbiology.SusceptibilityGroups[0].Entries[1].Interpretation);
        Assert.Equal("INTERMEDIARIO", microbiology.SusceptibilityGroups[1].Entries[1].Interpretation);
        Assert.DoesNotContain(occurrence.Observations, static item => item.RawValue is "1" or "2");
    }

    [Fact]
    public void DigitsInAssayNamesAreNotInventedAsMeasurements()
    {
        var batch = PatientEpisodeAssembler.Assemble([
            Document("assay.pdf", "doc-assay", 0,
                "ANTI-HIV 1 E 2",
                "RESULTADO: NÃO REAGENTE",
                "Material: SORO")
        ]);

        var semantic = new LaboratorySemanticExtractor().Extract(new LaboratorySemanticExtractionInput(batch));
        var occurrence = Assert.Single(Assert.Single(Assert.Single(semantic.Patients).Episodes).LaboratoryOccurrences);

        Assert.DoesNotContain(occurrence.Observations, static item => item.NumericValue is not null);
        Assert.Contains(occurrence.Observations, static item => item.CodedValue == "NÃO REAGENTE");
    }

    [Fact]
    public void TtpaTreatsAmostraAsResultAndMaterialAsSpecimen()
    {
        var batch = PatientEpisodeAssembler.Assemble([
            Document("ttpa.pdf", "doc-ttpa", 0,
                "TEMPO DE TROMBOPLASTINA PARCIAL ATIVADA (TTPA)",
                "AMOSTRA: 22,1 segundos",
                "Material: SANGUE/CITRATO")
        ]);

        var semantic = new LaboratorySemanticExtractor().Extract(new LaboratorySemanticExtractionInput(batch));
        var occurrence = Assert.Single(Assert.Single(Assert.Single(semantic.Patients).Episodes).LaboratoryOccurrences);

        var result = Assert.Single(occurrence.Observations);
        Assert.Equal("AMOSTRA", result.Label);
        Assert.Equal("22,1", result.RawValue);
        Assert.Equal("segundos", result.RawUnit);
        Assert.Equal("SANGUE/CITRATO", Assert.Single(occurrence.Specimens).RawSpecimen);
    }

    [Fact]
    public void FrozenAssemblyCorpusHasCompleteDeterministicSemanticCoverageWhenConfigured()
    {
        var path = Environment.GetEnvironmentVariable("ARS_EXTRACTUM_ASSEMBLY_V1_JSON");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var batch = JsonSerializer.Deserialize<PatientBatch>(File.ReadAllText(path), CaseInsensitiveJson);
        Assert.NotNull(batch);
        var extractor = new LaboratorySemanticExtractor();
        var first = extractor.Extract(new LaboratorySemanticExtractionInput(batch));
        var second = extractor.Extract(new LaboratorySemanticExtractionInput(batch));

        Assert.Equal(11, first.Coverage.PatientCount);
        Assert.Equal(298, first.Coverage.EpisodeCount);
        Assert.Equal(1687, first.Coverage.CanonicalBlockCount);
        Assert.Equal(9008, first.Coverage.CanonicalActiveLineCount);
        Assert.Equal(9008, first.Coverage.OwnedActiveLineCount);
        Assert.Equal(0, first.Coverage.UnsupportedActiveLineCount);
        Assert.Equal(0, first.Coverage.MultiplyOwnedActiveLineCount);
        Assert.Equal(0, first.Coverage.RepresentationFailureCount);
        Assert.Equal(first.Coverage.KnownAnchorCount, first.Coverage.RecognizedAnchorCount);
        Assert.True(first.Coverage.IsLossless);
        var occurrences = first.Patients.SelectMany(static patient => patient.Episodes)
            .SelectMany(static episode => episode.LaboratoryOccurrences).ToArray();
        var catalog = ReferenceLaboratoryCatalog.LoadBuiltIn();
        var countsByConcept = occurrences.GroupBy(static item => item.ConceptId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
        Assert.All(catalog.Concepts, concept => Assert.Equal(
            concept.ObservedOccurrenceCount,
            countsByConcept.GetValueOrDefault(concept.ConceptId)));
        Assert.Equal(9008, occurrences.Sum(static item => item.FieldEvidence.Count));
        Assert.Equal(9008, occurrences.Sum(static item =>
            item.SourceSegments.Sum(static segment => segment.CanonicalLineIds.Count)));
        Assert.All(occurrences.SelectMany(static item => item.FieldEvidence), static evidence =>
            Assert.NotEmpty(evidence.SourceAppearances));
        var specimenLineCount = occurrences.Sum(occurrence => occurrence.FieldEvidence.Count(evidence =>
            (ReferenceLaboratoryCatalog.Normalize(
                ReferenceLaboratoryCatalog.LiteralLabel(evidence.SanitizedText)) is "MATERIAL" or "AMOSTRA") &&
            !(occurrence.ConceptId == "fsph-nh.tempo-de-tromboplastina-parcial-ativada-ttpa" &&
              ReferenceLaboratoryCatalog.Normalize(
                  ReferenceLaboratoryCatalog.LiteralLabel(evidence.SanitizedText)) == "AMOSTRA")));
        Assert.Equal(specimenLineCount, occurrences.Sum(static item => item.Specimens.Count));
        var multiOrganism = Assert.Single(occurrences, static item =>
            item.Microbiology?.SusceptibilityGroups.Count > 1);
        Assert.Equal(2, multiOrganism.Microbiology!.Organisms.Count);
        Assert.Equal(2, multiOrganism.Microbiology.SusceptibilityGroups.Count);
        Assert.All(first.Patients.SelectMany(static patient => patient.Episodes), static episode =>
        {
            Assert.Equal(episode.DocumentaryEpisode.EpisodeKey, episode.EpisodeKey);
            Assert.True(episode.Coverage.IsLossless);
            Assert.Empty(episode.UnsupportedContent);
        });
        var firstBytes = JsonSerializer.SerializeToUtf8Bytes(first);
        var secondBytes = JsonSerializer.SerializeToUtf8Bytes(second);
        var firstHash = Hash(firstBytes);
        var secondHash = Hash(secondBytes);
        Assert.Equal(firstHash, secondHash);

        var outputDirectory = Environment.GetEnvironmentVariable("ARS_EXTRACTUM_SEMANTIC_V1_OUTPUT");
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllBytes(Path.Combine(outputDirectory, "semantic-patient-batch.json"), firstBytes);
            File.WriteAllText(
                Path.Combine(outputDirectory, "validation-summary.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = "laboratory-semantic-v1-validation/1.0",
                    firstHash,
                    secondHash,
                    deterministic = firstHash == secondHash,
                    first.Coverage,
                }, IndentedJson));
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static SanitizedDocument Document(
        string fileName,
        string documentId,
        int inputIndex,
        params string[] texts)
    {
        var lines = texts.Select((text, index) => new SanitizedLine(
            $"p0001-l{index + 10:D4}",
            index + 10,
            text,
            text,
            SanitizedDisposition.Active,
            ["test.fixture"],
            [$"p0001-w{index + 10:D6}"],
            [])).ToArray();
        var header = new SanitizedHeader(
            "Fundação de Saúde Pública de Novo Hamburgo",
            "Laboratório Público Municipal",
            "PACIENTE TESTE",
            "Feminino",
            "01/01/1980",
            "46",
            "MÉDICO",
            "1",
            "REQ-1",
            "01/01/2026",
            "08:00:00",
            "ORIGEM",
            "01/01/2026",
            "08:00:00",
            [],
            []);
        return new SanitizedDocument("test", "test", documentId, fileName,
            [new SanitizedPage(1, header, lines, true)]);
    }
}
