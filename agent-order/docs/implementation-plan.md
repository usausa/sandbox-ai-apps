# 自動発注エージェント デモ — 実装プラン v1.0

> 対象仕様: `docs/spec-draft.md` v0.5（Fix済み）
> 作成日: 2026-06-24
> 方針: **MVP = 中核3画面＋基盤一式**（画面1/2/3＋共通レイアウト＋仮想時計＋販売/入荷シミュレーション＋Dapper/DB＋エージェント）。画面4/5/6 は次フェーズ。

---

## 0. 全体方針

- **ボトムアップに積み上げる**: データ層 → ドメイン → シミュレーション → エージェント → UI → Aspire/可観測性 → テスト。各層は下位に依存するため、この順で「常に動く」状態を保つ。
- **垂直スライスで早期に通す**: Phase 4 終了時点で「チャットで指示→提案→承認→入荷」までCLI/最小UIで疎通させ、以降を肉付け。
- **決定論を徹底**: 仮想時計・シード・販売シミュレーションはすべて固定シードで再現可能に。
- 規模感は **S（〜半日）／M（1〜2日）／L（3日〜）** の相対見積もりで付記（1人想定）。

### 依存関係（概略）

```
P0 Scaffold
   └─ P1 Data(Dapper/DB/Seed)
         ├─ P2 Domain(IClock/Policy/DemandCalc/OrderService)
         │      ├─ P3 Simulation(BackgroundServices)
         │      └─ P4 Agent(Foundry/Tools/AF機能)
         │             └─ P5 Web UI(MudBlazor 画面1/2/3)
         └─ P6 Aspire/Metrics（P3〜P5と並行可）
P7 Tests（各Pに追従）   P8 デモ調整（最後）
```

---

## Phase 0 — ソリューション雛形 〔S〕

- `AutoOrderAgent.sln` を作成し、`src/` `tests/` を構成（仕様 §4.3 のとおり）。
  - src: `Web`(Blazor Server) / `Agent` / `Domain` / `Data` / `ServiceDefaults` / `AppHost`
  - tests: `Domain.Tests` / `Data.Tests` / `Agent.Tests`
- `Directory.Build.props` / `Directory.Packages.props`（中央集約パッケージ管理）で .NET 10・Nullable・LangVersion を統一。
- NuGet 追加（バージョンは実装時に最新確定）:
  - `Microsoft.Agents.AI`, `Microsoft.Extensions.AI`, Foundry/OpenAI 系コネクタ
  - `Dapper`, `Microsoft.Data.Sqlite`
  - `MudBlazor`
  - Aspire: `Aspire.Hosting.AppHost`, ServiceDefaults テンプレート
  - テスト: `xUnit`, `Shouldly`, `NSubstitute`, `Microsoft.NET.Test.Sdk`
- **完了条件**: `dotnet build` / `dotnet test`（空テスト）が通る。AppHost から空のWebが起動する。

---

## Phase 1 — データ層（Dapper / SQLite） 〔M〕

仕様 §5。**Dapper は本プロジェクトに閉じる**。

1. **モデル（POCO）**: `Product` `Category` `Supplier` `SupplierProduct` `Inventory` `SalesHistory` `WeatherForecast` `CalendarEvent` `PurchaseOrder` `PurchaseOrderLine` `ApprovalRecord` `ReorderSettings` `AgentRun` `AgentRunStep` `Store`。enum は §5 準拠。
2. **`schema.sql`（DDL）**: 全テーブル＋一意制約。起動時に適用。**WAL 有効化** (`PRAGMA journal_mode=WAL;`)。
3. **型ハンドラ**: `DateOnly`/`DateTimeOffset`↔ISO文字列、enum↔TEXT。**金額は整数（円）保存**。
4. **リポジトリ interface（Domain側に定義）＋ Dapper 実装（Data側）**:
   - 読み取り: 在庫・販売実績(期間)・天気・イベント・仕入先カタログ・設定
   - 書き込み: PO作成/状態更新、在庫更新、ApprovalRecord、AgentRun/Step
   - 集計: 未入荷PO（Status=Ordered）からの入荷待ち再計算（`IncomingQty` 導出）
5. **接続管理**: 短命接続、書き込みは直列化（lock or 単一書き込みキュー）。
6. **`DbSeeder`**: マスタ→在庫→販売実績(過去90日)→天気/イベント(先N日)→設定。固定シード・VirtualEpoch確定。再シード操作。

- **完了条件**: `Data.Tests` で CRUD・状態遷移・期限切れ抽出・入荷待ち集計が緑。

---

## Phase 2 — ドメインロジック 〔M〕

外部依存ゼロ。最もテストしやすい中核。

