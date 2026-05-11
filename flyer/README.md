# FlyerChecker

チラシ画像と商品マスタを比較して、広告価格の差異をチェックするBlazor Serverアプリケーション。

## アーキテクチャ

```
チラシ画像 (ブラウザD&D)
    ↓
Microsoft Foundry (gpt-4o) ── 商品名・価格を JSON 抽出
    ↓
Azure AI Search ── 商品名でベクトル類似検索（上位5件）
    ↓
Microsoft Foundry (gpt-4o) ── マスタ候補と照合・価格差を判定
    ↓
結果を画面に表示
```

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
cd FlyerChecker
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
Id,Name,Price,Category
P0001,コカ・コーラ 500ml,150,飲料
P0002,ポテトチップス うすしお 60g,128,スナック
```

## 使い方

### 1. アプリケーションの起動

```pwsh
cd FlyerChecker
dotnet run
```

ブラウザで `http://localhost:5120` を開きます。

### 2. マスタデータの投入

画面上部の「マスタCSV登録」ボタンをクリックして CSV ファイルを選択します。  
初回は Azure AI Search のインデックスが自動作成されます。

### 3. チラシ画像のチェック

チラシ画像（PNG / JPEG / WEBP）をドラッグ＆ドロップするか、ドロップゾーンをクリックして選択します。  
処理が開始され、商品ごとの結果が順次表示されます。

## 結果の見方

| 列 | 説明 |
|----|------|
| チラシ商品名 | チラシ画像から抽出した商品名 |
| チラシ価格 | チラシ画像から抽出した価格 |
| マスタ商品名 | AI Search で最も一致した商品名 |
| マスタ価格 | マスタの正式価格 |
| 差額 | ✅ 一致 / ❌ チラシが高い / ⚠️ チラシが安い |
| コメント | LLM による判定コメント |

## プロジェクト構成

```
FlyerChecker/
├── Components/
│   ├── Layout/
│   │   ├── MainLayout.razor        # アプリシェル・ヘッダー
│   │   └── ReconnectModal.razor    # 再接続ダイアログ
│   └── Pages/
│       ├── Check.razor             # メイン画面
│       └── Check.razor.cs          # コードビハインド
├── Models/                         # データモデル
├── Services/
│   ├── FlyerCheckerService.cs      # チェック処理オーケストレーション
│   ├── FlyerImageReader.cs         # チラシ画像解析（Foundry）
│   ├── MasterCsvLoader.cs          # CSV ローダー
│   ├── PriceDifferenceAnalyzer.cs  # 価格差異分析（Foundry）
│   └── ProductService.cs           # Azure AI Search 操作
├── Settings/                       # 設定クラス
├── Prompts/
│   ├── flyer_reader.txt            # チラシ解析プロンプト
│   └── price_analyzer.txt          # 価格差異分析プロンプト
├── appsettings.json
├── Log.cs                          # LoggerMessage 定義
└── Program.cs
```
