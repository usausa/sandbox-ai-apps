namespace InspectorChecker.Models;

public sealed record CustomerVoltageProfile(
    string CustomerId,
    double AverageVoltage,
    double StandardDeviation,
    double MinimumVoltage,
    double MaximumVoltage,
    double ExactRepeatRatio);
