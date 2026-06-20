namespace PosChecker.Models;

// SalesDetail.csv の1行 = 1売上明細（用途名を結合済み）。
// キー (StoreCode, SalesDate, PosNo, SalesNo) で SalesHeaderRecord に結合する。
public sealed record SalesDetailRecord(
    string StoreCode,
    DateOnly SalesDate,
    int PosNo,
    int SalesNo,
    string Jancode,
    string ProductName,
    string UsageCode,
    string UsageName,
    int UnitPrice,
    int Quantity,
    int LineAmount)
{
    public string Key => $"{StoreCode}/{SalesDate:yyyyMMdd}/{PosNo}/{SalesNo}";
}
