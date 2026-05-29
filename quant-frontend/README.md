## 1. 環境準備
在你的電腦上，請確保已安裝以下工具：

1. Git (用於版本控制與 GitHub 同步)
2. Azure CLI (用於執行自動化建置腳本)
3. Node.js (用於執行前端 Vue)
4. Docker Desktop (本機測試用，非必備)
5. Fork 專案 (https://github.com/bst0529/QuantTradingSystem) 並下載

## 2. 基礎設施全自動建置 (Azure)
打開你的 PowerShell，登入 Azure：
``` 
# PowerShell
az login --use-device-code
```
接著，直接複製貼上並執行以下整段腳本。這段腳本會幫你建立：資源群組、容器登錄 (ACR)、儲存體 (Azure Files 雲端硬碟)、環境大廳，以及掛載好資料庫的 API 容器！(耗時約 8 ~ 10 分鐘)
``` 
# PowerShell
# ==========================================
# 1. 設定全域變數 (可依個人喜好修改後綴)
# ==========================================
$SUFFIX = "2026" # 避免名稱重複，可改為任意數字
$RG = "rg-quant-trading-$SUFFIX"
$LOC = "koreacentral"
$ACR = "registryquant$SUFFIX"
$STA = "stquanttrading$SUFFIX"
$SHARE = "sqldata"
$ENV = "env-quant-$SUFFIX"
$API = "quant-api"

# ==========================================
# 2. 建立資源群組與容器登錄 (ACR)
# ==========================================
Write-Host "正在建立基礎架構..."
az group create --name $RG --location $LOC
az acr create --resource-group $RG --name $ACR --sku Basic --admin-enabled true

# ==========================================
# 3. 建立儲存體與檔案共用 (SQLite 雲端硬碟)
# ==========================================
Write-Host "正在建立雲端儲存體..."
az storage account create --resource-group $RG --name $STA --location $LOC --sku Standard_LRS
$CONN = az storage account show-connection-string --resource-group $RG --name $STA --output tsv
az storage share create --name $SHARE --connection-string $CONN
$STA_KEY = az storage account keys list --resource-group $RG --account-name $STA --query "[0].value" --output tsv

# ==========================================
# 4. 建立 Container Apps 環境並掛載硬碟
# ==========================================
Write-Host "正在建立 Container Apps 環境 (這需要幾分鐘)..."
az extension add --name containerapp --upgrade
az containerapp env create --name $ENV --resource-group $RG --location $LOC

az containerapp env storage set `
    --access-mode ReadWrite `
    --azure-file-account-name $STA `
    --azure-file-account-key $STA_KEY `
    --azure-file-share-name $SHARE `
    --storage-name quant-storage `
    --name $ENV `
    --resource-group $RG

# ==========================================
# 5. 產生 CI/CD 所需的憑證與密碼 (重要！！)
# ==========================================
Write-Host "正在產生 GitHub Actions 部署憑證..."
$SUB_ID = az account show --query id --output tsv
$AZURE_CREDENTIALS = az ad sp create-for-rbac --name "quant-deploy-sp-$SUFFIX" --role contributor --scopes /subscriptions/$SUB_ID/resourceGroups/$RG --sdk-auth

$ACR_USER = az acr credential show --name $ACR --query "username" --output tsv
$ACR_PASS = az acr credential show --name $ACR --query "passwords[0].value" --output tsv

Write-Host "=========================================================="
Write-Host "雲端基礎設施建立完畢！請將以下資訊新增至 GitHub Secrets："
Write-Host "=========================================================="
Write-Host "Secret 名稱: ACR_USERNAME"
Write-Host "值: $ACR_USER"
Write-Host "----------------------------------------------------------"
Write-Host "Secret 名稱: ACR_PASSWORD"
Write-Host "值: $ACR_PASS"
Write-Host "----------------------------------------------------------"
Write-Host "Secret 名稱: AZURE_CREDENTIALS"
Write-Host "值 (複製整段 JSON 包含大括號):"
Write-Host $AZURE_CREDENTIALS
Write-Host "=========================================================="
```

## 3. 設定 GitHub Actions (CI/CD)
請到你的 GitHub 專案網頁：
1. 點擊 Settings -> 左側選單找 Secrets and variables -> Actions。
2. 點擊 New repository secret，將剛剛 PowerShell 腳本最後印出來的三個資訊 (ACR_USERNAME, ACR_PASSWORD, AZURE_CREDENTIALS) 加進去。

接著，在專案中建立 CI/CD 工作流檔案：
建立路徑： .github/workflows/deploy-backend.yml
檔案內容：
```
#YAML
name: Deploy API to Azure Container Apps

on:
  push:
    branches: [ "main" ]
    paths:
      - 'src/**'
      - '.github/workflows/**'
  workflow_dispatch: # 加上這行，就能在 GitHub 網頁上手動點擊執行按鈕

# 請確保這裡的變數名稱與你 PowerShell 腳本中的名稱一致
env:
  ACR_NAME: registryquant2026 # 若有改後綴，請同步修改
  IMAGE_NAME: quant-api
  CONTAINER_APP_NAME: quant-api
  RESOURCE_GROUP: rg-quant-trading-2026 # 若有改後綴，請同步修改

jobs:
  build-and-deploy:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout Code
        uses: actions/checkout@v3

      - name: Log in to Azure Container Registry (ACR)
        uses: docker/login-action@v2
        with:
          registry: ${{ env.ACR_NAME }}.azurecr.io
          username: ${{ secrets.ACR_USERNAME }}
          password: ${{ secrets.ACR_PASSWORD }}

      - name: Build and Push API Docker Image
        uses: docker/build-push-action@v4
        with:
          context: .
          file: ./src/QuantTrading.Api/Dockerfile
          push: true
          tags: ${{ env.ACR_NAME }}.azurecr.io/${{ env.IMAGE_NAME }}:${{ github.sha }}

      - name: Azure Login
        uses: azure/login@v1
        with:
          creds: ${{ secrets.AZURE_CREDENTIALS }}

      - name: Deploy to Azure Container App
        uses: azure/CLI@v1
        with:
          azcliversion: latest
          inlineScript: |
            az containerapp up \
              --name ${{ env.CONTAINER_APP_NAME }} \
              --resource-group ${{ env.RESOURCE_GROUP }} \
              --image ${{ env.ACR_NAME }}.azurecr.io/${{ env.IMAGE_NAME }}:${{ github.sha }} \
              --target-port 8080 \
              --ingress external \
              --env-vars "ConnectionStrings__DefaultConnection=Data Source=/app/data/quant_data.db;"
            
            # 掛載儲存體 (Azure CLI containerapp up 尚未完整支援 volume，用 update 補上)
            az containerapp update \
              --name ${{ env.CONTAINER_APP_NAME }} \
              --resource-group ${{ env.RESOURCE_GROUP }} \
              --set-env-vars "ConnectionStrings__DefaultConnection=Data Source=/app/data/quant_data.db;"
```


> 魔法說明：只要你把程式碼 Push 到 GitHub 的 main 分支，這個腳本就會自動打包最新的 Docker Image、上傳，並通知 Azure 把新的 API 跑起來，完全不需要手動操作！

## 4.建立 Azure AI 大模型
0. 如果 Azure 訂閱帳號目前「尚未解鎖」使用 AI 服務（Cognitive Services）的權限 -- 略過

```
# PowerShell
# 1. 啟用 Cognitive Services 權限
az provider register --namespace Microsoft.CognitiveServices

# 2. 確認啟用狀態 (需等待 1~3 分鐘)
az provider show --namespace Microsoft.CognitiveServices --query "registrationState"
```

1. 建立 Azure AI 服務並部署 GPT-4o/大模型 -- 不可行

> <span style="color:red;">RequestDisallowedByAzure: This policy maintains a set of best available regions where your subscription can deploy resources. 是 Azure 最嚴格的訂閱級別地區鎖定政策 (Policy Lock)。</span>

2. 改用官方 OpenAI API -- 不可行
> <span style="color:red;">帳號裡面沒有預先儲值（Prepaid）金額，伺服器就會直接把你擋在門外並回傳 429 錯誤。</span>

3. 改用 Groq
### 步驟 4-1：取得 Groq API 金鑰 (免費)
   1. 請前往 GroqConsole (console.groq.com)。
   2. 登入（可以用 Google 帳號直接登入）。
   3. 在上方選單點擊 API Keys。
   4. 點擊右上角的 Create API Key，給它隨便取個名字（例如 QuantTrading），然後複製那串以 gsk_ 開頭的金鑰。

### 步驟 4-2：部署 Azure Functions 雲端基礎設施
請在 PowerShell 中執行以下腳本，這將會為您的 C# 程式碼建立一個專屬的 Serverless 運算大廳，並將剛剛取得的 Groq 金鑰注入環境變數中：

```powershell
$FUNCTIONS_APP = "fn-quant-ai-$SUFFIX"
$FUNCTIONS_STORAGE = "stfnshared$SUFFIX"

# 執行前，請將這行換成你剛剛複製的 Groq 金鑰
$GROQ_API_KEY = "<gsk_你的金鑰請貼在這裡>"

Write-Host " 正在建立 Azure Functions 運算資源..."

# 1. 建立 Function 內部儲存體與 Function App (採用 dotnet-isolated 隔離模式)
az storage account create --name $FUNCTIONS_STORAGE --resource-group $RG --location $LOC --sku Standard_LRS

az functionapp create `
    --name $FUNCTIONS_APP `
    --resource-group $RG `
    --storage-account $FUNCTIONS_STORAGE `
    --consumption-plan-location $LOC `
    --functions-version 4 `
    --os-type Linux `
    --runtime dotnet-isolated

# 2. 將 Groq 金鑰安全地寫入雲端環境變數
az functionapp config appsettings set `
    --name $FUNCTIONS_APP `
    --resource-group $RG `
    --settings "Groq__ApiKey=$GROQ_API_KEY"

Write-Host "Azure Functions 部署完畢，且已成功綁定 Groq 金鑰！"
```
檢查 Azure Functions 是否已啟動：
```
# PowerShell
az functionapp show `
    --name $FUNCTIONS_APP `
    --resource-group rg-quant-trading-2026 `
    --query "state" `
    --output tsv
```
> 記得把名稱換成你實際的變數或名稱


### 步驟 4-3：AI 代理程式參數調整 (C# .NET 8)
在後端專案中，我們直接使用標準的 OpenAI NuGet 套件，並在建構子中將底層連線網址替換為 Groq 的伺服器節點。

檔案路徑： src/QuantTrading.Functions/AiAgentFunction.cs
核心程式碼：

1. 檔案路徑： QuantTradingSystem\src\QuantTrading.Functions 建立一個檔案 local.settings.json 其內容如下：
```
# JSON
{
  "IsEncrypted": false,
  "Values": {
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    
    "Groq__ApiKey": "<剛剛從 Groq 複製的長長金鑰貼在這裡>" 
  }
}   
```
2. 啟動 QuantTrading.Functions
位置：QuantTradingSystem\src\QuantTrading.Functions
```
# PowerShell
func start
```
> 如果還沒安裝 Azure Functions Core Tools
```
# PowerShell
npm i -g azure-functions-core-tools@4 --unsafe-perm true
```

3. 然後打開 Postman，對著你的本機網址如： http://localhost:7222/api/GetAiAdvice 發送 POST 請求：
```
# JSON
{
  "marketData": "今日 0050 爆量上漲，突破季線，KD 黃金交叉"
}
```

> AI 模型名稱幾個月就會換一次。如果你換了上面這個名字還是報錯，請直接點擊進入 Groq 官方模型列表 (console.groq.com/docs/models)，看表格裡面 Model ID 欄位寫什麼（找有 70b 的通常最聰明），把它複製貼上替換掉就好！

```
# C#
# 將 QuantTrading.Functions.AiAgentFunction 中的型號換掉 
_chatClient = new ChatClient("型號", credential, options);
```
3. Azure Functionsh 測試
如果你還沒有把本機的程式碼推上雲端，你可以在本機的 QuantTrading.Functions 專案目錄下，使用 Azure Functions Core Tools 執行這行指令，將程式碼直接發佈上雲端：
```
# Bash
# 請把後面的名字換成你在雲端建立的 Function App 名稱
func azure functionapp publish fn-quant-ai-2026
```
> 取得 url，將之前 Postman 測試連結改用 url

4. 檔案路徑： D:\Code\QuantTradingSystem\src\QuantTrading.Worker\appsettings.json，內容如下：
```
# JSON
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=/app/data/quant_data.db;"
  },
  "FinMind": {
    "Token": ""
  },
  "AiAgentUrl": "<上面取得的 url>"
}
```

5. commit 並推到 github
可到 Github > QuantTradingSystem 專案 > 上方的 Actions 查看 CI/CD 的執行結果
7. 查找 quant-api 的 Application Url
```
# PowerShell
az containerapp show `
  --name $API `
  --resource-group $RG `
  --query "properties.configuration.ingress.fqdn" `
  --output tsv
