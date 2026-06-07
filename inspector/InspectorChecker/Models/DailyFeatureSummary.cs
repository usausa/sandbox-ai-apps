namespace InspectorChecker.Models;

public sealed record DailyFeatureSummary(
    DateOnly InvestigationDate,
    int RecordCount,
    int CustomerCount,
    double MeanCurrent,
    double MinimumCurrent,
    double MaximumCurrent,
    double StandardDeviation,
    int UniqueCurrentCount,
    double DuplicateRatio,
    double RoundValueRatio,
    double NearDefaultRatio,
    string MostCommonCurrent,
    int MostCommonCurrentCount,
    bool HasRepeatedTemplate);
