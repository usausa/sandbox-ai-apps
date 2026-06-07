namespace InspectorChecker.Models;

public sealed record InspectionFeatureSummary(
    int RecordCount,
    int CustomerCount,
    DateOnly StartDate,
    DateOnly EndDate,
    double MeanCurrent,
    double StandardDeviation,
    double NearDefaultRatio,
    IReadOnlyList<CurrentValueFrequency> ValueDistribution,
    IReadOnlyList<DailyFeatureSummary> DailySummaries,
    IReadOnlyList<RepeatedDailyTemplate> RepeatedDailyTemplates);