```


## 5. 前端佈署
修改資料來源
> 檔案路徑：QuantTradingSystem\quant-frontend\src\App.vue
const apiUrl = `https://XXX/api/strategy/0050?startDate=${startDate}&endDate=${endDate}`;
將 XXX 改為 quant-api 的 Application Url

## 5-1 建立 Azure Static Web App (前端) -- 不可用
> (LocationNotAvailableForResourceType) The provided location 'koreacentral' is not available for resource type 'Microsoft.Web/staticSites'. List of available regions for the resource type is 'centralus,eastus2,westus2,westeurope,eastasia'.
Code: LocationNotAvailableForResourceType
Message: The provided location 'koreacentral' is not available for resource type 'Microsoft.Web/staticSites'. List of available regions for the resource type is 'centralus,eastus2,westus2,westeurope,eastasia'.

> <span style="color:red;">由於 Azure 學生訂閱（Azure for Students）有嚴格的地區防護原則（鎖定 koreacentral），而 Azure 官方並未在韓國機房提供 Static Web Apps 服務。為了達成「100% 繞過限制且完全免費」的終極自動化，我們採用 Azure Storage Account 靜態網站 方案，將前後端收攏在同一機房！</span>
```
# PowerShell
$SWA = "quant-frontend"

Write-Host "正在建立 Azure Static Web App 前端網頁..."
# 確保安裝最新版的 staticwebapp 擴充功能
az extension add --name staticwebapp --upgrade

# 建立 SWA 資源 (免費層級 Free SKU)
# 這步驟會自動把 SWA 綁定到你的 GitHub 儲存庫，並幫你在 GitHub 自動生出前端的 CI/CD 檔案！
az staticwebapp create `
    --name $SWA `
    --resource-group $RG `
    --location "koreacentral" `
    --source "https://github.com/你的GITHUB帳號/你的REPO名稱" `
    --branch "main" `
    --app-location "quant-frontend" `
    --output-location "dist" `
    --login-with-github
```

