using ArsExtractum.Core.Assembly;
using ArsExtractum.Core.Documents;
using Xunit;

namespace ArsExtractum.Tests;

public sealed class PatientEpisodeAssemblerTests
{
    [Fact]
    public void AssembleGroupsDocumentsByPatientAndOrdersExactEpisodes()
    {
        var batch = PatientEpisodeAssembler.Assemble(
        [
            Document("first.pdf", "doc-1", Page(1, "PACIENTE UM", "01/01/1980", "REQ-2", "02/01/2026", "12:00:00")),
            Document("second.pdf", "doc-2", Page(1, "PACIENTE UM", "01/01/1980", "REQ-1", "01/01/2026", "08:00:00")),
            Document("third.pdf", "doc-3", Page(1, "PACIENTE DOIS", "02/02/1990", "REQ-3", "03/01/2026", "09:00:00")),
        ]);

        Assert.Equal(2, batch.Patients.Count);
        Assert.Equal(PatientEpisodeAssembler.CurrentSchemaVersion, batch.SchemaVersion);
        Assert.Equal("1.2", batch.SchemaVersion);
        Assert.Equal(PatientEpisodeAssembler.CurrentRulesVersion, batch.RulesVersion);
        Assert.Equal("patient-episode-assembly-rules/1.0", batch.RulesVersion);
        Assert.Empty(batch.UnassignedDocuments);
        var firstPatient = Assert.Single(
            batch.Patients,
            static patient => patient.Identity.PatientName == "PACIENTE UM");
        Assert.Equal(2, firstPatient.SourceDocuments.Count);
        Assert.Equal(["REQ-2", "REQ-1"], firstPatient.Episodes.Select(static episode => episode.RequestNumber));
        Assert.Equal(3, batch.Ledger.Count);
        Assert.All(batch.Ledger, static entry => Assert.Equal("Assigned", entry.Disposition));
        Assert.Contains("RESULTADO: 1", PatientAssemblyTextFormatter.Format(batch, firstPatient.PatientKey));
        Assert.DoesNotContain("PACIENTE DOIS", PatientAssemblyTextFormatter.Format(batch, firstPatient.PatientKey));
    }

    [Fact]
    public void AssembleMergesSameEpisodeAcrossDocumentsWithoutDroppingPages()
    {
        var batch = PatientEpisodeAssembler.Assemble(
        [
            Document("part-a.pdf", "doc-a", Page(1, "PACIENTE UM", "01/01/1980", "REQ-1", "01/01/2026", "08:00:00")),
            Document("part-b.pdf", "doc-b", Page(1, "PACIENTE UM", "01/01/1980", "REQ-1", "01/01/2026", "08:00:00")),
        ]);

        var patient = Assert.Single(batch.Patients);
        var episode = Assert.Single(patient.Episodes);
        Assert.Equal(2, episode.Pages.Count);
        Assert.Equal(["part-a.pdf", "part-b.pdf"], episode.Pages.Select(static page => page.FileName));
        var block = Assert.Single(episode.ContentBlocks);
        Assert.Equal(2, block.Sources.Count);
        Assert.Equal(["part-a.pdf", "part-b.pdf"], block.Sources.Select(static source => source.FileName));
        Assert.Equal("episode.exact-active-page-content.v1", block.Equivalence.RuleId);
        Assert.Equal("ExactOrdinal", block.Equivalence.Comparison);
        Assert.Equal(block.Sources.Count, block.Equivalence.SourceCount);
        Assert.Equal(46, episode.AgeAtRequest.CompletedYears);
        Assert.Equal("Computed", episode.AgeAtRequest.Status);
        Assert.Equal(2, episode.Coverage.SourcePageCount);
        Assert.Equal(2, episode.Coverage.ActivePageCount);
        Assert.Equal(2, episode.Coverage.SourceActiveLineCount);
        Assert.Equal(1, episode.Coverage.CanonicalBlockCount);
        Assert.Equal(1, episode.Coverage.CanonicalActiveLineCount);
        Assert.Equal(1, episode.Coverage.EquivalentSourceCount);
        Assert.Equal(1, episode.Coverage.DeduplicatedLineCount);
        Assert.Equal(0, episode.Coverage.OrphanSourceCount);
        Assert.Equal(0, episode.Coverage.MultiplyAssignedSourceCount);
        Assert.True(episode.Coverage.IsLossless);
        var output = PatientAssemblyTextFormatter.Format(batch);
        Assert.Equal(1, CountOccurrences(output, "RESULTADO: 1"));
        Assert.Contains("Origens equivalentes:", output);
    }

    [Fact]
    public void AssembleDoesNotDeduplicatePartiallyDifferentContent()
    {
        var batch = PatientEpisodeAssembler.Assemble(
        [
            Document("part-a.pdf", "doc-a", Page(1, "PACIENTE UM", "01/01/1980", "REQ-1", "01/01/2026", "08:00:00")),
            Document("part-b.pdf", "doc-b", Page(2, "PACIENTE UM", "01/01/1980", "REQ-1", "01/01/2026", "08:00:00")),
        ]);

        var episode = Assert.Single(Assert.Single(batch.Patients).Episodes);
        Assert.Equal(2, episode.ContentBlocks.Count);
        Assert.All(episode.ContentBlocks, static block => Assert.Single(block.Sources));
        Assert.All(episode.ContentBlocks, static block => Assert.Equal(1, block.Equivalence.SourceCount));
        Assert.Equal(2, episode.Coverage.CanonicalBlockCount);
        Assert.Equal(0, episode.Coverage.EquivalentSourceCount);
        Assert.Equal(0, episode.Coverage.DeduplicatedLineCount);
        Assert.True(episode.Coverage.IsLossless);
    }

