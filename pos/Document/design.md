# PosChecker 設計書

POS トランザクションから、レジ担当者(従業員)と会員による **不正一覧の5事象** を、機械集計と LLM で検出する
デモアプリケーション（`PosChecker`）の設計書です。既存の `inspector`(InspectorChecker) を踏襲します。
CSV 仕様の正本は `README.md` §CSV仕様です。

---

## 1. 目的とスコープ

`不正一覧.md` に定義された次の5事象を、POS トランザクションから検知する。

1. **ポイント不正付与** … 客がカード未提示でも店員が自分のカードへ付与（`PointAbuse`）
2. **かご抜け** … 店員と客がグルで数量を少なく入力/未スキャン（`CartBypass`）
3. **クーポン不正利用** … スクショ等で同一クーポンを反復利用（`CouponAbuse`）
4. **フリー返品不正** … 担当者が不正にフリー返品（`ReturnFraud`）
5. **打ち直し不正** … 担当者が不正に打ち直し（`RekeyFraud`）

「人間が説明できる特徴」を機械集計で抽出し、LLM に最終的な不正確率と理由を判定させる。実運用エンジンではなく、
**判定ロジックを説明しやすく見せるデモ**である点は inspector と同じ。

- **判定主軸 = 店舗×担当者**（担当者ごとに不正スコアを算出）。
- **ポイント不正・クーポン不正は会員別にも評価**し、不正シグナルで裏付けのある会員のみフラグする。
- 入力は **3つの結合ビューCSV**（SalesHeader / SalesDetail / Promotion）を **まとめてアップロード**する。
  複数店舗・複数担当者・複数会員を1ファイル群として扱う。

---

## 2. アーキテクチャ

```text
3ビューCSV（SalesHeader / SalesDetail / Promotion）
    ↓
PosDataLoader … 検証し、キーで明細をヘッダへ結合（PosDataset）
    ↓
PosFeatureSummaryBuilder … 店舗×担当者・会員で決定論的に集計、不正シグナルを抽出
    ↓
PosFraudAnalyzer … 集計(camelCase) を Foundry(gpt-5.4-mini) に送信（temperature:0 / json）
    ↓
PosAnalysisResult（overall_score / cashier_results[] / member_results[]）＋ TokenUsageResult
    ↓
Blazor 画面で表示（総合スコア・担当者スコア・会員フラグ・分布・シグナル・トークン/費用）
```

機械集計（決定論）と LLM 判定（説明＋スコア）のハイブリッド。会員フラグは不正シグナルで裏付けのある会員のみに限定し、
母数の小さい偶然の偏りによる誤検知を排除する。

### inspector との対応

| inspector | PosChecker |
|---|---|
| 巡回調査 CSV（単一） | 3ビューCSV（ヘッダ/明細/販促） |
| `SurveyRecord` | `SalesHeaderRecord` / `SalesDetailRecord` / `PromotionRecord` |
| `InspectionFeatureSummaryBuilder` | `PosFeatureSummaryBuilder` |
| `InspectionFraudAnalyzer` | `PosFraudAnalyzer`（＋トークン/費用算出） |
| `InspectorCheckerService` | `PosCheckerService` |
| 日別 `daily_results[]` | `cashier_results[]` ＋ `member_results[]` |
| `TokenUsageResult` | 同左 |

### 技術スタック（inspector と同一）

.NET 10 / Blazor Server、`Microsoft.Extensions.AI` の `IChatClient`（Azure.AI.OpenAI 経由で Foundry）、
`CsvHelper` / `Serilog`、`Nullable=enable`、コーディング規約は `AGENTS.md` 準拠（メンバ変数に `_` を付けない・警告ゼロ）。

---

## 3. データ構造（3ビューCSV）

列名は英語 PascalCase。LLM 処理に不要な列は持たない。日付は `yyyy-MM-dd`、時刻は `HH:mm:ss`。
取引キー = `(StoreCode, SalesDate, PosNo, SalesNo)`。明細はこのキーでヘッダに結合する。
Promotion のキーは `(StoreCode, SalesDate, PosNo, SlipNo)`。列の一覧と意味は `README.md` §CSV仕様を参照。

### enum