> * -app-location: 指向你專案中前端資料夾的名稱 quant-frontend。
> *  --output-location: Vue 經由 npm run build 打包後的輸出資料夾名稱（通常是 dist）。
> *  執行這行時，命令列會跳出 GitHub 授權提示，以便 Azure 自動去你的 GitHub 設定部署金鑰。


如果遇到錯誤
```
The command failed with an unexpected error. Here is the traceback:
[Errno 13] Permission denied: 'C:\\Users\\bst05\\.azure\\cliextensions\\staticwebapp\\azext_staticwebapp\\azext_metadata.json'
Traceback (most recent call last):
  File "D:\a\_work\1\s\build_scripts\windows\artifacts\cli\Lib\site-packages\knack/cli.py", line 233, in invoke
  File "D:\a\_work\1\s\build_scripts\windows\artifacts\cli\Lib\site-packages\azure/cli/core/commands/__init__.py", line 523, in execute
  File "D:\a\_work\1\s\build_scripts\windows\artifacts\cli\Lib\site-packages\azure/cli/core/__init__.py", line 502, in load_command_table
  File "D:\a\_work\1\s\build_scripts\windows\artifacts\cli\Lib\site-packages\azure/cli/core/__init__.py", line 392, in _update_command_table_from_extensions
  File "D:\a\_work\1\s\build_scripts\windows\artifacts\cli\Lib\site-packages\azure/cli/core/extension/__init__.py", line 148, in get_metadata
  File "D:\a\_work\1\s\build_scripts\windows\artifacts\cli\Lib\site-packages\azure/cli/core/extension/__init__.py", line 177, in get_azext_metadata
PermissionError: [Errno 13] Permission denied: 'C:\\Users\\bst05\\.azure\\cliextensions\\staticwebapp\\azext_staticwebapp\\azext_metadata.json'
```
1. 刪除資料夾 C:\Users\bst05\.azure\cliextensions\staticwebapp\
2. 將 staticwebapp 加回去
```
# PowerShell
az extension add --name staticwebapp
```

