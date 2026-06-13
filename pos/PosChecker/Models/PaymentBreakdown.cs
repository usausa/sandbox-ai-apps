namespace PosChecker.Models;

public sealed record PaymentBreakdown(
    PaymentMethod Method,
    int Count,
    long Amount,
    double Ratio);
