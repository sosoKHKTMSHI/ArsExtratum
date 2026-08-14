using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ArsExtractum.App.Services;
using ArsExtractum.App.ViewModels;
using ArsExtractum.Core.Assembly;
using ArsExtractum.Core.DerivedMeasurements;
using ArsExtractum.Core.Documents;
using ArsExtractum.Core.LaboratorySemantic;
using ArsExtractum.Core.Pipeline;
using ArsExtractum.Core.OutputProjection;
using ArsExtractum.Core.Reconstruction;
using ArsExtractum.Core.Sanitization;
using ArsExtractum.PdfPig;
using Xunit;

namespace ArsExtractum.Tests;

public sealed class FullBundlePipelineAuditTests
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    [Fact]
    public async Task ProvidedFullBundlePreservesPagesAndPatientIsolation()
    {
        var configured = Environment.GetEnvironmentVariable("ARS_EXTRACTUM_FULL_BUNDLE_PDFS");
        if (string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        var paths = configured.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(24, paths.Length);

        var pipeline = new ProcessingPipeline(
        [
            new PdfPigCaptureStage(),
            new RawReconstructionStage(),
            new SanitizationStage(),
        ]);
        var viewModel = new MainWindowViewModel(pipeline);
        viewModel.AddFiles(paths);
        await viewModel.ProcessAsync();

        var batch = Assert.IsType<PatientBatch>(viewModel.PatientBatch);
        Assert.Equal(PatientEpisodeAssembler.CurrentSchemaVersion, batch.SchemaVersion);
        Assert.Equal(PatientEpisodeAssembler.CurrentRulesVersion, batch.RulesVersion);
        Assert.Equal(paths.Length, viewModel.CompletedRuns.Count);
        Assert.All(viewModel.CompletedRuns, static run => Assert.NotNull(run.Execution));
        Assert.Empty(batch.UnassignedDocuments);
        Assert.Equal(paths.Length, batch.Ledger.Count);
        Assert.All(batch.Ledger, static entry => Assert.Equal("Assigned", entry.Disposition));
        Assert.Equal(paths.Length, batch.Patients.Sum(static patient => patient.SourceDocuments.Count));
        Assert.Equal(11, batch.Patients.Count);
        Assert.Equal(298, batch.EpisodeCount);
        Assert.Equal(1858, batch.PageCount);
        var episodes = batch.Patients.SelectMany(static patient => patient.Episodes).ToArray();
        Assert.Equal(10109, episodes.Sum(static episode => episode.Coverage.SourceActiveLineCount));
        Assert.All(episodes, static episode =>
        {
            Assert.True(episode.Coverage.IsLossless);
            Assert.Equal(0, episode.Coverage.OrphanSourceCount);
            Assert.Equal(0, episode.Coverage.MultiplyAssignedSourceCount);
        });
        var semantic = Assert.IsType<SemanticPatientBatch>(viewModel.SemanticPatientBatch);
        Assert.Equal(11, semantic.Coverage.PatientCount);
        Assert.Equal(298, semantic.Coverage.EpisodeCount);
        Assert.Equal(1687, semantic.Coverage.CanonicalBlockCount);
        Assert.Equal(9008, semantic.Coverage.CanonicalActiveLineCount);
        Assert.Equal(9008, semantic.Coverage.OwnedActiveLineCount);
        Assert.Equal(0, semantic.Coverage.UnsupportedActiveLineCount);
        Assert.Equal(0, semantic.Coverage.MultiplyOwnedActiveLineCount);
        Assert.Equal(0, semantic.Coverage.RepresentationFailureCount);
        Assert.True(semantic.Coverage.IsLossless);
        Assert.Equal(
            batch.Patients.Select(static patient => patient.PatientKey),
            semantic.Patients.Select(static patient => patient.PatientKey));
        Assert.All(semantic.Patients, patient => Assert.Equal(
            batch.Patients.Single(item => item.PatientKey == patient.PatientKey)
                .Episodes.Select(static episode => episode.EpisodeKey),
            patient.Episodes.Select(static episode => episode.EpisodeKey)));
        Assert.DoesNotContain(viewModel.Stages, static stage =>
            stage.Id == "document.laboratory-entity-extraction");
        Assert.Equal(StageIds.OutputProjection, viewModel.Stages[^1].Id);
        semantic = Assert.IsType<SemanticPatientBatch>(viewModel.SemanticPatientBatch);
        Assert.Contains("Laboratoriais (", viewModel.OutputText, StringComparison.Ordinal);
        var derivedCoverage = Assert.IsType<DerivedMeasurementCoverage>(semantic.DerivedMeasurementCoverage);
        Assert.Equal(153, derivedCoverage.SourceCreatinineOccurrenceCount);
        Assert.Equal(153, derivedCoverage.DerivedRecordCount);
        Assert.Equal(125, derivedCoverage.ComputedCount);
        Assert.Equal(28, derivedCoverage.NotComputedCount);
        Assert.Equal(28, derivedCoverage.ReasonCodeCounts[nameof(DerivedMeasurementReasonCode.AgeBelow18)]);
        Assert.Equal(0, derivedCoverage.LabReportedEgfrInputUseCount);
        Assert.Equal(0, derivedCoverage.OrphanDerivedRecordCount);
        Assert.Equal(0, derivedCoverage.MultiplyMappedSourceOccurrenceCount);
        Assert.True(derivedCoverage.IsComplete);
        var derivedRecords = semantic.Patients.SelectMany(static patient => patient.Episodes)
            .SelectMany(static episode => episode.LaboratoryOccurrences)
            .SelectMany(static occurrence => occurrence.DerivedObservations).ToArray();
        Assert.Equal(153, derivedRecords.Select(static record => record.SourceOccurrenceId)
            .Distinct(StringComparer.Ordinal).Count());
        Assert.All(derivedRecords.Where(static record => record.Status == DerivedObservationStatus.Computed),
            static record =>
            {
                Assert.NotNull(record.NumericValue);
                Assert.True(double.IsFinite(record.NumericValue.Value));
                Assert.True(record.NumericValue.Value > 0d);
                Assert.Equal(DerivedMeasurementComputer.OutputUnit, record.Unit);
                Assert.Null(record.ReasonCode);
                Assert.NotNull(record.SourceObservationId);
                Assert.NotEmpty(record.Provenance.CandidateObservationEvidence);
                Assert.NotEmpty(record.Provenance.SpecimenEvidence);
                Assert.NotEmpty(record.Provenance.HeaderEvidence);
            });
        Assert.All(derivedRecords.Where(static record => record.Status == DerivedObservationStatus.NotComputed),
            static record => Assert.Equal(DerivedMeasurementReasonCode.AgeBelow18, record.ReasonCode));
        var clinicalOutput = Assert.IsType<ClinicalOutputBatch>(viewModel.ClinicalOutputBatch);
        Assert.Equal(OutputProjector.CurrentSchemaVersion, clinicalOutput.SchemaVersion);
        Assert.Equal(11, clinicalOutput.Coverage.SourcePatientCount);
        Assert.Equal(298, clinicalOutput.Coverage.SourceEpisodeCount);
        Assert.Equal(1943, clinicalOutput.Coverage.SourceOccurrenceCount);
        Assert.Equal(1943, clinicalOutput.Coverage.ProjectedOccurrenceCount +
            clinicalOutput.Coverage.SuppressedByExplicitPolicyCount +
            clinicalOutput.Coverage.SafeFallbackCount);
        Assert.Equal(77, clinicalOutput.Coverage.SuppressedByExplicitPolicyCount);
        Assert.Equal(0, clinicalOutput.Coverage.ProjectionFailureCount);
        Assert.Equal(0, clinicalOutput.Coverage.UnmappedOccurrenceCount);
        Assert.True(clinicalOutput.Coverage.IsComplete,
            string.Join(Environment.NewLine, clinicalOutput.Patients.SelectMany(static patient => patient.Episodes)
                .SelectMany(static episode => episode.ProjectedOccurrences)
                .SelectMany(static occurrence => occurrence.FieldProjectionRecords.Select(field =>
                    new { occurrence.SourceOccurrenceId, occurrence.ConceptId, occurrence.Lines, Field = field }))
                .Where(static item => item.Field.Disposition == FieldProjectionDisposition.ProjectionFailure)
                .Take(50)
                .Select(static item => $"{item.SourceOccurrenceId} | {item.ConceptId} | {item.Field.FieldKind} | {item.Field.FieldKey} | {item.Field.ReasonCode} | {string.Join(" / ", item.Lines)}")));
        Assert.False(clinicalOutput.Options.ShowUnits);
        Assert.False(clinicalOutput.Options.ShowCultures);
        Assert.True(clinicalOutput.Coverage.SourceFieldCount > 0);
        Assert.Equal(clinicalOutput.Coverage.SourceFieldCount, clinicalOutput.Coverage.AccountedFieldCount);
        Assert.Equal(0, clinicalOutput.Coverage.UnmappedFieldCount);
        Assert.Equal(0, clinicalOutput.Coverage.FieldProjectionFailureCount);
        var projectedFieldRecords = clinicalOutput.Patients.SelectMany(static patient => patient.Episodes)
            .SelectMany(static episode => episode.ProjectedOccurrences)
            .SelectMany(static occurrence => occurrence.FieldProjectionRecords)
            .Where(static field => field.Disposition == FieldProjectionDisposition.Projected)
            .ToArray();
        Assert.NotEmpty(projectedFieldRecords);
        Assert.All(projectedFieldRecords, static field =>
        {
            Assert.NotNull(field.OutputLineIndex);
            Assert.False(string.IsNullOrWhiteSpace(field.OutputFragment));
        });
        Assert.DoesNotContain(clinicalOutput.Patients.SelectMany(static patient => patient.Episodes)
                .SelectMany(static episode => episode.ProjectedOccurrences)
                .SelectMany(static occurrence => occurrence.FieldProjectionRecords),
            static field => field.ReasonCode == "not-selected-by-editorial-policy");
        var clinicalText = ClinicalOutputTextFormatter.Format(clinicalOutput);
        Assert.Contains("TFG ", clinicalText, StringComparison.Ordinal);
        Assert.DoesNotContain("TAXA DE FILTRAÇÃO GLOMERULAR ESTIMADA", clinicalText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cultura De Swab", clinicalText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TTPa 22,0", clinicalText, StringComparison.Ordinal);
        Assert.DoesNotContain("TTPa: Amostra", clinicalText, StringComparison.OrdinalIgnoreCase);
        var semanticOccurrences = semantic.Patients.SelectMany(static patient => patient.Episodes)
            .SelectMany(static episode => episode.LaboratoryOccurrences).ToArray();
        var ttpa = semanticOccurrences.Where(static occurrence => occurrence.ConceptId ==
            "fsph-nh.tempo-de-tromboplastina-parcial-ativada-ttpa").ToArray();
        Assert.Equal(25, ttpa.Length);
        Assert.All(ttpa, static occurrence =>
        {
            Assert.Contains(occurrence.Observations, static observation =>
                ReferenceLaboratoryCatalog.Normalize(observation.Label) == "AMOSTRA" &&
                observation.RawUnit == "segundos");
            Assert.DoesNotContain(occurrence.Specimens, static specimen =>
                specimen.RawSpecimen.Contains("segundos", StringComparison.OrdinalIgnoreCase));
        });
        var hemogramEpisodeCount = semantic.Patients.SelectMany(static patient => patient.Episodes)
            .Count(static episode => episode.LaboratoryOccurrences.Any(static occurrence =>
                occurrence.ConceptId == "fsph-nh.hemograma-completo"));
        var projectedHemogramEpisodeCount = clinicalOutput.Patients.SelectMany(static patient => patient.Episodes)
            .Count(static episode => episode.ProjectedOccurrences.Any(static occurrence =>
                occurrence.ConceptId == "fsph-nh.hemograma-completo") &&
                episode.EditableClinicalText.Split(Environment.NewLine)[0].Contains("Hb ", StringComparison.Ordinal));
        Assert.Equal(180, hemogramEpisodeCount);
        Assert.Equal(hemogramEpisodeCount, projectedHemogramEpisodeCount);
        var culturePatientCount = semantic.Patients.Count(static patient => patient.Episodes
            .SelectMany(static episode => episode.LaboratoryOccurrences).Any(OutputProjector.IsCultureOccurrence));
        Assert.Equal(culturePatientCount, clinicalOutput.Notices.Count(static notice =>
            notice.Code == "output-projection.culture-verification-required"));
        var cultureReview = CultureReviewTextFormatter.Format(semantic);
        Assert.Contains("Negativo para Acinetobacter sp. resistente aos", cultureReview, StringComparison.Ordinal);
        Assert.Contains("ALGUNS BACILOS GRAM NEGATIVOS", cultureReview, StringComparison.Ordinal);
        Assert.Contains("Solicitar antibiograma em ate 5 dias", cultureReview, StringComparison.Ordinal);
        var secondClinicalOutput = OutputProjector.Project(new OutputProjectionInput(semantic));
        var firstClinicalBytes = JsonSerializer.SerializeToUtf8Bytes(clinicalOutput);
        var secondClinicalBytes = JsonSerializer.SerializeToUtf8Bytes(secondClinicalOutput);
        var firstClinicalHash = Convert.ToHexString(SHA256.HashData(firstClinicalBytes));
        var secondClinicalHash = Convert.ToHexString(SHA256.HashData(secondClinicalBytes));
        Assert.Equal(firstClinicalHash, secondClinicalHash);
        var secondDerived = DerivedMeasurementComputer.Enrich(new DerivedMeasurementComputationInput(semantic));
        var firstDerivedBytes = JsonSerializer.SerializeToUtf8Bytes(semantic);
        var secondDerivedBytes = JsonSerializer.SerializeToUtf8Bytes(secondDerived);
        var firstDerivedHash = Convert.ToHexString(SHA256.HashData(firstDerivedBytes));
        var secondDerivedHash = Convert.ToHexString(SHA256.HashData(secondDerivedBytes));
        Assert.Equal(firstDerivedHash, secondDerivedHash);

        var capturePageCount = viewModel.CompletedRuns
            .SelectMany(static run => run.Execution!.Stages.Values)
            .Where(static stage => stage.Payload is CaptureDocument)
            .Sum(static stage => ((CaptureDocument)stage.Payload).PageCount);
        Assert.Equal(capturePageCount, batch.PageCount);
        viewModel.SelectedStage = viewModel.Stages.Single(static stage =>
            stage.Id == StageIds.PatientEpisodeAssembly);
        foreach (var patient in batch.Patients)
        {
            var chronology = patient.Episodes
                .Select(static episode => ParseDateTime(episode.RequestDate, episode.RequestTime))
                .ToArray();
            Assert.True(
                chronology.Zip(chronology.Skip(1)).All(static pair => pair.First >= pair.Second),
                $"A ordem de episódios do paciente {patient.PatientKey} não está em newest-first.");

            viewModel.SelectedPatient = viewModel.Patients.Single(item =>
                item.Patient.PatientKey == patient.PatientKey);
            var output = viewModel.OutputText;
            Assert.Contains("[CONTEÚDO]", output, StringComparison.Ordinal);
            Assert.All(
                patient.SourceDocuments,
                source => Assert.Contains(source.FileName, output, StringComparison.Ordinal));
            Assert.DoesNotContain(
                batch.Patients
                    .Where(other => other.PatientKey != patient.PatientKey)
                    .SelectMany(static other => other.SourceDocuments),
                source => output.Contains(source.FileName, StringComparison.Ordinal));
        }

        var reportPath = Environment.GetEnvironmentVariable("ARS_EXTRACTUM_FULL_BUNDLE_REPORT");
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
            await DetailedReportExporter.ExportAsync(reportPath, viewModel.CompletedRuns, batch);
        }

        var derivedOutputDirectory = Environment.GetEnvironmentVariable("ARS_EXTRACTUM_DERIVED_V1_OUTPUT");
        if (!string.IsNullOrWhiteSpace(derivedOutputDirectory))
        {
            Directory.CreateDirectory(derivedOutputDirectory);
            await File.WriteAllBytesAsync(
                Path.Combine(derivedOutputDirectory, "semantic-patient-batch-enriched.json"),
                firstDerivedBytes);
            await File.WriteAllTextAsync(
                Path.Combine(derivedOutputDirectory, "validation-summary.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = "semantic-patient-batch-enriched-validation/1.0",
                    firstEnrichedHash = firstDerivedHash,
                    secondEnrichedHash = secondDerivedHash,
                    deterministic = firstDerivedHash == secondDerivedHash,
                    sourceDocuments = paths.Length,
                    sourcePages = batch.PageCount,
                    semanticOccurrences = semantic.Coverage.OccurrenceCount,
                    DerivedMeasurementCoverage = semantic.DerivedMeasurementCoverage,
                }, IndentedJson));
        }

        var outputProjectionDirectory = Environment.GetEnvironmentVariable("ARS_EXTRACTUM_OUTPUT_PROJECTION_V1_OUTPUT");
        if (!string.IsNullOrWhiteSpace(outputProjectionDirectory))
        {
            Directory.CreateDirectory(outputProjectionDirectory);
            await File.WriteAllBytesAsync(
                Path.Combine(outputProjectionDirectory, "clinical-output-batch.json"),
                firstClinicalBytes);
            await File.WriteAllTextAsync(
                Path.Combine(outputProjectionDirectory, "clinical-output.txt"),
                clinicalText);
            await File.WriteAllTextAsync(
                Path.Combine(outputProjectionDirectory, "validation-summary.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = "output-projection-v1-validation/1.0",
                    firstClinicalHash,
                    secondClinicalHash,
                    deterministic = firstClinicalHash == secondClinicalHash,
                    sourceDocuments = paths.Length,
                    sourcePages = batch.PageCount,
                    semanticOccurrences = semantic.Coverage.OccurrenceCount,
                    clinicalOutput.Coverage,
                }, IndentedJson));
        }
    }

    private static DateTime ParseDateTime(string date, string time) =>
        DateTime.ParseExact(
            $"{date} {time}",
            "dd/MM/yyyy HH:mm:ss",
            CultureInfo.InvariantCulture);
}