## 5-2 啟用 Azure Storage 靜態網站功能
這功能完全免費、支援全球存取，而且因為它屬於儲存體，絕對可以蓋在韓國中部！
1. 開啟儲存體的靜態網站功能
沿用之前建好的儲存體 $STA，在裡面開闢一個網頁空間：
```
# PowerShell
# 啟用儲存體的靜態網站功能，並指定首頁為 index.html
az storage blob service-properties update `
    --account-name $STA `
    --static-website true `
    --index-document index.html `
    --404-document index.html
```

2. 取得前端專屬網址
執行這行，它會印出前端以後在網路上的正式網址：
```
# PowerShell
# 查詢網頁的 Web 終端節點 (URL)
az storage account show --name $STA --resource-group $RG --query "primaryEndpoints.web" --output tsv
```
> 這將網址記下來

## 5-3：打包並上傳前端檔案
既然不能走 SWA 的 GitHub 管道，只要在本地端切換到 quant-frontend 執行打包，然後用 CLI 送上雲端：
```
# PowerShell
# 切換到前端目錄
cd D:\Code\QuantTradingSystem\quant-frontend
npm install
npm run build

# 把 dist 裡面的所有網頁檔案，直接塞進雲端儲存體的 $web 容器中
az storage blob upload-batch `
    --account-name $STA `
    --source ./dist `
    --destination '$web' `
    --overwrite true
```

