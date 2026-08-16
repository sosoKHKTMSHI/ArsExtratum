using System.Globalization;
using System.Text;

namespace ArsExtractum.Core.LaboratoryCurves;

public static class LaboratoryCurveTextFormatter
{
    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");

    public static string Format(LaboratoryCurveProjection projection, bool includeDelta)
    {
        var builder = new StringBuilder("Curvas:");
        foreach (var series in projection.Series)
        {
            builder.AppendLine().Append('#').Append(series.Label);
            if (!string.IsNullOrWhiteSpace(series.Unit))
            {
                builder.Append(" (").Append(series.Unit).Append(')');
            }

            builder.Append(": ");
            var repeatedDays = series.Points.GroupBy(static point => DateOnly.FromDateTime(point.Timestamp))
                .Where(static group => group.Count() > 1)
                .Select(static group => group.Key)
                .ToHashSet();
            LaboratoryCurvePoint? previous = null;
            for (var index = 0; index < series.Points.Count; index++)
            {
                var point = series.Points[index];
                if (index > 0)
                {
                    builder.Append(" | ");
                }

                builder.Append(FormatDate(point.Timestamp, projection.IncludeYear,
                        repeatedDays.Contains(DateOnly.FromDateTime(point.Timestamp))))
                    .Append(" - ")
                    .Append(FormatPoint(series, point));
                if (includeDelta && series.SupportsDelta && previous is not null &&
                    point.Values.Count == 1 && previous.Values.Count == 1)
                {
                    builder.Append(" (").Append(FormatDelta(point.Values[0], previous.Values[0])).Append(')');
                }

                previous = point;
            }
        }

        return builder.ToString();
    }

    private static string FormatPoint(LaboratoryCurveSeries series, LaboratoryCurvePoint point)
    {
        if (series.Key == LaboratoryCurveDefinitions.LeukogramFractions)
        {
            var leukocytes = point.Values[0];
            var fractions = point.Values.Skip(1).Select(static value => $"{value.Label} {value.DisplayValue}%");
            return $"Leuco {leukocytes.DisplayValue} ({string.Join(" | ", fractions)})";
        }

        if (series.Key == LaboratoryCurveDefinitions.BilirubinsFractions)
        {
            return $"({string.Join(" | ", point.Values.Select(static value => $"{value.Label} {value.DisplayValue}"))})";
        }

        return point.Values[0].DisplayValue;
    }

    private static string FormatDate(DateTime timestamp, bool includeYear, bool includeTime)
    {
        var format = includeYear ? "dd/MM/yy" : "dd/MM";
        if (includeTime)
        {
            format += " HH:mm";
        }

        return timestamp.ToString(format, CultureInfo.InvariantCulture);
    }

    private static string FormatDelta(LaboratoryCurveValue current, LaboratoryCurveValue previous)
    {
        var delta = current.NumericValue - previous.NumericValue;
        if (delta == 0m)
        {
            return "±0";
        }

        var fixedDecimals = current.FixedDeltaDecimals ?? previous.FixedDeltaDecimals;
        var decimals = fixedDecimals ?? Math.Max(DisplayedDecimals(current), DisplayedDecimals(previous));
        var format = fixedDecimals is not null
            ? "#,##0." + new string('0', decimals)
            : decimals == 0
                ? "#,##0"
                : "#,##0." + new string('#', decimals);
        return (delta > 0m ? "+" : string.Empty) + delta.ToString(format, Portuguese);
    }

    private static int DisplayedDecimals(LaboratoryCurveValue value)
    {
        var separator = value.DisplayValue.LastIndexOf(',');
        return separator < 0 ? 0 : Math.Min(2, value.DisplayValue.Length - separator - 1);
    }
}
