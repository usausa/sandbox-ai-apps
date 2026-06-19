# PosChecker

実 POS テーブルのトランザクションから、レジ担当者(従業員)と会員による **不正一覧の5事象** を、
機械集計と LLM のハイブリッドで検出する Blazor Server アプリケーションです。判定は **「店舗×担当者」を主軸**に行い、
ポイント不正・クーポン不正は **会員別**にも評価します。設計は既存の `inspector`(InspectorChecker) を踏襲しています。

> `不正一覧.md` の「不正一覧」「データ情報抜粋」を仕様の出発点にしています。テーブルそのままではなく、
> プロンプト/集計で使いやすいよう **結合したビュー**として CSV を定義しています(本書 §CSV仕様が正本)。

## 検出する5つの不正事象

| # | 事象 | scenarios 値 | 主な特徴量 |
|---|---|---|---|
| 1 | ポイント不正付与（客がカード未提示でも店員が自分のカードへ付与） | `PointAbuse` | 担当者の売上が特定会員に偏る・同一会員の短時間連続会計・会員側の担当者偏り |
| 2 | かご抜け（店員と客がグルで数量を少なく入力/未スキャン） | `CartBypass` | 同一JAN/同一用途を複数購入する会計を頻発する担当者 |
| 3 | クーポン不正利用（スクショ等を何度も利用） | `CouponAbuse` | 同一会員が同一クーポンを反復スキャン・会員限定/アプリクーポンの配信期間と乖離 |
| 4 | フリー返品不正 | `ReturnFraud` | 担当者別の返品件数・返品率が突出・時間帯偏り |
| 5 | 打ち直し不正 | `RekeyFraud` | 担当者別の打直件数・打直率が突出・時間帯偏り |

## アーキテクチャ

```text
3ビューCSV（SalesHeader / SalesDetail / Promotion）
    ↓
PosDataLoader … 行を検証し、キーで明細をヘッダへ結合
    ↓
PosFeatureSummaryBuilder … 決定論的に集計
    ├─ 店舗×担当者: 返品率/打直率・会員偏り・同一商品複数購入・短時間連続会計・時間帯偏り
    ├─ 会員: 担当者偏り・同一クーポン反復・会員限定/アプリクーポン件数
    └─ 不正シグナル（FraudSignal）と分布
    ↓
PosFraudAnalyzer … 集計(camelCase)を Foundry(gpt-5.4-mini) に送信
    ↓（temperature:0 / response_format:json）
不正確率 / 担当者スコア / 会員フラグ / トークン使用量・概算費用
    ↓
Blazor 画面で表示
```

決定論的な機械集計（説明可能な根拠）と LLM 判定（スコア＋理由）のハイブリッドです。会員フラグは
`FraudSignal` で裏付けのある会員のみに限定し、母数の小さい偶然の偏りによる誤検知を排除しています。

## CSV 仕様（最終仕様）

UTF-8・ヘッダ付き。3つのビューCSVを **まとめてアップロード**します（ヘッダ列名で自動判別）。
列名は英語 PascalCase。enum 列は下表の英語値を使います。日付は `yyyy-MM-dd`、時刻は `HH:mm:ss`。

### 1. SalesHeader.csv — 売上取引ヘッダ
1行 = 1取引。キー = (StoreCode, SalesDate, PosNo, SalesNo)。

| 列 | 説明 |
|---|---|
| `StoreCode` | 店舗コード |
| `SalesDate` | 売上日付 |
| `PosNo` | POS番号 |
| `SalesNo` | 売上番号 |
| `TransactionType` | `Sale`(売上) / `Return`(返品) |
| `ProcessType` | `Normal` / `BulkCancel`(一括取消) / `Abort`(中止) / `Exchange`(両替) / `Rekey`(打ち直し) |
| `RegisterType` | `Normal`(通常) / `Practice`(練習) / `ScanCheck`(スキャンチェック) |
| `CashierCode` | 担当者コード |
| `CashierName` | 担当者名 |
| `MemberCode` | 会員(ポイントカード)コード（空可） |
| `SystemTime` | 取引時刻 |
| `TenderType` | `Cash`(現金) / `Credit`(クレジット) / `GiftCard`(商品券) |
| `SettlementType` | `Credit` / `UnionPay`(銀聯) / `EMoney`(電子マネー) / `BarcodePay`(バーコード決済)（空可） |
| `AccountNumber` | 決済会員番号/受付番号（空可） |
| `PointsUsed` | 使用ポイント |
| `TotalAmount` | 取引合計額（明細合計） |

