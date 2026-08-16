using System.Globalization;
using ArsExtractum.Core.DerivedMeasurements;
using ArsExtractum.Core.LaboratorySemantic;

namespace ArsExtractum.Core.LaboratoryCurves;

public static class LaboratoryCurveProjector
{
    private const string HemogramConcept = "fsph-nh.hemograma-completo";
    private const string HemoglobinConcept = "fsph-nh.dosagem-de-hemoglobina";
    private const string PlateletsConcept = "fsph-nh.plaquetas";
    private const string BilirubinConcept = "fsph-nh.bilirrubina-direta";

    private static readonly IReadOnlyDictionary<string, ScalarDefinition> ScalarDefinitions =
        new Dictionary<string, ScalarDefinition>(StringComparer.Ordinal)
        {
            [LaboratoryCurveDefinitions.CReactiveProtein] = new("PCR", "fsph-nh.proteina-c-reativa", null, "mg/L"),
            [LaboratoryCurveDefinitions.Ast] = new("TGO", "fsph-nh.transaminase-glutamico-oxalacetica-tgo", null, "U/L"),
            [LaboratoryCurveDefinitions.Alt] = new("TGP", "fsph-nh.transaminase-glutamico-piruvica-tgp", null, "U/L"),
            [LaboratoryCurveDefinitions.Amylase] = new("Amilase", "fsph-nh.amilase", null, "U/L"),
            [LaboratoryCurveDefinitions.Lipase] = new("Lipase", "fsph-nh.lipase", null, "U/L"),
            [LaboratoryCurveDefinitions.Creatinine] = new("Creatinina", "fsph-nh.creatinina", null, "mg/dL"),
            [LaboratoryCurveDefinitions.Urea] = new("Ureia", "fsph-nh.ureia", null, "mg/dL"),
            [LaboratoryCurveDefinitions.Sodium] = new("Sódio", "fsph-nh.sodio", null, "mEq/L"),
            [LaboratoryCurveDefinitions.Potassium] = new("Potássio", "fsph-nh.potassio", null, "mEq/L"),
        };

    public static IReadOnlyList<LaboratoryCurveOption> AvailableOptions(SemanticPatientBatch batch, string patientKey)
    {
        var patient = FindPatient(batch, patientKey);
        var all = ProjectAll(patient, new LaboratoryCurveFilter(LaboratoryCurveFilterMode.All), DateOnly.MaxValue);
        return LaboratoryCurveDefinitions.Options
            .Where(option => all.Any(series => OptionOwnsSeries(option.Key, series.Key) && series.Points.Count > 0))
            .OrderBy(static option => option.Order)
            .ToArray();
    }

    public static LaboratoryCurveProjection Project(LaboratoryCurveProjectionInput input)
    {
        ValidateFilter(input.Filter);
        var patient = FindPatient(input.Batch, input.PatientKey);
        var selected = input.SelectedOptionKeys.ToHashSet(StringComparer.Ordinal);
        if (selected.Count == 0)
        {
            return new LaboratoryCurveProjection(input.PatientKey, [], false);
        }

        var unknown = selected.Except(LaboratoryCurveDefinitions.Options.Select(static option => option.Key),
            StringComparer.Ordinal).ToArray();
        if (unknown.Length > 0)
        {
            throw new ArgumentException($"Opção de curva desconhecida: {unknown[0]}.", nameof(input));
        }

        var series = ProjectAll(patient, input.Filter, input.CurrentDate)
            .Where(item => selected.Any(key => OptionOwnsSeries(key, item.Key)))
            .OrderBy(static item => item.Order)
            .ThenBy(static item => item.Key, StringComparer.Ordinal)
            .ToArray();
        return new LaboratoryCurveProjection(input.PatientKey, series,
            ShouldIncludeYear(input.Filter, input.CurrentDate, series));
    }

