namespace InspectorChecker.Models;

public sealed record InspectionFeatureSummary(
    int RecordCount,
    int CustomerCount,
    DateOnly StartDate,
    DateOnly EndDate,
    double MeanVoltage,
    double StandardDeviation,
    IReadOnlyList<CustomerVoltageProfile> CustomerProfiles,
    IReadOnlyList<DailyFeatureSummary> DailySummaries,
    IReadOnlyList<RepeatedDailyTemplate> RepeatedDailyTemplates);
