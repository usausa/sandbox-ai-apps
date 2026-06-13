namespace PosChecker.Models;

public sealed record TransactionRecord(
    DateOnly BusinessDate,
    string CashierId,
    string TransactionId,
    TimeOnly Time,
    TransactionType Type,
    int Amount,
    int ItemCount,
    PaymentMethod PaymentMethod,
    int DiscountAmount,
    int PointsEarned,
    int PointsRedeemed,
    string? MembershipId,
    string? OriginalTransactionId,
    bool? HasReceipt,
    int Sequence);