> 元の処理区分には「打ち直し」が無いため、事象5を表現する拡張値 `Rekey` を追加しています。

### 2. SalesDetail.csv — 売上明細
1行 = 1明細。キー (StoreCode, SalesDate, PosNo, SalesNo) でヘッダに結合。

| 列 | 説明 |
|---|---|
| `StoreCode` / `SalesDate` / `PosNo` / `SalesNo` | 結合キー |
| `Jancode` | JANコード |
| `ProductName` | 商品名 |
| `UsageCode` | 用途コード |
| `UsageName` | 用途名 |
| `UnitPrice` | 実売価 |
| `Quantity` | 数量 |
| `LineAmount` | 明細合計額 |

### 3. Promotion.csv — 販促/クーポン
1行 = 1クーポンスキャン。キー = (StoreCode, SalesDate, PosNo, SlipNo)。

| 列 | 説明 |
|---|---|
| `StoreCode` | 店舗コード |
| `SalesDate` | 売上日付 |
| `PosNo` | POS番号 |
| `SlipNo` | 処理通番 |
| `PlanCode` | 企画コード |
| `PlanName` | 企画名 |
| `CouponCode` | スキャンしたクーポンコード |
| `ScannedMemberCode` | スキャンした会員コード（空可） |
| `CouponJan` | クーポン券JAN |
| `IssueType` | `Receipt`(レシート) / `DM` / `Flyer`(チラシ) / `Other` |
| `CouponName` | クーポン名称（アプリクーポン判定に使用） |
| `StartDate` / `EndDate` | 配信期間（空可） |
| `MemberTargetType` | `None` / `AllMembers` / `CashMembers` / `CreditMembers` / `SpecifiedMembers` / `NonMembers` / `RakutenMembers` / `SdAndRakuten` / `SdNotRakuten`（会員限定判定） |

### CSV 例

```csv
# SalesHeader.csv
StoreCode,SalesDate,PosNo,SalesNo,TransactionType,ProcessType,RegisterType,CashierCode,CashierName,MemberCode,SystemTime,TenderType,SettlementType,AccountNumber,PointsUsed,TotalAmount
0001,2026-05-01,2,1,Sale,Normal,Normal,1002,鈴木二郎,M1010,13:00:00,Cash,,,0,640
```
```csv
# SalesDetail.csv
StoreCode,SalesDate,PosNo,SalesNo,Jancode,ProductName,UsageCode,UsageName,UnitPrice,Quantity,LineAmount
0001,2026-05-01,2,1,4901001000017,牛乳1L,01,食品,220,1,220
```
```csv
# Promotion.csv
StoreCode,SalesDate,PosNo,SlipNo,PlanCode,PlanName,CouponCode,ScannedMemberCode,CouponJan,IssueType,CouponName,StartDate,EndDate,MemberTargetType
0001,2026-05-01,2,1,9001,5月会員限定セール,C1001,M1007,2900000010019,DM,会員限定10%OFF,2026-05-01,2026-05-14,SpecifiedMembers
```

## 機械集計の特徴量（決定論）

`PosCheckerSettings` のしきい値で `FraudSignal` を立て、LLM へ根拠として渡します（既定値）。

| しきい値 | 既定 | 用途 |
|---|---|---|
| `ShortIntervalSeconds` | 300 | 同一会員の短時間連続会計とみなす間隔（事象1） |
| `MemberConcentrationThreshold` | 0.3 | 担当者の売上が特定会員へ偏る目安（事象1） |
| `HighReturnRatioThreshold` | 0.1 | 返品率が高い目安（事象4） |
| `HighRekeyRatioThreshold` | 0.1 | 打直率が高い目安（事象5） |
| `RepeatedCouponMinOccurrences` | 3 | 同一会員の同一クーポン反復の最小回数（事象3） |
| `SameItemMinOccurrences` | 2 | 同一JANの複数明細とみなす最小数（事象2） |
| `BusinessHoursStartHour` / `EndHour` | 9 / 21 | 営業時間帯（外を時間帯偏りとして計上） |
| `ExcludeNonNormalRegister` | true | 練習/スキャンチェックの取引を集計から除外 |

`FraudSignal` の種類: `CashierMemberConcentration` / `SameItemMultiBuy` / `ShortIntervalSameMember` /
`RepeatedCouponByMember` / `HighReturnCashier` / `HighRekeyCashier`。