1. **`IClock` / `VirtualClock`**（§3.7/§4.5）: 仮想起点＋（現実経過×倍率24）→ 現在仮想日時。倍率切替・一時停止・手動スキップ。DIシングルトン。
2. **需要計算 `DemandCalculator`**（§8.5 を確定 → 下記式）:
   - 予測日次需要 `D = baseDaily × f_weather × f_dow × f_event`
   - 必要在庫 `Need = D × (LeadTimeDays + ReviewIntervalDays) + SafetyStock`
   - 発注必要量 `Q = max(0, Need − (OnHand + Incoming))`
   - ロット丸め `Qorder = ceil(Q / CaseQty) × CaseQty`、`MOQ` 適用
   - `baseDaily` = 直近実績の移動平均（例: 直近14日）
3. **承認ポリシー `ApprovalPolicyEvaluator`**（§3.3）: 入力（金額・数量・倍率・カテゴリの常時承認・新規/休眠）→ 出力 `Auto / Manual`。閾値は `ReorderSettings`（上限5万/倍率2.0/期限24h）。
4. **発注ドメインサービス `OrderService`**（§3.3.1 の確定処理を集約）:
   - `CreateDraft(...)` 提案生成＋ポリシー評価 → Auto は即 `Ordered`、Manual は `PendingApproval`
   - `Approve / Reject / AdjustQty`（画面3・チャット承認の両経路から呼ばれる単一窓口）
   - 確定時: `ExpectedDeliveryDate = 仮想現在日 + LeadTime`、在庫の `Incoming` 再計算

- **完了条件**: `Domain.Tests` で 仮想時計換算・各係数・ポリシー分岐・丸め・状態遷移が緑。

### 需要係数の初期値（§8.5 で確定）

| 係数 | 条件 | 値（例） |
|---|---|---|
| `f_weather`（気温感応品） | 最高気温≥30℃ | 1.5 ／ 25–30℃: 1.2 ／ 雨: 0.8 ／ その他: 1.0 |
| `f_dow` | 土日 | 1.3 ／ 金: 1.1 ／ 平日: 1.0 |
| `f_event` | ImpactLevel=High | 1.5 ／ Medium: 1.25 ／ Low: 1.1 ／ なし: 1.0 |
| `ReviewIntervalDays` | 日次発注前提 | 1 |

> 非感応品は `f_weather=1.0`。販売シミュレーション(P3)も同じ係数＋小ノイズを ground truth に使う。

---

## Phase 3 — シミュレーション（BackgroundService 群） 〔M〕

仕様 §3.4/§3.6/§3.6.1。すべて `IClock` 基準で仮想時刻を監視。

1. **`SalesSimulationService`**（仮想0:00）: 各商品 `actual = round(baseDaily × f_weather × f_dow × f_event × noise)` ぶん `OnHand` を減算（0未満は欠品）。`SalesHistory` に当日追記。
2. **`RollingDataService`**（日次）: 天気・イベントを「仮想現在日＋先N日(14)」まで補充。
3. **`DailyOrderCheckService`**（仮想6:00）: エージェント（P4）をバッチ起動し、過少在庫を提案化。※P4完成までは `OrderService` 直呼びのスタブで先行可。
4. **`ExpiryService`**（仮想1h毎）: 期限超過 `PendingApproval` を `Expired`（§3.3 放置挙動）。
5. **`InboundService`**（仮想1h毎）: `ExpectedDeliveryDate` 到達 PO を入荷処理（`OnHand += qty`、`Received`、`Incoming` 再計算）。

- **完了条件**: 仮想時間を手動スキップすると 在庫減→発注→入荷→期限切れ が再現（UIなしでログ確認）。

---

## Phase 4 — エージェント（Microsoft Agent Framework） 〔L〕

仕様 §3.5/§4.2/§4.6。デモ目的②の中心。

1. **Foundry 接続**: `IChatClient`（Azure AI Foundry, `gpt-5.4-mini`）。設定は user secrets/環境変数。
2. **ツール（`AIFunctionFactory.Create`）**: `GetInventory` `GetSalesHistory` `GetWeatherForecast` `GetEventCalendar` `GetSupplierCatalog` `GetCurrentDate` `CalculateReorderSuggestion` `CreateOrderDraft` `PlaceOrder`。各ツールは Domain サービス/リポジトリへ委譲。
3. **エージェント生成**: `ChatClientAgent`。**システムプロンプト**に発注ドメイン限定のガードレール（§3.8）と数量決定方針（ツール補助＋LLM判断）。
4. **採用する任意機能（4つ）**:
   - **ミドルウェア**: ツール/エージェント呼び出しのロギング＋メトリクス計測（§4.7 Meter 発火）。
   - **OpenTelemetry 計装**: Framework内蔵の計装を有効化。
   - **会話セッション履歴**: チャットのスレッド継続。実行を `AgentRun`/`AgentRunStep` に記録。
   - **構造化出力**: 発注提案を型付き構造で受領し明細へ変換。
