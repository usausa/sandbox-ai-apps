namespace PosChecker.Models;

// 取引区分。売上 / 返品。
public enum TransactionType
{
    Sale,
    Return
}

// 処理区分。通常 / 一括取消 / 中止 / 両替 に加え、不正一覧の事象5を表現する拡張値 Rekey(打ち直し) を持つ。
public enum ProcessType
{
    Normal,
    BulkCancel,
    Abort,
    Exchange,
    Rekey
}

// 登録区分。通常 / 練習 / スキャンチェック。
public enum RegisterType
{
    Normal,
    Practice,
    ScanCheck
}

// 金種。現金 / クレジット / 商品券。
public enum TenderType
{
    Cash,
    Credit,
    GiftCard
}

// 決済種別。クレジット / 銀聯 / 電子マネー / バーコード決済。
public enum SettlementType
{
    Credit,
    UnionPay,
    EMoney,
    BarcodePay
}

// 発行区分。レシート / DM / チラシ / その他。
public enum IssueType
{
    Receipt,
    DM,
    Flyer,
    Other
}

// 対象会員指定区分。会員限定クーポン判定に使用する。
public enum MemberTargetType
{
    None,
    AllMembers,
    CashMembers,
    CreditMembers,
    SpecifiedMembers,
    NonMembers,
    RakutenMembers,
    SdAndRakuten,
    SdNotRakuten
}