| enum | 値 |
|---|---|
| `TransactionType` | Sale / Return |
| `ProcessType` | Normal / BulkCancel / Abort / Exchange / **Rekey**(打ち直し) |
| `RegisterType` | Normal / Practice / ScanCheck |
| `TenderType` | Cash / Credit / GiftCard |
| `SettlementType` | Credit / UnionPay / EMoney / BarcodePay |
| `IssueType` | Receipt / DM / Flyer / Other |
| `MemberTargetType` | None / AllMembers / CashMembers / CreditMembers / SpecifiedMembers / NonMembers / RakutenMembers / SdAndRakuten / SdNotRakuten |

> 元の処理区分には「打ち直し」が無いため、事象5を表現する拡張値 `Rekey` を追加している。

### C# 入力モデル

```csharp
public sealed record SalesHeaderRecord(
    string StoreCode, DateOnly SalesDate, int PosNo, int SalesNo,
    TransactionType TransactionType, ProcessType ProcessType, RegisterType RegisterType,
    string CashierCode, string CashierName, string? MemberCode, TimeOnly SystemTime,
    TenderType TenderType, SettlementType? SettlementType, string? AccountNumber,
    int PointsUsed, int TotalAmount, int Sequence);

public sealed record SalesDetailRecord(
    string StoreCode, DateOnly SalesDate, int PosNo, int SalesNo,
    string Jancode, string ProductName, string UsageCode, string UsageName,
    int UnitPrice, int Quantity, int LineAmount);

public sealed record PromotionRecord(
    string StoreCode, DateOnly SalesDate, int PosNo, int SlipNo,
    string PlanCode, string PlanName, string CouponCode, string? ScannedMemberCode,
    string CouponJan, IssueType IssueType, string CouponName,
    DateOnly? StartDate, DateOnly? EndDate, MemberTargetType MemberTargetType, int Sequence);

public sealed record SalesTransaction(SalesHeaderRecord Header, IReadOnlyList<SalesDetailRecord> Details);
public sealed record PosDataset(IReadOnlyList<SalesTransaction> Transactions, IReadOnlyList<PromotionRecord> Promotions);
```

`PromotionRecord.IsMemberLimited`（会員限定: MemberTargetType が SpecifiedMembers/CreditMembers/Rakuten系）と
`IsAppCoupon`（CouponName/PlanName に「アプリ」を含む）を派生プロパティで持つ。

### LLM ペイロード（camelCase・WriteIndented）

生の取引ヘッダは送らず、決定論的な集計と販促のみを送る（トークン削減）。

```jsonc
{
  "datasetSummary": { "transactionCount": 671, "storeCount": 2, "cashierCount": 10, "memberCount": 38,
                      "startDate": "2026-05-01", "endDate": "2026-05-07", "salesCount": 620, "returnCount": 28, "rekeyCount": 12 },
  "typeDistribution":  [ { "type": "Sale", "count": 620, "amount": 1234500, "ratio": 0.924 } ],
  "tenderDistribution":[ { "tender": "Cash", "count": 360, "amount": 720000, "ratio": 0.54 } ],
  "cashierSummaries":  [ { "storeCode": "0001", "cashierCode": "1002", "cashierName": "鈴木二郎",
                           "returnRatio": 0.04, "rekeyRatio": 0.02, "memberConcentration": 0.56,
                           "topMemberCode": "M9001", "sameItemRepeatCount": 1, "shortIntervalSameMemberCount": 41 } ],
  "memberSummaries":   [ { "memberCode": "M9001", "salesCount": 80, "cashierConcentration": 0.95, "repeatedCouponCount": 0 } ],
  "fraudSignals":      [ { "kind": "CashierMemberConcentration", "storeCode": "0001", "cashierCode": "1002",
                           "memberCode": "M9001", "detail": "...", "count": 42, "ratio": 0.56 } ],
  "rawPromotions":     [ { "storeCode": "0001", "couponCode": "C1001", "scannedMemberCode": "M1007",
                           "memberTargetType": "SpecifiedMembers", "startDate": "2026-05-01" } ]
}
```

---

## 4. 不正判定ルール

判定主軸は **店舗×担当者**、補助で **会員**。比率は 0 除算回避のため分母を `max(n,1)` とし `Math.Round(x,3)` で丸める。
`ExcludeNonNormalRegister=true` のとき、登録区分が練習/スキャンチェックの取引は集計から除外する。