    private static List<LaboratoryCurveSeries> ProjectAll(
        SemanticPatient patient,
        LaboratoryCurveFilter filter,
        DateOnly currentDate)
    {
        var episodes = patient.Episodes
            .Select(episode => new EpisodeContext(episode, ParseTimestamp(episode)))
            .Where(static item => item.Timestamp is not null)
            .Where(item => Included(DateOnly.FromDateTime(item.Timestamp!.Value), filter, currentDate))
            .Select(static item => item with { Timestamp = item.Timestamp!.Value })
            .ToArray();
        var result = new List<LaboratoryCurveSeries>();

        AddScalar(result, episodes, LaboratoryCurveDefinitions.Hemoglobin, "Hemoglobina", "g/dL", 10,
            occurrence => occurrence.ConceptId is HemogramConcept or HemoglobinConcept,
            observation => LabelIs(observation, "HEMOGLOBINA"));
        AddScalar(result, episodes, LaboratoryCurveDefinitions.Platelets, "Plaquetas", "/mm³", 20,
            occurrence => occurrence.ConceptId is HemogramConcept or PlateletsConcept,
            observation => LabelIs(observation, "PLAQUETAS"));
        AddScalar(result, episodes, LaboratoryCurveDefinitions.Leukocytes, "Leuco", "/mm³", 30,
            occurrence => occurrence.ConceptId == HemogramConcept,
            observation => LabelIs(observation, "LEUCOCITOS"));
        AddLeukogram(result, episodes);

        foreach (var pair in ScalarDefinitions)
        {
            var option = LaboratoryCurveDefinitions.ByKey(pair.Key);
            var definition = pair.Value;
            AddScalar(result, episodes, pair.Key, definition.Label, definition.Unit, option.Order,
                occurrence => occurrence.ConceptId == definition.ConceptId,
                observation => definition.ObservationLabel is null || LabelIs(observation, definition.ObservationLabel));
        }

        AddBilirubins(result, episodes);
        AddEgfr(result, episodes);
        return result;
    }

    private static void AddScalar(
        ICollection<LaboratoryCurveSeries> output,
        IReadOnlyList<EpisodeContext> episodes,
        string key,
        string label,
        string unit,
        int order,
        Func<LaboratoryOccurrence, bool> occurrenceMatch,
        Func<LaboratoryObservation, bool> observationMatch)
    {
        var points = new List<LaboratoryCurvePoint>();
        foreach (var context in episodes)
        {
            foreach (var occurrence in context.Episode.LaboratoryOccurrences.Where(occurrenceMatch))
            {
                var candidates = occurrence.Observations
                    .Where(observation => observation.NumericValue is not null && IsExactNumeric(observation) &&
                                          observationMatch(observation) &&
                                          UnitMatches(observation, unit))
                    .ToArray();
                if (candidates.Length != 1)
                {
                    continue;
                }

                var observation = candidates[0];
                points.Add(Point(context, observation.ObservationId,
                    new LaboratoryCurveValue(key, label, observation.NumericValue!.Value,
                        DisplayCompact(observation.NumericValue.Value), unit)));
            }
        }

        AddIfAny(output, key, label, unit, true, order, points);
    }

