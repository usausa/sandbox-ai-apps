namespace InspectorChecker.Models;

public sealed record RepeatedDailyTemplate(
    string ValuePattern,
    int OccurrenceCount,
    IReadOnlyList<DateOnly> Dates);
