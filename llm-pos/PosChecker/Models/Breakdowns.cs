namespace PosChecker.Models;

// 取引種別 (売上/返品) ごとの件数・金額・構成比。
public sealed record TransactionTypeBreakdown(TransactionType Type, int Count, long Amount, double Ratio);

// 金種 (現金/クレジット/商品券) ごとの件数・金額・構成比。
public sealed record TenderBreakdown(TenderType Tender, int Count, long Amount, double Ratio);