    private static void AddLeukogram(ICollection<LaboratoryCurveSeries> output, IReadOnlyList<EpisodeContext> episodes)
    {
        var points = new List<LaboratoryCurvePoint>();
        foreach (var context in episodes)
        {
            foreach (var occurrence in context.Episode.LaboratoryOccurrences.Where(static occurrence =>
                         occurrence.ConceptId == HemogramConcept))
            {
                var leukocytes = SingleObservation(occurrence, "LEUCOCITOS", "/mm³");
                if (leukocytes is null)
                {
                    continue;
                }

                var values = new List<LaboratoryCurveValue>
                {
                    new("leukocytes", "Leuco", leukocytes.NumericValue!.Value,
                        DisplayCompact(leukocytes.NumericValue.Value), "/mm³"),
                };
                AddPercentage(values, occurrence, "N", "SEGMENTADOS (NEUTROFILOS)", "NEUTROFILOS");
                AddPercentage(values, occurrence, "L", "LINFOCITOS");
                AddPercentage(values, occurrence, "B", "BASTONETES");
                AddPercentage(values, occurrence, "Mielócitos", "MIELOCITOS");
                AddPercentage(values, occurrence, "Metamielócitos", "METAMIELOCITOS");
                AddPercentage(values, occurrence, "Blastos", "BLASTOS");
                if (values.Count < 2)
                {
                    continue;
                }

                points.Add(new LaboratoryCurvePoint(context.Timestamp!.Value, values,
                    context.Episode.EpisodeKey, leukocytes.ObservationId));
            }
        }

        AddIfAny(output, LaboratoryCurveDefinitions.LeukogramFractions, "Leucograma", "/mm³", false, 40, points);
    }

    private static void AddPercentage(
        List<LaboratoryCurveValue> values,
        LaboratoryOccurrence occurrence,
        string outputLabel,
        params string[] labels)
    {
        var matches = occurrence.Observations.Where(observation => observation.NumericValue is not null &&
            string.Equals(NormalizeUnit(observation.NormalizedUnit ?? observation.RawUnit), "%", StringComparison.Ordinal) &&
            labels.Any(label => LabelIs(observation, label))).ToArray();
        if (matches.Length != 1)
        {
            return;
        }

        var observation = matches[0];
        var truncated = decimal.Truncate(observation.NumericValue!.Value * 10m) / 10m;
        values.Add(new LaboratoryCurveValue(NormalizeLabel(outputLabel), outputLabel, truncated,
            truncated.ToString("0.0", CultureInfo.GetCultureInfo("pt-BR")), "%"));
    }

    private static void AddBilirubins(ICollection<LaboratoryCurveSeries> output, IReadOnlyList<EpisodeContext> episodes)
    {
        var isolated = new Dictionary<string, List<LaboratoryCurvePoint>>(StringComparer.Ordinal)
        {
            ["bt"] = [],
            ["bd"] = [],
            ["bi"] = [],
        };
        var combined = new List<LaboratoryCurvePoint>();
        foreach (var context in episodes)
        {
            foreach (var occurrence in context.Episode.LaboratoryOccurrences.Where(static occurrence =>
                         occurrence.ConceptId == BilirubinConcept))
            {
                var values = new List<LaboratoryCurveValue>();
                AddBilirubin(values, occurrence, "bt", "BT", "BILIRRUBINA TOTAL");
                AddBilirubin(values, occurrence, "bd", "BD", "BILIRRUBINA DIRETA");
                AddBilirubin(values, occurrence, "bi", "BI", "BILIRRUBINA INDIRETA");
                foreach (var value in values)
                {
                    isolated[value.Key].Add(Point(context, FindObservationId(occurrence, value.Label), value));
                }

                if (values.Count > 0)
                {
                    combined.Add(new LaboratoryCurvePoint(context.Timestamp!.Value, values,
                        context.Episode.EpisodeKey, FindObservationId(occurrence, values[0].Label)));
                }
            }
        }

        AddIfAny(output, "bilirubin-total", "BT", "mg/dL", true, 101, isolated["bt"]);
        AddIfAny(output, "bilirubin-direct", "BD", "mg/dL", true, 102, isolated["bd"]);
        AddIfAny(output, "bilirubin-indirect", "BI", "mg/dL", true, 103, isolated["bi"]);
        AddIfAny(output, LaboratoryCurveDefinitions.BilirubinsFractions, "Bilirrubinas", "mg/dL", false, 110, combined);
    }

    private static void AddBilirubin(
        List<LaboratoryCurveValue> values,
        LaboratoryOccurrence occurrence,
        string key,
        string label,
        string sourceLabel)
    {
        var observation = SingleObservation(occurrence, sourceLabel, "mg/dL");
        if (observation is not null)
        {
            values.Add(new LaboratoryCurveValue(key, label, observation.NumericValue!.Value,
                DisplayCompact(observation.NumericValue.Value), "mg/dL"));
        }
    }

