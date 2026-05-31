namespace InspectorChecker.Models;

public sealed record DailyFeatureSummary(
    DateOnly InvestigationDate,
    int RecordCount,
    int CustomerCount,
    double MeanVoltage,
    double MinimumVoltage,
    double MaximumVoltage,
    int UniqueVoltageCount,
    double DuplicateRatio,
    double RoundValueRatio,
    double NearDefaultRatio,
    double MeanAbsoluteCustomerDeviation,
    string MostCommonVoltage,
    int MostCommonVoltageCount,
    bool HasRepeatedTemplate);
