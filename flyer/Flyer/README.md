# Flyer

チラシ画像と商品マスタを比較して、広告価格の差異をチェックするコンソールアプリケーション。

## 構成

- **Microsoft Foundry (Chat / マルチモーダル)**
  - チラシ画像から商品名と価格を JSON で抽出
  - 検索でヒットしたマスタ候補と突合し、価格差を算出
- **Microsoft Foundry (Embeddings)**
  - 商品名のベクトル化
- **Azure AI Search (ベクトル ストア)**
  - 公式の商品名/価格マスタを保持し、チラシから抽出した曖昧な商品名で類似検索

## 必要な環境

### ツール

- .NET 10 SDK
- Azure CLI（`DefaultAzureCredential` でアクセスする場合）

### Azure リソース

1. **Microsoft Foundry リソース**（`*.services.ai.azure.com` のエンドポイント）
   - チャットモデルのデプロイ（例: `gpt-4o`、画像入力対応のもの）
   - 埋め込みモデルのデプロイ（例: `text-embedding-3-small`、1536 次元）
2. **Azure AI Search サービス**（Basic 以上推奨）
3. 認証
   - 推奨: `Azure AI Developer` および `Search Index Data Contributor` ロールを実行ユーザーに付与し、`DefaultAzureCredential` を使用
   - もしくは API キー認証

## 設定

接続情報は `appsettings.json` または .NET User Secrets から取得します。
本番環境では User Secrets / 環境変数 / Azure Key Vault などを利用し、リポジトリには値をコミットしないでください。

### appsettings.json の構造

```json
{
  "Foundry": {
    "Endpoint": "https://YOUR-FOUNDRY-RESOURCE-NAME.services.ai.azure.com",
    "ApiKey": "",
    "ChatDeployment": "gpt-4o",
    "EmbeddingDeployment": "text-embedding-3-small",
    "EmbeddingDimensions": 1536
  },
  "AzureAISearch": {
    "Endpoint": "https://YOUR-SEARCH-RESOURCE.search.windows.net",
    "ApiKey": "",
    "IndexName": "flyer-products"
  }
}
```

`ApiKey` を空にすると `DefaultAzureCredential`（Azure CLI、Visual Studio、Managed Identity など）でアクセスします。

### User Secrets で設定する例

```pwsh
cd Flyer
dotnet user-secrets init
dotnet user-secrets set "Foundry:Endpoint" "https://<resource>.services.ai.azure.com"
dotnet user-secrets set "Foundry:ChatDeployment" "gpt-4o"
dotnet user-secrets set "Foundry:EmbeddingDeployment" "text-embedding-3-small"
dotnet user-secrets set "AzureAISearch:Endpoint" "https://<search>.search.windows.net"
dotnet user-secrets set "AzureAISearch:IndexName" "flyer-products"
# 必要に応じて API キー
dotnet user-secrets set "Foundry:ApiKey" "<key>"
dotnet user-secrets set "AzureAISearch:ApiKey" "<key>"
```

## マスタ CSV のフォーマット

UTF-8、ヘッダ付きの CSV。`Id` は省略可（省略時は自動採番）。

```csv
Id,Name,Price
P0001,コカ・コーラ 500ml,150
P0002,ポテトチップス うすしお 60g,128
```

## 使い方

### 1. マスタデータの投入

```pwsh
dotnet run --project Flyer -- load --file .\master.csv
```

初回実行時に Azure AI Search のインデックス（`AzureAISearch:IndexName`）が自動作成されます。

### 2. チラシ画像のチェック

```pwsh
dotnet run --project Flyer -- check --file .\flyer.jpg --top 3
```

チラシ画像から抽出した各商品について、ベクトル検索で取得した上位 N 件のマスタ候補と突合し、表形式で差額を表示します。

## プロジェクト構成

```
Flyer/
├── Program.cs              # DI / 設定 / ホスト構築
├── appsettings.json        # 接続情報のテンプレート
├── Commands/               # System.CommandLine ベースの CLI コマンド
│   ├── LoadCommand.cs
│   └── CheckCommand.cs
├── Options/                # 設定バインディング用の POCO
└── Services/               # ビジネスロジック（DI 経由で Command から利用）
    ├── FlyerImageReader.cs        # 画像 → 商品/価格 (LLM)
    ├── MasterCsvLoader.cs         # CSV 読み込み
    ├── ProductVectorStore.cs      # Azure AI Search ベクトルストア
    ├── PriceDifferenceAnalyzer.cs # 価格差判定 (LLM)
    └── Models/                    # データモデル
```
