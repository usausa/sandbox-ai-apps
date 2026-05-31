namespace InspectorChecker.Services;

using InspectorChecker.Models;

public sealed class InspectionFeatureSummaryBuilder
{
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

        var customerProfiles = orderedRecords
            .GroupBy(x => x.CustomerId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var voltages = group.Select(x => x.Voltage).ToArray();
                return new CustomerVoltageProfile(
                    group.Key,
                    Round(voltages.Average()),
                    Round(StandardDeviation(voltages)),
                    Round(voltages.Min()),
                    Round(voltages.Max()),
                    Round(1 - (voltages.Distinct().Count() / (double)voltages.Length)));
            })
            .OrderBy(x => x.CustomerId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var averageByCustomer = customerProfiles.ToDictionary(
            x => x.CustomerId,
            x => x.AverageVoltage,
            StringComparer.OrdinalIgnoreCase);

        var dailyDetails = orderedRecords
            .GroupBy(x => x.InvestigationDate)
            .Select(group =>
            {
                var dayRecords = group
                    .OrderBy(x => x.CustomerId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Sequence)
                    .ToArray();

                var voltages = dayRecords.Select(x => x.Voltage).ToArray();
                var groupedByVoltage = dayRecords
                    .GroupBy(x => x.Voltage)
                    .OrderByDescending(x => x.Count())
                    .ThenBy(x => x.Key)
                    .ToArray();

                var template = string.Join(
                    ", ",
                    dayRecords.Select(x => $"{x.CustomerId}={x.Voltage:F1}V"));

                return new DailyTemplateSnapshot(
                    group.Key,
                    template,
                    new DailyFeatureSummary(
                        group.Key,
                        dayRecords.Length,
                        dayRecords.Select(x => x.CustomerId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                        Round(voltages.Average()),
                        Round(voltages.Min()),
                        Round(voltages.Max()),
                        groupedByVoltage.Length,
                        Round((dayRecords.Length - groupedByVoltage.Length) / (double)dayRecords.Length),
                        Round(dayRecords.Count(x => IsRoundValue(x.Voltage)) / (double)dayRecords.Length),
                        Round(dayRecords.Count(x => Math.Abs(x.Voltage - 100.0) <= 0.2) / (double)dayRecords.Length),
                        Round(dayRecords.Average(x => Math.Abs(x.Voltage - averageByCustomer[x.CustomerId]))),
                        $"{groupedByVoltage[0].Key:F1}V",
                        groupedByVoltage[0].Count(),
                        false));
            })
            .OrderBy(x => x.InvestigationDate)
            .ToArray();

        var repeatedTemplates = dailyDetails
            .GroupBy(x => x.Template)
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

        var allVoltages = orderedRecords.Select(x => x.Voltage).ToArray();

        return new InspectionFeatureSummary(
            orderedRecords.Length,
            customerProfiles.Length,
            orderedRecords[0].InvestigationDate,
            orderedRecords[^1].InvestigationDate,
            Round(allVoltages.Average()),
            Round(StandardDeviation(allVoltages)),
            customerProfiles,
            dailySummaries,
            repeatedTemplates);
    }

    private static bool IsRoundValue(double voltage)
    {
        var scaled = (int)Math.Round(voltage * 10, MidpointRounding.AwayFromZero);
        return scaled % 5 == 0;
    }

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
