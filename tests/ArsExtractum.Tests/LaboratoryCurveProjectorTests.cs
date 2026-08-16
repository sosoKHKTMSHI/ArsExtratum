using ArsExtractum.App;
using ArsExtractum.App.ViewModels;
using ArsExtractum.Core.Assembly;
using ArsExtractum.Core.DerivedMeasurements;
using ArsExtractum.Core.Documents;
using ArsExtractum.Core.LaboratoryCurves;
using ArsExtractum.Core.LaboratorySemantic;
using System.Windows.Controls;
using Xunit;

namespace ArsExtractum.Tests;

public sealed class LaboratoryCurveProjectorTests
{
    [Fact]
    public void LaboratoryCurvesWindowOpensWithReadOnlyOutputBinding()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new LaboratoryCurvesWindow(Batch(), "patient-test", "PACIENTE");
                window.Show();
                window.UpdateLayout();
                Assert.True(((RadioButton)window.FindName("AllFilterRadio")).IsChecked);
                Assert.False(((DatePicker)window.FindName("StartDatePicker")).IsEnabled);
                Assert.False(((TextBox)window.FindName("LastDaysTextBox")).IsEnabled);
                Assert.True(((CheckBox)window.FindName("DeltaCheckBox")).IsEnabled);
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "A janela de curvas não concluiu sua abertura.");
        Assert.Null(failure);
    }

    [Fact]
    public void TemporalChoicesAreMutuallyExclusiveAndEnableOnlyTheirOwnInputs()
    {
        var viewModel = new LaboratoryCurvesViewModel(Batch(), "patient-test", "PACIENTE");

        Assert.True(viewModel.IsAllFilter);
        Assert.False(viewModel.UsesCustomRange);
        Assert.False(viewModel.UsesLastDays);

        viewModel.IsCustomRangeFilter = true;
        Assert.False(viewModel.IsAllFilter);
        Assert.True(viewModel.IsCustomRangeFilter);
        Assert.True(viewModel.UsesCustomRange);
        Assert.False(viewModel.UsesLastDays);

        viewModel.IsLastDaysFilter = true;
        Assert.False(viewModel.IsCustomRangeFilter);
        Assert.True(viewModel.IsLastDaysFilter);
        Assert.False(viewModel.UsesCustomRange);
        Assert.True(viewModel.UsesLastDays);
    }

    [Fact]
    public void ViewModelRejectsIncompleteRangeAndNonPositiveLastDays()
    {
        var batch = Batch(Episode("e1", "02/01/2026", "08:00:00",
            Scalar("e1", "fsph-nh.proteina-c-reativa", "PCR", "8,4", 8.4m, "mg/L")));
        var viewModel = new LaboratoryCurvesViewModel(batch, "patient-test", "PACIENTE");
        viewModel.Options.Single().IsSelected = true;

        viewModel.IsCustomRangeFilter = true;
        Assert.False(viewModel.Generate());
        Assert.Equal("Informe um intervalo de datas válido.", viewModel.ValidationMessage);

        viewModel.IsLastDaysFilter = true;
        viewModel.LastDaysText = "0";
        Assert.False(viewModel.Generate());
        Assert.Equal("Informe uma quantidade positiva de dias.", viewModel.ValidationMessage);
    }

    [Fact]
    public void CustomRangeCrossingYearUsesShortYearEvenWithPointsInOneYear()
    {
        var batch = Batch(Episode("e1", "02/01/2026", "08:00:00",
            Scalar("e1", "fsph-nh.sodio", "SÓDIO", "135", 135m, "mEq/L")));
        var filter = new LaboratoryCurveFilter(LaboratoryCurveFilterMode.CustomRange,
            new DateOnly(2025, 12, 31), new DateOnly(2026, 1, 2));

        var text = LaboratoryCurveTextFormatter.Format(Project(batch,
            [LaboratoryCurveDefinitions.Sodium], false, filter), false);

        Assert.Equal("Curvas:\r\n#Sódio (mEq/L): 02/01/26 - 135", text);
    }

    [Fact]
    public void GlobalPeriodUsesShortYearForEverySelectedSeries()
    {
        var batch = Batch(
            Episode("old", "31/12/2025", "08:00:00",
                Scalar("old", "fsph-nh.creatinina", "CREATININA", "1,00", 1m, "mg/dL")),
            Episode("new", "02/01/2026", "08:00:00",
                Scalar("new", "fsph-nh.creatinina", "CREATININA", "1,10", 1.1m, "mg/dL"),
                Scalar("new", "fsph-nh.proteina-c-reativa", "PCR", "8,40", 8.4m, "mg/L")));

        var text = LaboratoryCurveTextFormatter.Format(Project(batch,
            [LaboratoryCurveDefinitions.Creatinine, LaboratoryCurveDefinitions.CReactiveProtein], false), false);

        Assert.Contains("#Creatinina (mg/dL): 31/12/25 - 1 | 02/01/26 - 1,1", text, StringComparison.Ordinal);
        Assert.Contains("#PCR (mg/L): 02/01/26 - 8,4", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CompactScalarsRemoveOnlyInsignificantZerosAndDeltaKeepsMeaningfulPrecision()
    {
        var batch = Batch(
            Episode("e1", "01/01/2026", "08:00:00",
                Scalar("e1", "fsph-nh.transaminase-glutamico-oxalacetica-tgo", "TGO", "20,00", 20m, "U/L"),
                Scalar("e1", "fsph-nh.creatinina", "CREATININA", "1,01", 1.01m, "mg/dL")),
            Episode("e2", "02/01/2026", "08:00:00",
                Scalar("e2", "fsph-nh.transaminase-glutamico-oxalacetica-tgo", "TGO", "29,10", 29.1m, "U/L"),
                Scalar("e2", "fsph-nh.creatinina", "CREATININA", "0,97", 0.97m, "mg/dL")));

        var text = LaboratoryCurveTextFormatter.Format(Project(batch,
            [LaboratoryCurveDefinitions.Ast, LaboratoryCurveDefinitions.Creatinine], true), true);

        Assert.Contains("#TGO (U/L): 01/01 - 20 | 02/01 - 29,1 (+9,1)", text, StringComparison.Ordinal);
        Assert.Contains("#Creatinina (mg/dL): 01/01 - 1,01 | 02/01 - 0,97 (-0,04)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ScalarSeriesOrdersPointsFiltersDatesAndFormatsDelta()
    {
        var batch = Batch(
            Episode("e3", "26/07/2026", "12:00:00", Scalar("e3", "fsph-nh.creatinina", "CREATININA", "1,20", 1.20m, "mg/dL")),
            Episode("e1", "18/07/2026", "12:00:00", Scalar("e1", "fsph-nh.creatinina", "CREATININA", "1,70", 1.70m, "mg/dL")),
            Episode("e2", "24/07/2026", "12:00:00", Scalar("e2", "fsph-nh.creatinina", "CREATININA", "0,90", 0.90m, "mg/dL")));

        var projection = Project(batch, [LaboratoryCurveDefinitions.Creatinine], true,
            new LaboratoryCurveFilter(LaboratoryCurveFilterMode.CustomRange,
                new DateOnly(2026, 7, 18), new DateOnly(2026, 7, 26)));
        var text = LaboratoryCurveTextFormatter.Format(projection, true);

        Assert.Equal("Curvas:\r\n#Creatinina (mg/dL): 18/07 - 1,7 | 24/07 - 0,9 (-0,8) | 26/07 - 1,2 (+0,3)", text);
    }

    [Fact]
    public void LeukogramFractionsAreConsolidatedTruncatedAndNeverReceiveDelta()
    {
        var first = Panel("e1", "fsph-nh.hemograma-completo",
            Observation("leuco-1", "LEUCÓCITOS", "14.020", 14020m, "/mm3"),
            Observation("neut-1", "SEGMENTADOS (NEUTRÓFILOS)", "77,68", 77.68m, "%"),
            Observation("linf-1", "LINFÓCITOS", "9,79", 9.79m, "%"),
            Observation("mielo-1", "MIELÓCITOS", "2,99", 2.99m, "%"));
        var second = Panel("e2", "fsph-nh.hemograma-completo",
            Observation("leuco-2", "LEUCÓCITOS", "10.820", 10820m, "/mm3"),
            Observation("neut-2", "NEUTRÓFILOS", "70,04", 70.04m, "%"),
            Observation("bast-2", "BASTONETES", "3,00", 3m, "%"));
        var batch = Batch(Episode("e1", "18/07/2026", "08:00:00", first),
            Episode("e2", "20/07/2026", "08:00:00", second));

        var projection = Project(batch, [LaboratoryCurveDefinitions.LeukogramFractions], true);
        var text = LaboratoryCurveTextFormatter.Format(projection, true);

        Assert.Equal("Curvas:\r\n#Leucograma (/mm³): 18/07 - Leuco 14.020 (N 77,6% | L 9,7% | Mielócitos 2,9%) | 20/07 - Leuco 10.820 (N 70,0% | B 3,0%)", text);
        Assert.DoesNotContain("(+", text, StringComparison.Ordinal);
        Assert.DoesNotContain("(-", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BilirubinsSupportIndependentDeltasAndCompositeWithoutDelta()
    {
        LaboratoryOccurrence Bilirubins(string episode, string bt, decimal btNumeric, string bd, decimal bdNumeric) =>
            Panel(episode, "fsph-nh.bilirrubina-direta",
                Observation($"bt-{episode}", "BILIRRUBINA TOTAL", bt, btNumeric, "mg/dL"),
                Observation($"bd-{episode}", "BILIRRUBINA DIRETA", bd, bdNumeric, "mg/dL"));
        var batch = Batch(
            Episode("e1", "18/07/2026", "08:00:00", Bilirubins("e1", "3,80", 3.8m, "2,60", 2.6m)),
            Episode("e2", "20/07/2026", "08:00:00", Bilirubins("e2", "2,90", 2.9m, "1,80", 1.8m)));

        var projection = Project(batch,
            [LaboratoryCurveDefinitions.BilirubinsIsolated, LaboratoryCurveDefinitions.BilirubinsFractions], true);
        var text = LaboratoryCurveTextFormatter.Format(projection, true);

        Assert.Contains("#BT (mg/dL): 18/07 - 3,8 | 20/07 - 2,9 (-0,9)", text, StringComparison.Ordinal);
        Assert.Contains("#BD (mg/dL): 18/07 - 2,6 | 20/07 - 1,8 (-0,8)", text, StringComparison.Ordinal);
        Assert.Contains("#Bilirrubinas (mg/dL): 18/07 - (BT 3,8 | BD 2,6) | 20/07 - (BT 2,9 | BD 1,8)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("BI", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DynamicOptionsRemainInsideClosedPolicyAndViewModelValidatesSelection()
    {
        var batch = Batch(Episode("e1", "01/01/2026", "08:00:00",
            Scalar("e1", "fsph-nh.proteina-c-reativa", "PCR", "8,4", 8.4m, "mg/L"),
            Scalar("e1", "fsph-nh.tempo-de-protrombina-tp", "ATIVIDADE", "86,0", 86m, "%")));
        var options = LaboratoryCurveProjector.AvailableOptions(batch, "patient-test");
        var viewModel = new LaboratoryCurvesViewModel(batch, "patient-test", "PACIENTE", new DateOnly(2026, 1, 1));

        Assert.Single(options);
        Assert.Equal(LaboratoryCurveDefinitions.CReactiveProtein, options[0].Key);
        Assert.False(viewModel.Generate());
        Assert.Equal("Selecione ao menos uma curva.", viewModel.ValidationMessage);
        viewModel.Options.Single().IsSelected = true;
        Assert.True(viewModel.Generate());
        Assert.Contains("#PCR (mg/L): 01/01 - 8,4", viewModel.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public void LastDaysUsesInjectedCurrentDateAndCrossYearOutputShowsYear()
    {
        var batch = Batch(
            Episode("e1", "31/12/2025", "08:00:00", Scalar("e1", "fsph-nh.sodio", "SÓDIO", "132", 132m, "mEq/L")),
            Episode("e2", "02/01/2026", "08:00:00", Scalar("e2", "fsph-nh.sodio", "SÓDIO", "135", 135m, "mEq/L")),
            Episode("future", "05/01/2026", "08:00:00", Scalar("future", "fsph-nh.sodio", "SÓDIO", "139", 139m, "mEq/L")));

        var projection = LaboratoryCurveProjector.Project(new LaboratoryCurveProjectionInput(batch, "patient-test",
            [LaboratoryCurveDefinitions.Sodium], new LaboratoryCurveFilter(LaboratoryCurveFilterMode.LastDays, LastDays: 4),
            false, new DateOnly(2026, 1, 2)));
        var text = LaboratoryCurveTextFormatter.Format(projection, false);

        Assert.Contains("31/12/25 - 132 | 02/01/26 - 135", text, StringComparison.Ordinal);
        Assert.DoesNotContain("139", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CensoredNumericResultIsNotUsedAsAnExactCurvePoint()
    {
        var batch = Batch(
            Episode("e1", "01/01/2026", "08:00:00", Scalar("e1", "fsph-nh.proteina-c-reativa", "PCR", "<5,00", 5m, "mg/L")),
            Episode("e2", "02/01/2026", "08:00:00", Scalar("e2", "fsph-nh.proteina-c-reativa", "PCR", "7,50", 7.5m, "mg/L")));

        var text = LaboratoryCurveTextFormatter.Format(Project(batch,
            [LaboratoryCurveDefinitions.CReactiveProtein], true), true);

        Assert.Equal("Curvas:\r\n#PCR (mg/L): 02/01 - 7,5", text);
        Assert.DoesNotContain("delta", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingUnitIsOmittedAndRepeatedDayReceivesTime()
    {
        var batch = Batch(
            Episode("missing", "01/01/2026", "07:00:00", Scalar("missing", "fsph-nh.sodio", "SÓDIO", "130", 130m, "")),
            Episode("morning", "02/01/2026", "08:15:00", Scalar("morning", "fsph-nh.sodio", "SÓDIO", "132", 132m, "mEq/L")),
            Episode("evening", "02/01/2026", "18:30:00", Scalar("evening", "fsph-nh.sodio", "SÓDIO", "135", 135m, "mEq/L")));

        var text = LaboratoryCurveTextFormatter.Format(Project(batch, [LaboratoryCurveDefinitions.Sodium], false), false);

        Assert.Equal("Curvas:\r\n#Sódio (mEq/L): 02/01 08:15 - 132 | 02/01 18:30 - 135", text);
        Assert.DoesNotContain("130", text, StringComparison.Ordinal);
    }

    [Fact]
    public void EgfrUsesDisplayedTruncatedValuesForItsDelta()
    {
        var first = CreatinineWithDerived("first", DerivedObservationStatus.Computed, 66.69);
        var second = CreatinineWithDerived("second", DerivedObservationStatus.Computed, 57.69);
        var third = CreatinineWithDerived("third", DerivedObservationStatus.Computed, 60.59);
        var unavailable = CreatinineWithDerived("unavailable", DerivedObservationStatus.NotComputed, null);
        var batch = Batch(
            Episode("first", "01/01/2026", "08:00:00", first),
            Episode("second", "02/01/2026", "08:00:00", second),
            Episode("third", "03/01/2026", "08:00:00", third),
            Episode("unavailable", "04/01/2026", "08:00:00", unavailable));

        var text = LaboratoryCurveTextFormatter.Format(Project(batch, [LaboratoryCurveDefinitions.Egfr], true), true);

        Assert.Equal("Curvas:\r\n#TFG (mL/min/1,73m²): 01/01 - 66,6 | 02/01 - 57,6 (-9,0) | 03/01 - 60,5 (+2,9)", text);
    }

    private static LaboratoryCurveProjection Project(
        SemanticPatientBatch batch,
        IReadOnlyCollection<string> options,
        bool delta,
        LaboratoryCurveFilter? filter = null) =>
        LaboratoryCurveProjector.Project(new LaboratoryCurveProjectionInput(batch, "patient-test", options,
            filter ?? new LaboratoryCurveFilter(LaboratoryCurveFilterMode.All), delta, new DateOnly(2026, 8, 16)));

    private static SemanticPatientBatch Batch(params SemanticEpisode[] episodes)
    {
        var occurrenceCount = episodes.Sum(static episode => episode.LaboratoryOccurrences.Count);
        return new SemanticPatientBatch(LaboratorySemanticExtractor.CurrentSchemaVersion,
            LaboratorySemanticExtractor.CurrentRulesVersion, "1.0.0", "fixture",
            [new SemanticPatient("patient-test", new PatientIdentity("PACIENTE", "01/01/1970", "Feminino"), [], episodes)],
            new LaboratorySemanticCoverage(1, episodes.Length, occurrenceCount, occurrenceCount, occurrenceCount,
                occurrenceCount, 0, 0, 0, occurrenceCount, occurrenceCount), [])
        {
            DerivedMeasurementRulesVersion = DerivedMeasurementComputer.CurrentRulesVersion,
            DerivedMeasurementCoverage = new DerivedMeasurementCoverage(0, 0, 0, 0,
                new Dictionary<string, int>(), 0, 0, 0),
        };
    }

    private static SemanticEpisode Episode(
        string key,
        string date,
        string time,
        params LaboratoryOccurrence[] occurrences)
    {
        var documentary = new AssembledEpisode(key, "REQ", date, time,
            new EpisodeAgeAtRequest(56, "Computed", null),
            new EpisodeAssemblyCoverage(1, 1, 0, occurrences.Length, occurrences.Length, occurrences.Length,
                0, 0, 0, 0, true), [], [], []);
        return new SemanticEpisode(key, documentary, occurrences, [],
            new LaboratorySemanticEpisodeCoverage(occurrences.Length, occurrences.Length, occurrences.Length,
                occurrences.Length, 0, 0, 0, occurrences.Length, occurrences.Length));
    }

    private static LaboratoryOccurrence Scalar(
        string episode,
        string concept,
        string label,
        string raw,
        decimal numeric,
        string unit) => Panel(episode, concept, Observation($"{concept}-{episode}", label, raw, numeric, unit));

    private static LaboratoryOccurrence Panel(
        string episode,
        string concept,
        params LaboratoryObservation[] observations) =>
        new($"occurrence-{concept}-{episode}", episode, concept, concept, observations.Length == 1 ? "scalar" : "panel",
            LaboratoryRepresentationStatus.FullyStructured, observations, [], [], [], [], [], null, [],
            observations.Select(static observation => observation.Evidence).ToArray(), [], []);

    private static LaboratoryOccurrence CreatinineWithDerived(
        string episode,
        DerivedObservationStatus status,
        double? value)
    {
        var occurrence = Scalar(episode, "fsph-nh.creatinina", "RESULTADO", "1,00", 1m, "mg/dL");
        var derived = new DerivedObservation($"derived-{episode}", "patient-test", episode,
            occurrence.OccurrenceId, occurrence.Observations[0].ObservationId,
            DerivedMeasurementComputer.DerivedConceptId, DerivedObservationKind.Derived,
            DerivedMeasurementComputer.MethodId, status, value,
            value is null ? null : DerivedMeasurementComputer.OutputUnit,
            value is null ? DerivedMeasurementReasonCode.AgeBelow18 : null,
            new DerivedMeasurementInputs("01/01/1970", "01/01/2026", 56, "Feminino", "female",
                "1,00", 1m, "mg/dL", "mg/dL", "SORO"),
            new DerivedMeasurementProvenance("patient-test", episode, occurrence.OccurrenceId,
                [occurrence.Observations[0].ObservationId], [occurrence.Observations[0].Evidence], [], []), []);
        return occurrence with { DerivedObservations = [derived] };
    }

    private static LaboratoryObservation Observation(
        string id,
        string label,
        string raw,
        decimal numeric,
        string unit) =>
        new(id, label, raw, numeric, null, unit, unit,
            new SemanticFieldEvidence("observations", "block", "line", raw, raw, [], [], [], []));
}
