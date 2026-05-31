namespace InspectorChecker.Models;

public sealed record RepeatedDailyTemplate(
    string CustomerPattern,
    int OccurrenceCount,
    IReadOnlyList<DateOnly> Dates);
