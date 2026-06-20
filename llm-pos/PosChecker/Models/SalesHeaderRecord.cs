namespace PosChecker.Models;

// SalesHeader.csv の1行 = 1売上取引（担当者名・決済情報を結合済み）。
// キー = (StoreCode, SalesDate, PosNo, SalesNo)。
public sealed record SalesHeaderRecord(
    string StoreCode,
    DateOnly SalesDate,
    int PosNo,
    int SalesNo,
    TransactionType TransactionType,
    ProcessType ProcessType,
    RegisterType RegisterType,
    string CashierCode,
    string CashierName,
    string? MemberCode,
    TimeOnly SystemTime,
    TenderType TenderType,
    SettlementType? SettlementType,
    string? AccountNumber,
    int PointsUsed,
    int TotalAmount,
    int Sequence)
{
    public string Key => $"{StoreCode}/{SalesDate:yyyyMMdd}/{PosNo}/{SalesNo}";
}
