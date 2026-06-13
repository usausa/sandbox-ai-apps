namespace PosChecker.Models;

public sealed record SequenceAnomaly(
    DateOnly Date,
    string Kind,
    string? TransactionId,
    string? OriginalTransactionId,
    int Amount,
    int Occurrences,
    int SecondsApart);