    private static void AddEgfr(ICollection<LaboratoryCurveSeries> output, IReadOnlyList<EpisodeContext> episodes)
    {
        var points = new List<LaboratoryCurvePoint>();
        foreach (var context in episodes)
        {
            foreach (var occurrence in context.Episode.LaboratoryOccurrences.Where(static occurrence =>
                         occurrence.ConceptId == "fsph-nh.creatinina"))
            {
                var computed = occurrence.DerivedObservations.Where(static observation =>
                    observation.Status == DerivedObservationStatus.Computed &&
                    observation.NumericValue is not null &&
                    observation.ConceptId == DerivedMeasurementComputer.DerivedConceptId).ToArray();
                if (computed.Length != 1)
                {
                    continue;
                }

                var observation = computed[0];
                var numeric = (decimal)observation.NumericValue!.Value;
                var truncated = decimal.Truncate(numeric * 10m) / 10m;
                // The projected numeric value must equal the displayed value so its delta is auditable by subtraction.
                points.Add(Point(context, observation.DerivedObservationId,
                    new LaboratoryCurveValue(LaboratoryCurveDefinitions.Egfr, "TFG", truncated,
                        truncated.ToString("0.0", CultureInfo.GetCultureInfo("pt-BR")),
                        "mL/min/1,73m²", 1)));
            }
        }

        AddIfAny(output, LaboratoryCurveDefinitions.Egfr, "TFG", "mL/min/1,73m²", true, 130, points);
    }

    private static LaboratoryObservation? SingleObservation(
        LaboratoryOccurrence occurrence,
        string label,
        string unit)
    {
        var values = occurrence.Observations.Where(observation => observation.NumericValue is not null &&
            IsExactNumeric(observation) && LabelIs(observation, label) && UnitMatches(observation, unit)).ToArray();
        return values.Length == 1 ? values[0] : null;
    }

    private static LaboratoryCurvePoint Point(
        EpisodeContext context,
        string fieldId,
        LaboratoryCurveValue value) =>
        new(context.Timestamp!.Value, [value], context.Episode.EpisodeKey, fieldId);

    private static void AddIfAny(
        ICollection<LaboratoryCurveSeries> output,
        string key,
        string label,
        string unit,
        bool supportsDelta,
        int order,
        IEnumerable<LaboratoryCurvePoint> points)
    {
        var ordered = points.OrderBy(static point => point.Timestamp)
            .ThenBy(static point => point.EpisodeKey, StringComparer.Ordinal)
            .ThenBy(static point => point.SourceFieldId, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length > 0)
        {
            output.Add(new LaboratoryCurveSeries(key, label, unit, supportsDelta, order, ordered));
        }
    }

    private static bool OptionOwnsSeries(string optionKey, string seriesKey) =>
        optionKey == LaboratoryCurveDefinitions.BilirubinsIsolated
            ? seriesKey is "bilirubin-total" or "bilirubin-direct" or "bilirubin-indirect"
            : optionKey == seriesKey;

    private static SemanticPatient FindPatient(SemanticPatientBatch batch, string patientKey) =>
        batch.Patients.SingleOrDefault(patient => patient.PatientKey == patientKey)
        ?? throw new ArgumentException("Paciente não encontrado no lote semântico.", nameof(patientKey));

