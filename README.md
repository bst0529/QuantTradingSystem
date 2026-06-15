## 1. 環境準備
在你的電腦上，請確保已安裝以下工具：

1. Git (用於版本控制與 GitHub 同步) 
https://git-scm.com/install/windows
2. Azure CLI (用於執行自動化建置腳本) 
https://learn.microsoft.com/zh-tw/cli/azure/install-azure-cli?view=azure-cli-latest
3. Node.js (用於執行前端 Vue) 
https://nodejs.org/zh-tw/download
4. Docker Desktop (本機測試用，非必備) 
https://docs.docker.com/desktop/setup/install/windows-install/
5. Fork 專案並下載至本地

## 2. 基礎設施全自動建置 (Azure)
打開你的 PowerShell，登入 Azure：
``` 
# PowerShell
# --use-device-code: 透過裝置代碼在瀏覽器中登入，適合避免終端機直接彈出視窗卡住的情況
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
# --name: 資源群組名稱
# --location: 部署的資料中心位置 (如 koreacentral 代表韓國中部)
az group create --name $RG --location $LOC

# --resource-group: 指定資源所屬的群組
# --name: ACR 的全域唯一名稱
# --sku Basic: 定價層級，Basic 成本最低
# --admin-enabled true: 啟用管理員使用者，後續 CI/CD 才能用帳密進行 Docker 登入推播
az acr create --resource-group $RG --name $ACR --sku Basic --admin-enabled true

# ==========================================
# 3. 建立儲存體與檔案共用 (SQLite 雲端硬碟)
# ==========================================
Write-Host "正在建立雲端儲存體..."
# --sku Standard_LRS: 標準層級的本地備援儲存體 (成本較低，資料在單一資料中心複製)
az storage account create --resource-group $RG --name $STA --location $LOC --sku Standard_LRS

# --output tsv: 將回傳的 JSON 格式轉換為純文字 (Tab-Separated Values)，去除引號以便存入變數
$CONN = az storage account show-connection-string --resource-group $RG --name $STA --output tsv

# --connection-string: 傳入剛取得的連線字串，授權 CLI 建立共用資料夾
az storage share create --name $SHARE --connection-string $CONN

# --query "[0].value": 使用 JMESPath 語法查詢，只抓取回傳結果中第一把存取金鑰的值
$STA_KEY = az storage account keys list --resource-group $RG --account-name $STA --query "[0].value" --output tsv

# ==========================================
# 4. 建立 Container Apps 環境並掛載硬碟
# ==========================================
Write-Host "正在建立 Container Apps 環境 (這需要幾分鐘)..."
# --upgrade: 確保擴充套件是最新版
az extension add --name containerapp --upgrade
az containerapp env create --name $ENV --resource-group $RG --location $LOC

# 將 Azure Files 註冊到 Container Apps 環境中作為一個可用的儲存空間
# --access-mode ReadWrite: 設定掛載點的權限為可讀寫 (SQLite 需要寫入)
# --azure-file-account-name / -key / -share-name: 綁定前面建好的 Azure Files 資訊
# --storage-name: 在這個環境中，為此掛載點取一個識別名稱 (後續容器設定會用到)
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
# --query id: 取得當前訂閱的 ID
$SUB_ID = az account show --query id --output tsv

# 建立名為 Service Principal (SP) 的服務實體，專門給 GitHub 自動化程式使用的帳號
# --role contributor: 賦予「參與者」權限，可以建立/修改資源，但不能把權限分給別人
# --scopes: 安全性考量，限制這個 SP 只能操作這個專案的資源群組，不能動到其他資源
# --sdk-auth: 輸出相容於 Azure SDK / GitHub Actions 驗證的標準 JSON 格式
$AZURE_CREDENTIALS = az ad sp create-for-rbac --name "quant-deploy-sp-$SUFFIX" --role contributor --scopes /subscriptions/$SUB_ID/resourceGroups/$RG --sdk-auth

# 取得 ACR 登入帳號與密碼
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

# ==========================================
# 1. 觸發條件設定 (Triggers)
# ==========================================
on:
  push:
    branches: [ "main" ] # 當程式碼 push 到 main 分支時自動觸發
    paths:
      - 'src/**'                 # 只有當 src 資料夾內的程式碼有異動時才觸發
      - '.github/workflows/**'   # 或是這個 YAML 腳本本身有修改時觸發
  workflow_dispatch: # 允許在 GitHub 網頁 Actions 介面中「手動」點擊按鈕觸發執行

# ==========================================
# 2. 全域環境變數設定 (Environment Variables)
# ==========================================
# 請確保這裡的變數名稱與你基礎設施腳本 (PowerShell) 中的名稱一致
env:
  ACR_NAME: registryquant2026           # 你的 Azure Container Registry (ACR) 名稱
  IMAGE_NAME: quant-api                 # 準備打包的 Docker 映像檔名稱
  CONTAINER_APP_NAME: quant-api         # Azure Container App 的資源名稱
  RESOURCE_GROUP: rg-quant-trading-2026 # 資源群組名稱

# ==========================================
# 3. 執行工作區塊 (Jobs)
# ==========================================
jobs:
  build-and-deploy:
    runs-on: ubuntu-latest # 使用 GitHub 提供的最新版 Ubuntu 虛擬機來執行自動化任務
    
    steps:
      # 步驟 A: 將 GitHub 儲存庫的程式碼下載到虛擬機中
      - name: Checkout Code
        uses: actions/checkout@v3

      # 步驟 B: 登入 Azure Container Registry (ACR)
      - name: Log in to Azure Container Registry (ACR)
        uses: docker/login-action@v2
        with:
          registry: ${{ env.ACR_NAME }}.azurecr.io
          username: ${{ secrets.ACR_USERNAME }} # 從 GitHub Secrets 讀取帳號
          password: ${{ secrets.ACR_PASSWORD }} # 從 GitHub Secrets 讀取密碼

      # 步驟 C: 根據 Dockerfile 打包應用程式，並推送到 ACR
      - name: Build and Push API Docker Image
        uses: docker/build-push-action@v4
        with:
          context: .
          file: ./src/QuantTrading.Api/Dockerfile
          push: true
          # 給予映像檔標籤 (Tag)，使用 GitHub 此次 Commit 的 SHA 碼作為唯一版號
          tags: ${{ env.ACR_NAME }}.azurecr.io/${{ env.IMAGE_NAME }}:${{ github.sha }}

      # 步驟 D: 登入 Azure 帳號 (取得操作雲端資源的權限)
      - name: Azure Login
        uses: azure/login@v1
        with:
          creds: ${{ secrets.AZURE_CREDENTIALS }} # 從 GitHub Secrets 讀取 Service Principal 憑證

      # 步驟 E: 透過 Azure CLI 指令更新並部署 Container App
      - name: Deploy to Azure Container App
        uses: azure/CLI@v1
        with:
          azcliversion: latest
          inlineScript: |
            # 1. 部署/更新容器與縮放規則
            # --image: 指定剛剛推送至 ACR 的映像檔與版本標籤
            # --target-port: 容器內部應用程式聽取的 Port (此 API 為 8080)
            # --ingress external: 開放外部網路存取，給前端打 API 用
            # --env-vars: 注入資料庫連線字串的環境變數
            # 注意：up 指令不支援縮放參數，所以只做基礎部署
            az containerapp up \
              --name ${{ env.CONTAINER_APP_NAME }} \
              --resource-group ${{ env.RESOURCE_GROUP }} \
              --image ${{ env.ACR_NAME }}.azurecr.io/${{ env.IMAGE_NAME }}:${{ github.sha }} \
              --target-port 8080 \
              --ingress external \
              --env-vars "ConnectionStrings__DefaultConnection=Data Source=/app/data/quant_data.db;"
            
            # 2. 確保環境變數與儲存空間設定生效
            # 使用 update 確保雲端 SQLite 所需的環境變數在資源更新時不會遺失
            # 透過 update 指令確保環境變數生效，並套用自動縮放規則
            # --min-replicas 0: 【省錢關鍵】無流量時自動縮減為 0 個容器 (Serverless)
            # --max-replicas 10: 【效能關鍵】大流量時最多開啟 10 個容器來處理請求
            az containerapp update \
              --name ${{ env.CONTAINER_APP_NAME }} \
              --resource-group ${{ env.RESOURCE_GROUP }} \
              --set-env-vars "ConnectionStrings__DefaultConnection=Data Source=/app/data/quant_data.db;" \
              --min-replicas 0 \
              --max-replicas 10
```