    [Fact]
    public void AssemblePreservesRepeatedSubmissionOfTheSameDocumentAsDistinctSources()
    {
        var document = Document(
            "same.pdf",
            "doc-same",
            Page(1, "PACIENTE UM", "01/01/1980", "REQ-1", "01/01/2026", "08:00:00"));

        var batch = PatientEpisodeAssembler.Assemble(
        [
            new PatientAssemblyInput(document, 0),
            new PatientAssemblyInput(document, 1),
        ]);

        var episode = Assert.Single(Assert.Single(batch.Patients).Episodes);
        var block = Assert.Single(episode.ContentBlocks);
        Assert.Equal(2, block.Sources.Count);
        Assert.Equal([0, 1], block.Sources.Select(static source => source.InputIndex));
        Assert.Equal(0, episode.Coverage.MultiplyAssignedSourceCount);
        Assert.True(episode.Coverage.IsLossless);
    }

    [Fact]
    public void AssembleFreezesDeterministicPatientAndEpisodeKeys()
    {
        var document = Document(
            "fixture.pdf",
            "doc-fixture",
            Page(1, "PACIENTE UM", "01/01/1980", "REQ-1", "01/01/2026", "08:00:00"));

        var first = PatientEpisodeAssembler.Assemble([document]);
        var second = PatientEpisodeAssembler.Assemble([document]);
        var firstPatient = Assert.Single(first.Patients);
        var secondPatient = Assert.Single(second.Patients);
        var firstEpisode = Assert.Single(firstPatient.Episodes);
        var secondEpisode = Assert.Single(secondPatient.Episodes);

        Assert.Equal("patient-0b5ec00765f21599", firstPatient.PatientKey);
        Assert.Equal("episode-a41f94dc01973cb9", firstEpisode.EpisodeKey);
        Assert.Equal(firstPatient.PatientKey, secondPatient.PatientKey);
        Assert.Equal(firstEpisode.EpisodeKey, secondEpisode.EpisodeKey);
    }

    [Fact]
    public void AssembleKeepsDifferentRequestsAndTimesInDifferentEpisodes()
    {
        var batch = PatientEpisodeAssembler.Assemble(
        [
            Document("request-a.pdf", "doc-a", Page(1, "PACIENTE UM", "01/01/1980", "REQ-1", "01/01/2026", "08:00:00")),
            Document("request-b.pdf", "doc-b", Page(1, "PACIENTE UM", "01/01/1980", "REQ-2", "01/01/2026", "08:00:00")),
            Document("time-b.pdf", "doc-c", Page(1, "PACIENTE UM", "01/01/1980", "REQ-1", "01/01/2026", "09:00:00")),
        ]);

        var episodes = Assert.Single(batch.Patients).Episodes;
        Assert.Equal(3, episodes.Count);
        Assert.Equal(3, episodes.Select(static episode => episode.EpisodeKey).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AssembleIsolatesDocumentWhosePagesHaveConflictingPatients()
    {
        var document = Document(
            "conflict.pdf",
            "doc-conflict",
            Page(1, "PACIENTE UM", "01/01/1980", "REQ-1", "01/01/2026", "08:00:00"),
            Page(2, "PACIENTE DOIS", "02/02/1990", "REQ-1", "01/01/2026", "08:00:00"));

        var batch = PatientEpisodeAssembler.Assemble([document]);

        Assert.Empty(batch.Patients);
        var unassigned = Assert.Single(batch.UnassignedDocuments);
        Assert.Equal(2, unassigned.Pages.Count);
        Assert.Equal("Rejected", Assert.Single(batch.Ledger).Disposition);
        Assert.Equal(2, batch.Ledger[0].UnassignedPageCount);
        Assert.Contains("outro nome ou nascimento", unassigned.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AssembleDoesNotMergeDocumentsWithConflictingSex()
    {
        var batch = PatientEpisodeAssembler.Assemble(
        [
            Document("first.pdf", "doc-1", Page(1, "PACIENTE UM", "01/01/1980", "REQ-1", "01/01/2026", "08:00:00")),
            Document("second.pdf", "doc-2", Page(1, "PACIENTE UM", "01/01/1980", "REQ-2", "02/01/2026", "08:00:00", "Masculino")),
        ]);

        Assert.Single(batch.Patients);
        var unassigned = Assert.Single(batch.UnassignedDocuments);
        Assert.Equal("second.pdf", unassigned.FileName);
        Assert.Equal("Rejected", batch.Ledger.Single(entry => entry.FileName == "second.pdf").Disposition);
        Assert.Contains("sexo divergente", unassigned.Reason, StringComparison.Ordinal);
    }

    private static SanitizedDocument Document(
        string fileName,
        string documentId,
        params SanitizedPage[] pages) =>
        new("1.0", "1.1", documentId, fileName, pages);

    private static SanitizedPage Page(
        int pageNumber,
        string patient,
        string birthDate,
        string requestNumber,
        string requestDate,
        string requestTime,
        string sex = "Feminino") =>
        new(
            pageNumber,
            new SanitizedHeader(
                "Fundação",
                "Laboratório",
                patient,
                sex,
                birthDate,
                "46",
                "SOLICITANTE",
                "1",
                requestNumber,
                requestDate,
                requestTime,
                "UNIDADE",
                requestDate,
                requestTime,
                [],
                []),
            [
                new SanitizedLine(
                    $"p{pageNumber:D4}-l0010",
                    10,
                    $"RESULTADO: {pageNumber}",
                    $"RESULTADO: {pageNumber}",
                    SanitizedDisposition.Active,
                    [],
                    [$"p{pageNumber:D4}-w0010"],
                    []),
            ],
            true);

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