## 画面の見方

- **不正確率**: ファイル全体の総合スコア（0〜100）。score 70 以上の担当者/会員がいれば 60 以上に引き上がる。
- **担当者スコア（店舗×担当者）**: 判定の主軸。スコア・該当事象・機械集計の特徴・LLM理由・根拠キー。
- **会員フラグ**: ポイント不正/クーポン不正の疑いがある会員（`FraudSignal` で裏付けのある会員のみ）。
- **取引種別 / 金種の構成**、**検出された不正シグナル**、**売上CSVプレビュー**。
- **アップロード概要**: 取引数・店舗/担当者数・会員数・期間に加え、**トークン使用量**と**概算費用（円）**を表示。

## サンプルデータ

画面からダウンロードできるサンプルは6セット（各 SalesHeader/SalesDetail/Promotion の3CSV）。複数店舗・複数担当者・複数会員・約1週間。

| セット | 種別 | 設計意図 |
|---|---|---|
| `normal` | 正常 | 返品/打直が低率、ポイント/クーポンは自然な利用 |
| `fraud-point` | 不正 | 担当者1002(店0001)が自分のカードM9001へ偏って付与・短時間連続会計（事象1） |
| `fraud-cart` | 不正 | 担当者1003(店0001)が同一JAN/用途の複数購入会計を頻発（事象2） |
| `fraud-coupon` | 不正 | 会員M1007が会員限定/アプリクーポンを反復スキャン（事象3） |
| `fraud-return` | 不正 | 担当者1004(店0001)の返品率が突出・時間帯偏り（事象4） |
| `fraud-rekey` | 不正 | 担当者1005(店0001)の打直率が突出・時間帯偏り（事象5） |

サンプルは `tools/generate_samples.py` で再現生成できます（乱数シード固定）。

```pwsh
python tools/generate_samples.py
```

## ルール検証（Foundry）

`tools/validate_foundry.py` は C# の `PosFeatureSummaryBuilder` と同じ集計（camelCase ペイロード）＋
`Prompts/pos_analyzer.txt` を Python で再現し、各サンプルセットを Foundry に POST して
総合スコアと担当者/会員スコアの分離を確認します。API キーは環境変数からのみ読みます。

```pwsh
$env:FOUNDRY_API_KEY="<APIキー>"; python tools/validate_foundry.py; Remove-Item Env:\FOUNDRY_API_KEY
```

判定の主軸は **店舗×担当者** です。実測（temperature:0, gpt-5.4-mini）では、各不正セットの対象担当者/会員が高スコア、
正常担当者は低スコアに明確に分離します。

| セット | 総合 | 対象担当者/会員のスコア |
|---|---|---|
| normal | 約40 | 担当者は最大でも 40 前後 |
| fraud-point | 約70 | 0001/1002 ≈ 92、会員 M9001 ≈ 95 |
| fraud-cart | 約58 | 0001/1003 ≈ 82 |
| fraud-coupon | 約78 | 0001/1002 ≈ 74、会員 M1007 ≈ 92 |
| fraud-return | 約78 | 0001/1004 ≈ 96 |
| fraud-rekey | 約78 | 0001/1005 ≈ 92 |

## Foundry 設定

`appsettings.json` の `Foundry` セクション。API キーはコミットせず user-secrets / 環境変数で設定します。

```json
{
  "Foundry": {
    "Endpoint": "https://foundry-usausa-resource.services.ai.azure.com",
    "ApiKey": "",
    "ChatDeployment": "gpt-5.4-mini",
    "InputPricePer1M": 0.75,
    "OutputPricePer1M": 4.50,
    "UsdJpyRate": 160.4
  }
}
```

- `temperature: 0`、`response_format: { "type": "json_object" }` を使用。
- 概算費用は Foundry が返すトークン数に `InputPricePer1M`/`OutputPricePer1M`(USD/100万トークン) と `UsdJpyRate` を掛けて算出します（単価未設定なら費用は表示しません）。

```pwsh
dotnet user-secrets set "Foundry:ApiKey" "<APIキー>" --project PosChecker
```

## 補足

- 本アプリケーションはデモ用途です。判定ロジックは「入力が特徴的なパターンを持つか」を説明しやすいように設計しています。
- 実運用では端末ログ・監視カメラ・ドロワー開閉センサ・シフト表との突合で精度を上げられます。
- 設計の詳細は `Document/design.md` を参照してください。