> 魔法說明：只要你把程式碼 Push 到 GitHub 的 main 分支，這個腳本就會自動打包最新的 Docker Image、上傳，並通知 Azure 把新的 API 跑起來，完全不需要手動操作！

## 4.建立 Azure AI 大模型
0. 如果 Azure 訂閱帳號目前「尚未解鎖」使用 AI 服務（Cognitive Services）的權限 -- 略過

```
# PowerShell
# 1. 啟用 Cognitive Services 權限
# --namespace: 指定要向目前訂閱註冊的資源提供者服務名稱
az provider register --namespace Microsoft.CognitiveServices

# 2. 確認啟用狀態 (需等待 1~3 分鐘)
# --query: 查詢當前該提供者的註冊狀態是否已變成 Registered
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

Write-Host "正在建立 Azure Functions 運算資源..."

# 1. 建立 Function 內部儲存體與 Function App (採用 dotnet-isolated 隔離模式)
az storage account create --name $FUNCTIONS_STORAGE --resource-group $RG --location $LOC --sku Standard_LRS

# --storage-account: Function 運作本身需要依賴一個儲存體來放系統日誌與核心檔案
# --consumption-plan-location: 採用「使用量方案 (Consumption Plan)」，主打用多少付多少、無流量時自動縮放至 0，並指定佈署區域
# --functions-version 4: 採用 Azure Functions v4 最新執行階段
# --os-type Linux: 指定底層作業系統為 Linux
# --runtime dotnet-isolated: 採用 .NET 隔離工作者模型，確保相依套件不會與 Function 主機衝突
az functionapp create `
    --name $FUNCTIONS_APP `
    --resource-group $RG `
    --storage-account $FUNCTIONS_STORAGE `
    --consumption-plan-location $LOC `
    --functions-version 4 `
    --os-type Linux `
    --runtime dotnet-isolated

