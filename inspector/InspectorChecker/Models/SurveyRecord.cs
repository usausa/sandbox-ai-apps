namespace InspectorChecker.Models;

public sealed record SurveyRecord(
    DateOnly InvestigationDate,
    string CustomerId,
    double Voltage,
    int Sequence);