## 大功告成
如果遇到 CORS 問題
```
# PowerShell
az functionapp cors add `
  --name $FUNCTIONS_APP `
  --resource-group $RG `
  --allowed-origins "<F12 顯示的 origin>"
```

## 不留痕跡完整移除環境

乾淨抹除所有雲端資源：
```
# 1. 刪除整個雲端資源群組 (API, Worker, DB, 儲存體一次抹除)
az group delete --name $RG --yes --no-wait

# (選用) 查閱資源群組的刪除進度狀態
az group show --name $RG --query "properties.provisioningState" --output tsv

# 2. 刪除 GitHub CI/CD 專用的虛擬權限帳戶 (Service Principal)
## 1. 取得 Service Principal 的 Object ID 並存入變數
$spId = az ad sp list --display-name "quant-deploy-sp-2026" --query "[].id" --output tsv

## 2. 檢查變數是否為空，如果有找到就執行刪除
if ($spId) {
    Write-Host "找到 Service Principal，準備刪除 Object ID: $spId"
    az ad sp delete --id $spId
    Write-Host "刪除成功！"
} else {
    Write-Host "找不到名為 quant-deploy-sp-2026 的 Service Principal。"
}

# 複查帳戶是否成功刪除
az ad sp list --display-name "quant-deploy-sp-2026" --query "[].id" --output tsv

# (若上方複查有印出殘留的 ID，請複製該 ID 執行此行強制刪除)
# az ad sp delete --id "把剛剛印出來的ID貼在這裡"
```