5. **承認必須ツール**: `PlaceOrder` を `ApprovalRequiredAIFunction` 化（チャット即時承認の経路、§3.3.1）。確定は `OrderService` に集約。
6. **ストリーミング**: `RunStreamingAsync`＋ツール呼び出しイベントを上位（UI）へ流す抽象を用意。

- **完了条件**: `Agent.Tests`（`IChatClient` モック）でツール入出力・承認分岐・ガードレール方針を検証。実 Foundry で1往復の手動疎通。

---

## Phase 5 — Web UI（MudBlazor / 画面1・2・3） 〔L〕

仕様 §3.1（CloudManager 参考・左メニュー・`Dense`・日本語のみ）。

1. **共通レイアウト**: `MainLayout`（`MudLayout`/`MudAppBar`/`MudDrawer`＋`MudNavMenu`）、コンパクトテーマ。
2. **画面1 ダッシュボード**: 過少在庫・承認待ち・期限切れ・自動承認済み・入荷予定・最近の発注（`MudCard`/`MudAlert`/`MudChip`）。
3. **画面2 エージェント・コンソール**: チャットUI、ストリーミング表示、ツール呼び出しのインライン可視化（P4のイベント抽象を購読）。
4. **画面3 承認**: 承認待ちキューを `MudDataGrid`（Dense）。明細・根拠・**残り承認期限カウントダウン**、承認/却下/数量編集/コメント → `OrderService`。
5. **デモ操作（簡易）**: 仮想時計の倍率/一時停止/手動スキップ、再シード（設定の最小版。フル設定画面6は次フェーズ）。

- **完了条件**: §7 ウォークスルー（1〜8）がUI上で一通り再現できる。

---

## Phase 6 — Aspire / メトリクス 〔M〕（P3〜P5と並行可）

仕様 §4.7（**AppHost＋Web一体**、バッチはWeb内 BackgroundService）。

1. **`ServiceDefaults`**: OpenTelemetry（trace/metrics/logs）・ヘルスチェック共通化。
2. **`AppHost`**: Web＋ダッシュボードをオーケストレーション。SQLite はファイル接続文字列を構成で注入。
3. **業務メトリクス Meter**: §4.7 の `orders.*` `agent.*` `inventory.stockout` 等を発火。
4. **ダッシュボード確認**: 提案/自動・手動承認/期限切れ/入荷/ツール呼び出しが可視化される。

- **完了条件**: Aspire ダッシュボードでエージェント＋業務メトリクスが見える。

---

## Phase 7 — テスト 〔各Phaseに追従, 計M〕

仕様 §4.8。xUnit / Shouldly / NSubstitute。LLM 非依存。

- `Domain.Tests`: 仮想時計・需要係数・ポリシー分岐・丸め/MOQ・状態遷移。
- `Data.Tests`: 実SQLite（一時ファイル）に `schema.sql` 適用、CRUD・期限切れ抽出・入荷待ち集計・在庫更新。
- `Agent.Tests`: 各 `AIFunction` 入出力、`PlaceOrder` 承認分岐、ガードレール方針（`IChatClient` モック）。
- **CI**: `dotnet test` が外部依存・APIキーなしで完結。

---

## Phase 8 — デモデータ調整・通し確認 〔S〕

- シードを調整し、**初回起動・初回6:00バッチで提案が出る**／放置で期限切れ／入荷で回復、が映えるように。
- §7 ウォークスルーを通しでリハーサル。倍率（24倍）でのテンポを確認。
- README（起動手順、Foundry設定、仮想時計操作、再シード）整備。

---

## リスクと対策

| リスク | 対策 |
|---|---|
| Foundry/`gpt-5.4-mini` のSDK・接続仕様が想定と差異 | P4 早期に最小疎通。`IChatClient` 抽象でモデル差異を吸収 |
| `ApprovalRequiredAIFunction` の挙動が想定と異なる | 承認の正はアプリ側キュー（§3.3.1）。Framework承認は補助経路に限定しリスク隔離 |
| SQLite 並行書き込み競合 | WAL＋書き込み直列化。BackgroundService の書き込みを単一経路に |
| LLM の数量がぶれる/暴走 | `CalculateReorderSuggestion` で推奨値を提示し範囲を誘導。構造化出力で明細を確定 |
| 仮想時間進行でデータ地平線を追い越す | `RollingDataService` で先N日を常時補充（P3） |

---

## 次アクション

1. 本プランの **Phase 0（雛形）から着手** してよいか確認。
2. Foundry の **エンドポイント／`gpt-5.4-mini` デプロイ名／認証**（user secrets キー）を準備（P4 で必要、P0〜P3 は不要）。