    private static DateTime? ParseTimestamp(SemanticEpisode episode) =>
        DateTime.TryParseExact(
            $"{episode.DocumentaryEpisode.RequestDate} {episode.DocumentaryEpisode.RequestTime}",
            "dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? value
            : null;

    private static bool Included(DateOnly date, LaboratoryCurveFilter filter, DateOnly currentDate) => filter.Mode switch
    {
        LaboratoryCurveFilterMode.All => true,
        LaboratoryCurveFilterMode.CustomRange => date >= filter.StartDate && date <= filter.EndDate,
        LaboratoryCurveFilterMode.LastDays => date <= currentDate &&
                                              date >= currentDate.AddDays(1 - filter.LastDays!.Value),
        _ => false,
    };

    private static bool ShouldIncludeYear(
        LaboratoryCurveFilter filter,
        DateOnly currentDate,
        IReadOnlyCollection<LaboratoryCurveSeries> series) => filter.Mode switch
    {
        LaboratoryCurveFilterMode.CustomRange => filter.StartDate!.Value.Year != filter.EndDate!.Value.Year,
        LaboratoryCurveFilterMode.LastDays => currentDate.AddDays(1 - filter.LastDays!.Value).Year != currentDate.Year,
        _ => series.SelectMany(static item => item.Points)
            .Select(static point => point.Timestamp.Year)
            .Distinct()
            .Skip(1)
            .Any(),
    };

    private static void ValidateFilter(LaboratoryCurveFilter filter)
    {
        if (filter.Mode == LaboratoryCurveFilterMode.CustomRange &&
            (filter.StartDate is null || filter.EndDate is null || filter.StartDate > filter.EndDate))
        {
            throw new ArgumentException("O intervalo personalizado é inválido.", nameof(filter));
        }

        if (filter.Mode == LaboratoryCurveFilterMode.LastDays && filter.LastDays is null or <= 0)
        {
            throw new ArgumentException("A quantidade de dias deve ser um número inteiro positivo.", nameof(filter));
        }
    }

    private static string FindObservationId(LaboratoryOccurrence occurrence, string outputLabel)
    {
        var source = outputLabel switch
        {
            "BT" => "BILIRRUBINA TOTAL",
            "BD" => "BILIRRUBINA DIRETA",
            "BI" => "BILIRRUBINA INDIRETA",
            _ => outputLabel,
        };
        return occurrence.Observations.First(observation => LabelIs(observation, source)).ObservationId;
    }

    private static bool LabelIs(LaboratoryObservation observation, string label) =>
        NormalizeLabel(observation.Label) == NormalizeLabel(label) ||
        NormalizeLabel(label) == "LEUCOCITOS" &&
        NormalizeLabel(observation.Label).StartsWith("LEUCOCITOS P MM3", StringComparison.Ordinal);

    private static string NormalizeLabel(string value) => ReferenceLaboratoryCatalog.Normalize(value);

    private static bool UnitMatches(LaboratoryObservation observation, string canonicalUnit) =>
        NormalizeUnit(observation.NormalizedUnit ?? observation.RawUnit) == NormalizeUnit(canonicalUnit) ||
        NormalizeUnit(canonicalUnit) == "/MM3" &&
        NormalizeLabel(observation.Label).Contains("P MM3", StringComparison.Ordinal);

    private static string NormalizeUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit))
        {
            return string.Empty;
        }

        return unit.Trim().ToUpperInvariant()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("³", "3", StringComparison.Ordinal)
            .Replace("²", "2", StringComparison.Ordinal)
            .Replace("DL", "dL", StringComparison.Ordinal)
            .Replace("ML", "mL", StringComparison.Ordinal)
            .Replace("MEQ", "mEq", StringComparison.Ordinal);
    }

    private static string DisplayCompact(decimal numericValue) =>
        numericValue.ToString("#,##0.##", CultureInfo.GetCultureInfo("pt-BR"));

    private static bool IsExactNumeric(LaboratoryObservation observation)
    {
        var value = observation.RawValue.TrimStart();
        return !value.StartsWith('<') && !value.StartsWith('>');
    }

    private sealed record ScalarDefinition(string Label, string ConceptId, string? ObservationLabel, string Unit);

    private sealed record EpisodeContext(SemanticEpisode Episode, DateTime? Timestamp);
}
