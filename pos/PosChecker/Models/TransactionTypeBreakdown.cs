namespace PosChecker.Models;

public sealed record TransactionTypeBreakdown(
    TransactionType Type,
    int Count,
    long Amount,
    double Ratio);
