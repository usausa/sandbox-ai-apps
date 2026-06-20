namespace InspectorChecker.Models;

public sealed record CurrentValueFrequency(
    double Current,
    int Count,
    double Ratio);