### 担当者特徴量（`CashierFeatureSummary`、事象1・2・4・5）

| 特徴量 | 算出式 | 事象 |
|---|---|---|
| `returnRatio` | 返品件数 / 全取引件数 | 4 |
| `rekeyRatio` | 打直(ProcessType=Rekey)件数 / 全取引件数 | 5 |
| `memberConcentration` / `topMemberCode` | 会員付き売上の最頻会員の占有率 | 1 |
| `sameItemRepeatCount` | 同一JANが複数明細、または1明細で数量≥(min+1) の会計数 | 2 |
| `shortIntervalSameMemberCount` | 同一会員の連続売上が `ShortIntervalSeconds` 以内の回数 | 1 |
| `afterHoursReturnRekeyCount` | 営業時間外の返品・打直件数 | 4,5 |

### 会員特徴量（`MemberFeatureSummary`、事象1・3）

| 特徴量 | 算出式 | 事象 |
|---|---|---|
| `cashierConcentration` / `topCashierCode` | 当該会員の売上の最頻担当者の占有率 | 1 |
| `repeatedCouponCount` | 同一クーポンを `RepeatedCouponMinOccurrences` 回以上利用したクーポン数 | 3 |
| `memberLimitedCouponCount` / `appCouponCount` | 会員限定/アプリクーポンの利用件数 | 3 |

会員サマリは **不正シグナルで裏付けのある会員のみ**残す（母数の小さい偶然の偏りを排除）。

### 不正シグナル（`FraudSignal`、決定論）

`MinSuspiciousCount=3` を基本の最小件数とする。

- **CashierMemberConcentration**: `topMemberSalesCount≥3` かつ `memberConcentration≥MemberConcentrationThreshold`（事象1）
- **SameItemMultiBuy**: `sameItemRepeatCount≥3`（事象2）
- **ShortIntervalSameMember**: `shortIntervalSameMemberCount≥3`（事象1）
- **HighReturnCashier**: `returnCount≥3` かつ `returnRatio≥HighReturnRatioThreshold`（事象4）
- **HighRekeyCashier**: `rekeyCount≥3` かつ `rekeyRatio≥HighRekeyRatioThreshold`（事象5）
- **RepeatedCouponByMember**: 同一会員・同一クーポンが `RepeatedCouponMinOccurrences` 回以上（事象3）

### しきい値（`PosCheckerSettings`）

| パラメータ | 既定 | 用途 |
|---|---|---|
| `ShortIntervalSeconds` | 300 | 短時間連続会計（事象1） |
| `MemberConcentrationThreshold` | 0.3 | 担当者の会員偏り（事象1） |
| `HighReturnRatioThreshold` | 0.1 | 返品率（事象4） |
| `HighRekeyRatioThreshold` | 0.1 | 打直率（事象5） |
| `RepeatedCouponMinOccurrences` | 3 | クーポン反復（事象3、偶然の重複と区別） |
| `SameItemMinOccurrences` | 2 | 同一商品の複数明細（事象2） |
| `BusinessHoursStartHour` / `EndHour` | 9 / 21 | 時間帯偏り |
| `ExcludeNonNormalRegister` | true | 練習/スキャンチェック除外 |

### 正常の前提（誤検知回避）

- 件数の絶対値ではなく **他担当者と比べた比率の偏り・反復・時間帯の不自然さ**を見る。
- 客都合の返品・本人によるポイント/クーポン利用は正常に発生する。多いこと自体は不正ではない。
- 母数の小さい会員の `cashierConcentration` が高くても偶然なので不正としない。
- 不正シグナルが複数事象に広がる、または他担当者から明確に外れる場合に限り高スコア。

### スコアリング

- `overall_score`：ファイル全体 0〜100。大半の担当者が正常域なら 40 未満。ただし score 70 以上の担当者/会員が
  1つでもあれば 60 以上に引き上げる。
- 担当者 `score` / 会員 `score`：0〜100。不正シグナルが揃い他担当者から明確に外れる場合のみ 70 以上。
- C# 側で `cashier_results` を全 (店舗×担当者) へ正規化、`member_results` は会員サマリ（=シグナル裏付け）に限定。

### プロンプト出力 JSON（`Prompts/pos_analyzer.txt`）

