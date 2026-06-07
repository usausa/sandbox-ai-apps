namespace InspectorChecker.Services;

using System.Globalization;

using InspectorChecker.Models;

public sealed class InspectionFeatureSummaryBuilder
{
    // 漏れ電流の合格基準は 1mA 未満。不正入力は 1mA 直下の既定値 (0.97mA 付近) に張り付きやすい。
    private const double DefaultCurrent = 0.97;
    private const double NearDefaultTolerance = 0.02;

    public InspectionFeatureSummary Build(IReadOnlyList<SurveyRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        if (records.Count == 0)
        {
            throw new InvalidOperationException("分析対象のデータがありません。");
        }

        var orderedRecords = records
            .OrderBy(x => x.InvestigationDate)
            .ThenBy(x => x.Sequence)
            .ToArray();

        var dailyDetails = orderedRecords
            .GroupBy(x => x.InvestigationDate)
            .Select(group =>
            {
                var dayRecords = group
                    .OrderBy(x => x.CustomerId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Sequence)
                    .ToArray();

                var currents = dayRecords.Select(x => x.Current).ToArray();
                var groupedByCurrent = dayRecords
                    .GroupBy(x => x.Current)
                    .OrderByDescending(x => x.Count())
                    .ThenBy(x => x.Key)
                    .ToArray();

                // 巡回型では顧客IDが日替わりのため、テンプレートは顧客ID非依存の「値の並び」で判定する。
                var template = string.Join(
                    ", ",
                    currents.OrderBy(x => x).Select(FormatCurrent));

                return new DailyTemplateSnapshot(
                    group.Key,
                    template,
                    new DailyFeatureSummary(
                        group.Key,
                        dayRecords.Length,
                        dayRecords.Select(x => x.CustomerId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                        Round(currents.Average()),
                        Round(currents.Min()),
                        Round(currents.Max()),
                        Round(StandardDeviation(currents)),
                        groupedByCurrent.Length,
                        Round((dayRecords.Length - groupedByCurrent.Length) / (double)dayRecords.Length),
                        Round(dayRecords.Count(x => IsRoundValue(x.Current)) / (double)dayRecords.Length),
                        Round(dayRecords.Count(x => IsNearDefault(x.Current)) / (double)dayRecords.Length),
                        FormatCurrent(groupedByCurrent[0].Key),
                        groupedByCurrent[0].Count(),
                        false));
            })
            .OrderBy(x => x.InvestigationDate)
            .ToArray();

        var repeatedTemplates = dailyDetails
            .GroupBy(x => x.Template, StringComparer.Ordinal)
            .Where(x => x.Count() > 1)
            .Select(x => new RepeatedDailyTemplate(
                x.Key,
                x.Count(),
                x.Select(y => y.InvestigationDate).OrderBy(y => y).ToArray()))
            .OrderByDescending(x => x.OccurrenceCount)
            .ThenBy(x => x.Dates[0])
            .ToArray();

        var repeatedTemplateDates = repeatedTemplates
            .SelectMany(x => x.Dates)
            .ToHashSet();

        var dailySummaries = dailyDetails
            .Select(x => x.Summary with { HasRepeatedTemplate = repeatedTemplateDates.Contains(x.InvestigationDate) })
            .ToArray();

        var allCurrents = orderedRecords.Select(x => x.Current).ToArray();

        var valueDistribution = orderedRecords
            .GroupBy(x => x.Current)
            .Select(group => new CurrentValueFrequency(
                group.Key,
                group.Count(),
                Round(group.Count() / (double)orderedRecords.Length)))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Current)
            .ToArray();

        return new InspectionFeatureSummary(
            orderedRecords.Length,
            orderedRecords.Select(x => x.CustomerId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            orderedRecords[0].InvestigationDate,
            orderedRecords[^1].InvestigationDate,
            Round(allCurrents.Average()),
            Round(StandardDeviation(allCurrents)),
            Round(allCurrents.Count(IsNearDefault) / (double)allCurrents.Length),
            valueDistribution,
            dailySummaries,
            repeatedTemplates);
    }

    private static bool IsRoundValue(double current)
    {
        var scaled = (int)Math.Round(current * 100, MidpointRounding.AwayFromZero);
        return scaled % 5 == 0;
    }

    private static bool IsNearDefault(double current) =>
        Math.Abs(current - DefaultCurrent) <= NearDefaultTolerance + 1e-9;

    private static string FormatCurrent(double current) =>
        current.ToString("F2", CultureInfo.InvariantCulture) + "mA";

    private static double StandardDeviation(double[] values)
    {
        if (values.Length <= 1)
        {
            return 0;
        }

        var average = values.Average();
        var variance = values.Sum(x => Math.Pow(x - average, 2)) / values.Length;
        return Math.Sqrt(variance);
    }

    private static double Round(double value) => Math.Round(value, 3);

    private sealed record DailyTemplateSnapshot(
        DateOnly InvestigationDate,
        string Template,
        DailyFeatureSummary Summary);
}
