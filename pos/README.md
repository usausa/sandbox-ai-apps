# PosChecker

小売店の従業員による **POSレジ操作の不正**（取消抜き取り・返品抜き取り・ポイント不正など）を、トランザクションログと LLM から検出する Blazor Server アプリケーションです。判定は **「1営業日 × 1従業員」単位**で行います。既存の `inspector`（InspectorChecker）の作りを踏襲しています。

## アーキテクチャ

```text
トランザクションログCSV（従業員1名・複数営業日）
    ↓
TransactionCsvLoader … 行を検証して TransactionRecord に変換
    ↓
PosFeatureSummaryBuilder … 決定論的に集計
    ├─ 取引種別／支払方法の構成
    ├─ 日別の Void率 / 返金率 / レシート無し返品率 / 値引率
    ├─ ポイント（利用・付与・会員不在・特定会員偏り・現金集中）
    └─ シーケンス異常（売上直後取消 / ポイント利用直後取消 / 同額返品の反復）
    ↓
PosFraudAnalyzer … 集計(camelCase) + 生取引を Foundry(gpt-5.4-mini) に送信
    ↓（temperature:0 / response_format:json）
不正確率 / 日別スコア / 注目パターンを JSON で取得
    ↓
Blazor 画面で表示
```

機械集計（決定論）と LLM 判定（説明＋スコア）のハイブリッドです。

## できること

- レジ担当者1名分・複数営業日のトランザクション CSV を画面からアップロード
- 取引種別・支払方法の構成、日別の取消/返品/ポイント特徴量を事前集計
- Foundry の LLM に集計結果と生データを渡し、不正確率を 0〜100 で判定
- 営業日単位のスコア、疑わしい取引 ID、注目パターン、シーケンス異常を画面に表示
- 正常2種 / 不正3種のサンプル CSV を同梱

## 判定の前提

- 入力は従業員1名分・複数営業日。判定単位は「1営業日 × 1従業員」。
- 1取引 = 取引ヘッダ単位の1行（明細は金額・点数に集約）。`Amount` は商品総額（グロス、非負）で、方向は取引種別で表す。
- ポイントは 1pt = 1円相当。利用は値引きと同じく支払いを圧縮するが、原資が販促負債である点が異なる。
- 繁忙日に取引・返品が増えるのは自然なので、件数の絶対値ではなく比率・反復・時間帯の不自然さを見る。

## 想定する不正パターン

- **取消抜き取り**: 売上直後に同額を取り消す Void の反復、Void率が高く現金に偏る
- **返品抜き取り**: 返金額比率が高い、レシート無し返品、現金返金、同額返品の反復
- **ポイント不正**: 会員不在のポイント付与/利用、特定会員 ID への利用偏り、ポイント利用の現金売上集中、利用直後の取消
- レジ開放（No-Sale）の多発、値引き乱用、閑散帯（営業時間外）への取消・返品の集中

## サンプルデータ

画面からダウンロードできるサンプルは次の5つです（従業員1名 E001・2026-05 の14営業日）。

- `normal-typical.csv` — 通常営業。取消/返品が低率で自然にばらつく正常データ
- `normal-busy-weekend.csv` — 週末に取引・返品が増えるが比率は正常域に収まる正常データ
- `fraud-void-skim.csv` — 売上直後の同額 Void を複数日で反復する不正データ（取消抜き取り）
- `fraud-refund-no-receipt.csv` — レシート無し・高額・同額の現金返品が集中する不正データ（返品抜き取り）
- `fraud-point-abuse.csv` — 非会員売上に特定会員 ID でポイントを付与/利用する不正データ（ポイント不正）

サンプルは `tools/generate_samples.py` で再現生成できます。

```pwsh
python tools/generate_samples.py
```

## 設定

`appsettings.json` に検証用の Foundry エンドポイントとデプロイ名を設定済みです。