```json
{"overall_score":0,"summary":"","recommended_action":"","suspicious_patterns":[],
 "cashier_results":[{"store_code":"","cashier_code":"","cashier_name":"","score":0,"reason":"","scenarios":[],"suspicious_keys":[]}],
 "member_results":[{"member_code":"","score":0,"reason":"","scenarios":[]}]}
```

`scenarios` は `PointAbuse` / `CartBypass` / `CouponAbuse` / `ReturnFraud` / `RekeyFraud`。

---

## 5. 実装

### プロジェクト構成

```text
PosChecker/
├─ Models/        … enum・入力レコード・集計・結果モデル
├─ Services/
│   ├─ PosDataLoader.cs            … 3ビューCSV読込＋結合
│   ├─ PosFeatureSummaryBuilder.cs … 決定論的集計＋シグナル
│   ├─ PosFraudAnalyzer.cs         … LLM＋トークン/費用
│   └─ PosCheckerService.cs        … オーケストレーション
├─ Settings/      … FoundrySettings / PosCheckerSettings
├─ Prompts/pos_analyzer.txt
├─ Components/Pages/Check.razor(.cs)
└─ wwwroot/samples/<set>/{SalesHeader,SalesDetail,Promotion}.csv
tools/
├─ generate_samples.py  … サンプル生成（6セット×3CSV）
└─ validate_foundry.py  … Foundry 検証
```

### サービス層

| クラス | 主メソッド |
|---|---|
| `PosDataLoader` | `Task<PosDataset> LoadAsync(header, detail, promotion, ct)` |
| `PosFeatureSummaryBuilder` | `PosFeatureSummary Build(PosDataset)` |
| `PosFraudAnalyzer` | `Task<(PosAnalysisResult, TokenUsageResult)> AnalyzeAsync(summary, dataset, ct)` |
| `PosCheckerService` | `Task<PosCheckResult> AnalyzeAsync(headerPath, detailPath, promotionPath, progress, ct)` |

`PosFraudAnalyzer` は `Temperature=0`、`ResponseFormat=Json`、`BuildUsage(response.Usage)` で概算費用を算出、
`Normalize` で `cashier_results` を全担当者へ正規化、`member_results` を会員サマリ（シグナル裏付け）に限定する。

### 主な結果モデル

```csharp
public sealed record CashierRiskResult { string StoreCode; string CashierCode; string CashierName;
    int Score; string Reason; IReadOnlyList<string> Scenarios; IReadOnlyList<string> SuspiciousKeys; }
public sealed record MemberRiskResult  { string MemberCode; int Score; string Reason; IReadOnlyList<string> Scenarios; }
public sealed record PosAnalysisResult  { int OverallScore; string Summary; string RecommendedAction;
    IReadOnlyList<string> SuspiciousPatterns; IReadOnlyList<CashierRiskResult> CashierResults; IReadOnlyList<MemberRiskResult> MemberResults; }
public sealed record TokenUsageResult(long InputTokens, long OutputTokens, long TotalTokens, decimal? EstimatedCostJpy);
```

### 画面（Check.razor）

3ファイルを `InputFile multiple` でまとめてアップロード（ヘッダ列で自動判別）。表示: 総合スコア、担当者スコア
（店舗×担当者）、会員フラグ、取引種別/金種の構成、不正シグナル、**トークン使用量・概算費用**、売上CSVプレビュー、
サンプルセットDL。

### サンプルと検証

`tools/generate_samples.py` が複数店舗(2)・複数担当者(各5)・複数会員・約1週間の6セット（normal＋
fraud-point/cart/coupon/return/rekey）を生成（乱数シード固定）。`tools/validate_foundry.py` が C# と同じ集計＋
プロンプトを再現し各セットを Foundry に POST、総合スコアと担当者/会員スコアの分離を確認する（API キーは環境変数のみ）。
実測値は `README.md` §ルール検証。

### 留意点

- **ペイロード削減**: 生の取引ヘッダは送らず、集計＋販促のみ（トークン約1万、費用 約¥1〜2/回）。
- **金額型**: 合計が int を超えうるため集計は `long`。
- **会員フラグの誤検知抑制**: `member_results` は不正シグナル裏付けのある会員に限定（C# 側でも検証）。
- デモ前提。実運用は端末ログ・監視カメラ・ドロワー開閉センサ・シフト表との突合で精度向上。
