# PosChecker 設計書 ① 概要・アーキテクチャ

小売店の従業員による **POS レジ操作不正** を、トランザクションログと LLM を用いて検出するデモアプリケーション（`PosChecker`）の設計書です。既存の `inspector`（InspectorChecker）の作りを踏襲します。

## 1. 目的

レジ担当者が日々の営業の中で行う以下のような不正操作を、トランザクションログから検知する。

- 売上計上後の **取消（Void）** による現金抜き取り
- 架空・水増しの **返品（Return）** による現金抜き取り
- **ポイント** の付け替え・不正付与・不正利用（他人のポイントを客の現金取引に差し込み差額を着服）
- レジ開放（No-Sale）の多発、値引きの乱用 など

「人間が説明できる特徴」を機械集計で抽出し、LLM に最終的な不正確率と理由を判定させる。実運用の不正会計エンジンではなく、**判定ロジックを説明しやすく見せるデモ**である点は inspector と同じ。

## 2. 判定単位とスコープ（前提）

- **判定単位 = 1 営業日 × 1 従業員（レジ担当者）**。
- 入力は **「従業員 1 名分・複数営業日」** のトランザクション CSV を 1 ファイルとしてアップロードする（inspector の「調査員 1 名 × 11 日分」に対応）。
- 出力は **日別スコア（= 1 日 1 人の判定単位）** の一覧と、従業員全体の総合スコア。
- 1 取引（トランザクション）は **取引ヘッダ単位の 1 行**（明細は金額・点数に集約）として扱い、CSV をフラットに保つ。

> この前提はレビューで変更可能。「複数従業員 × 複数日」へ拡張する場合は、`CashierId` でグルーピングしてから日別集計する層を 1 段増やすだけで対応できる構造にしておく（→ `04-implementation-plan.md`）。

## 3. inspector との対応関係

| inspector | PosChecker | 備考 |
|---|---|---|
| 巡回調査 CSV（漏れ電流 mA） | トランザクションログ CSV | 1 行の意味が「1 顧客の測定値」→「1 取引」に変わる |
| `SurveyRecord` | `TransactionRecord` | 1 行のレコード |
| `InspectionFeatureSummaryBuilder` | `PosFeatureSummaryBuilder` | 決定論的な機械集計 |
| `InspectionFraudAnalyzer` | `PosFraudAnalyzer` | LLM 呼び出し（Foundry） |
| `InspectorCheckerService` | `PosCheckerService` | オーケストレーション |
| 漏れ電流の値分布／日別統計／テンプレート再利用 | 取引種別構成／日別の Void・Return・ポイント特徴量／シーケンス異常 | 集計の中身を POS 向けに差し替え |
| `overall_score` + `daily_results[]` | 同左 | 出力 JSON 構造は踏襲 |

## 4. アーキテクチャ

```text
トランザクションログ CSV（従業員1名・複数営業日）
    ↓
TransactionCsvLoader … 行を TransactionRecord に変換・検証
    ↓
PosFeatureSummaryBuilder … 決定論的に集計
    ├─ 取引種別ごとの件数・金額・構成比
    ├─ 日別（=1人1日）の Void率 / Return率 / レシートなし返品率
    ├─ ポイント利用・付与・調整の集計（会員不在・特定会員偏り・現金集中）
    ├─ 時間帯分布・シーケンス異常（売上直後取消・ポイント利用直後取消など）
    └─ 同一金額/同一元取引への返金繰り返し
    ↓
PosFraudAnalyzer … 集計(camelCase) + 生取引を Foundry(gpt-5.4-mini) に送信
    ↓（temperature:0 / response_format:json）
PosAnalysisResult … overall_score / daily_results[] / suspicious_patterns
    ↓
Blazor 画面で表示（総合スコア・日別スコア・特徴量・注目パターン）
```

機械集計（決定論）と LLM 判定（説明＋スコア）の **ハイブリッド** という inspector の中核思想をそのまま採用する。

## 5. 技術スタック（inspector と同一）

- .NET 10 / Blazor Server（`Microsoft.NET.Sdk.Web`）
- `Microsoft.Extensions.AI` の `IChatClient`（`Azure.AI.OpenAI` 経由で Foundry に接続）
- `CsvHelper`（CSV 読み込み）
- `Serilog`（ロギング）
- `Nullable=enable` / `ImplicitUsings=enable` / `TreatWarningsAsErrors=true`
- コーディング規約は `inspector/AGENTS.md` に準拠（メンバ変数に `_` 接頭辞を付けない・ビルド警告ゼロ・警告抑制は事前確認）

## 6. LLM 設定（Foundry）

```json
{
  "Foundry": {
    "Endpoint": "https://foundry-usausa-resource.services.ai.azure.com",
    "ApiKey": "",
    "ChatDeployment": "gpt-5.4-mini"
  }
}
```

- `temperature: 0`、`response_format: { "type": "json_object" }` を使用。
- API キーはコミットせず `.NET user-secrets`（`Foundry:ApiKey`）で設定する。

## 7. ドキュメント構成

- `01-overview.md`（本書）— 概要・アーキテクチャ・スコープ
- `02-transaction-data.md` — LLM に喰わせるトランザクションデータ構造（CSV 列定義・取引種別・支払方法・ポイント・LLM ペイロード・C# モデル）
- `03-fraud-rules.md` — 不正判定ルール・機械集計の特徴量・スコアリング・プロンプト設計
- `04-implementation-plan.md` — プロジェクト構成・クラス設計・サンプルデータ・実装手順・検証方法