```json
{
  "PosChecker": {
    "UploadPath": "./upload",
    "PreviewRowCount": 12,
    "SaleThenVoidWindowSeconds": 180,
    "RepeatedRefundMinOccurrences": 2,
    "BusinessHoursStartHour": 9,
    "BusinessHoursEndHour": 21,
    "HighValueReturnAmount": 5000,
    "RedeemConcentrationThreshold": 0.5
  },
  "Foundry": {
    "Endpoint": "https://foundry-usausa-resource.services.ai.azure.com",
    "ApiKey": "",
    "ChatDeployment": "gpt-5.4-mini"
  }
}
```

API キーはコミットせず、`.NET user-secrets` で設定してください。

```pwsh
cd PosChecker
dotnet user-secrets set "Foundry:ApiKey" "<受領したAPIキー>"
```

## CSV フォーマット

UTF-8、ヘッダ付きの CSV を想定します。1行 = 1取引。

```csv
BusinessDate,CashierId,TransactionId,Time,Type,Amount,ItemCount,PaymentMethod,DiscountAmount,PointsEarned,PointsRedeemed,MembershipId,OriginalTransactionId,HasReceipt
2026-05-01,E001,T0001,09:12:33,Sale,1280,3,Cash,0,12,0,M1001,,
2026-05-01,E001,T0002,09:15:02,Sale,640,1,Credit,0,6,0,,,
2026-05-01,E001,T0003,09:21:48,Void,640,1,Credit,0,0,0,,T0002,
2026-05-01,E001,T0050,17:44:10,Return,3980,2,Cash,0,0,0,,T0031,true
```

- `Type`: `Sale`(売上) / `Void`(取消) / `Return`(返品) / `NoSale`(レジ開放)
- `PaymentMethod`: `Cash` / `Credit` / `QR` / `GiftCard` / `Other`
- `Amount`: 商品総額（グロス、非負の円）。`PointsEarned` / `PointsRedeemed` は 1pt=1円。
- `OriginalTransactionId`: Void / Return が参照する元取引。`HasReceipt`: 返品のレシート有無（`true`/`false`）。

## 起動方法

```pwsh
cd PosChecker
dotnet run
```

ブラウザで `http://localhost:5142` を開きます。

## 画面の見方

- **不正確率**: 従業員全体に対する LLM のスコア（0〜100）
- **日別スコア**: 高スコア日から並ぶ。1営業日＝1従業員の判定単位。機械集計の特徴・疑わしい取引・LLM理由を表示
- **取引種別 / 支払方法の構成**: 件数・金額・構成比
- **シーケンス異常**: 売上直後取消・ポイント利用直後取消・同額返品の反復
- **CSVプレビュー**: 先頭数行

## ルール検証（Foundry）

`tools/validate_foundry.py` は C# の集計と同じ camelCase ペイロード＋ `Prompts/pos_analyzer.txt` を Python で再現し、各サンプルを Foundry に POST して `overall_score` を確認します。API キーは環境変数からのみ読みます。

```pwsh
$env:FOUNDRY_API_KEY="<APIキー>"; python tools/validate_foundry.py; Remove-Item Env:\FOUNDRY_API_KEY
```

スコア基準（temperature:0、`normals < 40` かつ `frauds ≥ 70` の分離を回帰確認）:

| サンプル | overall_score |
|---|---|
| normal-typical | 約28 |
| normal-busy-weekend | 約28 |
| fraud-void-skim | 約78 |
| fraud-refund-no-receipt | 約82 |
| fraud-point-abuse | 約82 |

## 補足

- 本アプリケーションはデモ用途です。判定ロジックは「入力が特徴的なパターンを持つか」を説明しやすいように設計しています。
- 実運用では端末ログ・監視カメラ・ドロワー開閉センサ・シフト表との突合で精度を上げられます。
- 設計の詳細は `Document/01-overview.md`〜`04-implementation-plan.md` を参照してください。