# 2. 將 Groq 金鑰安全地寫入雲端環境變數
# --settings: 以 Key=Value 格式注入環境變數，讓 C# 程式可以在不將金鑰寫死在程式碼的狀況下讀取
az functionapp config appsettings set `
    --name $FUNCTIONS_APP `
    --resource-group $RG `
    --settings "Groq__ApiKey=$GROQ_API_KEY"

Write-Host "Azure Functions 部署完畢，且已成功綁定 Groq 金鑰！"
```

檢查 Azure Functions 是否已啟動：
```
# PowerShell
# --query "state": 只撈取目前系統狀態 (應顯示為 Running)
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
# PowerShell
# 將本機的 Functions 程式碼打包並直接佈署推上雲端資源 ($FUNCTIONS_APP)
func azure functionapp publish $FUNCTIONS_APP
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
# --query "properties.configuration.ingress.fqdn": 查詢容器對外開放的完整網域名稱 (FQDN)，這是前端要用來打 API 的網址
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
az extension add --name staticwebapp --upgrade

# 建立 SWA 資源
# --source: 指定存放程式碼的 GitHub Repo 網址，Azure 會自動去幫你生出 CI/CD yaml 檔案
# --branch: 監聽的 Git 分支
# --app-location: 指定前端專案資料夾在 Repo 裡的相對路徑
# --output-location: 指定 Vue 經由 npm run build 打包後產生的輸出資料夾 (通常是 dist)
# --login-with-github: 觸發瀏覽器 GitHub 登入授權流程，讓 Azure 自動去你的 Repo 塞部署金鑰 (Secrets)
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

## 5-2 啟用 Azure Storage 靜態網站功能
這功能完全免費、支援全球存取，而且因為它屬於儲存體，絕對可以蓋在韓國中部！
1. 開啟儲存體的靜態網站功能
沿用之前建好的儲存體 $STA，在裡面開闢一個網頁空間：
```
# PowerShell
# 啟用儲存體的靜態網站功能，並指定首頁為 index.html
# --static-website true: 開啟儲存體的靜態網站託管功能，這會在系統內建立一個名叫 $web 的隱藏容器
# --index-document: 指定預設的首頁檔案名稱
# --404-document: 指定找不到頁面時的回退檔案。對於 Vue 這類單頁應用程式 (SPA) 非常重要，這讓所有未知的路由都能交回給 Vue Router 處理
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
# --query "primaryEndpoints.web": 撈取靜態網站的對外正式 URL
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

# az storage blob upload-batch: 批次將整個資料夾上傳到 Blob 儲存體
# --source: 指定本地剛打包完的 dist 資料夾
# --destination: 必須上傳到系統專屬的靜態網站容器 '$web'
# --overwrite true: 如果雲端已有舊檔案則直接覆寫 (適合部署新版本)
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
# --allowed-origins: 允許跨來源資源共用 (CORS)，將前端靜態網站的 URL 加進白名單，讓瀏覽器允許前端對 Function 發出請求
az functionapp cors add `
  --name $FUNCTIONS_APP `
  --resource-group $RG `
  --allowed-origins "<F12 顯示的 origin>"
```

## 不留痕跡完整移除環境

乾淨抹除所有雲端資源：
```
# 1. 刪除整個雲端資源群組 (API, Worker, DB, 儲存體一次抹除)
# --yes: 跳過互動式確認提示，直接開始刪除
# --no-wait: 送出刪除指令後立即放行終端機，讓 Azure 在雲端背景執行刪除動作，不用卡在本地等
az group delete --name $RG --yes --no-wait

# (選用) 查閱資源群組的刪除進度狀態
# --query "properties.provisioningState": 查詢當前狀態，刪除中會顯示 Deleting
az group show --name $RG --query "properties.provisioningState" --output tsv

# 2. 刪除 GitHub CI/CD 專用的虛擬權限帳戶 (Service Principal)
## 1. 取得 Service Principal 的 Object ID 並存入變數
# --display-name: 尋找先前建立用來部屬的虛擬帳號名稱
az ad sp list --display-name "quant-deploy-sp-$SUFFIX" --query "[].id" --output tsv

## 2. 檢查變數是否為空，如果有找到就執行刪除
if ($spId) {
    Write-Host "找到 Service Principal，準備刪除 Object ID: $spId"
    # 根據 Object ID 刪除該虛擬權限帳戶，避免留下安全隱患
    az ad sp delete --id $spId
    Write-Host "刪除成功！"
} else {
    Write-Host "找不到名為 quant-deploy-sp-$SUFFIX 的 Service Principal。"
}
```
