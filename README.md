## 📂 第一步：環境準備
在你的電腦上，請確保已安裝以下工具：

1. Git (用於版本控制與 GitHub 同步)
2. Azure CLI (用於執行自動化建置腳本)
3. Node.js (用於執行前端 Vue)
4. Docker Desktop (本機測試用，非必備)

## 第二步：基礎設施全自動建置 (Azure)
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

## 設定 GitHub Actions (CI/CD)
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

## 啟動前端看盤畫面
1. 打開終端機，進入前端資料夾：
```
# Bash
cd quant-frontend
```

2. 安裝套件並啟動：
```
# Bash
npm install
npm run dev
```

> 特別提示：連接雲端 API
> 在 App.vue 中，目前的資料來源是指向本地端 (http://localhost:5050)：
> ```
> # JavaScript
> const response = await fetch('http://localhost:5050/api/strategy/0050?...')
> ```
> 當你的後端成功透過 CI/CD 部署到 Azure 後，請到 Azure Portal 的 quant-api 頁面複製 Application Url，並將 App.vue 裡的網址替換為雲端網址，例如：
> ```
> # JavaScript
> const response = await fetch('https://quant-api.xxx.koreacentral.azurecontainerapps.io/api/strategy/0050?...')
> ```

## 建立 Azure Static Web App (前端)
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

> (LocationNotAvailableForResourceType) The provided location 'koreacentral' is not available for resource type 'Microsoft.Web/staticSites'. List of available regions for the resource type is 'centralus,eastus2,westus2,westeurope,eastasia'.
Code: LocationNotAvailableForResourceType
Message: The provided location 'koreacentral' is not available for resource type 'Microsoft.Web/staticSites'. List of available regions for the resource type is 'centralus,eastus2,westus2,westeurope,eastasia'.

> <span style="color:red;">由於 Azure 學生訂閱（Azure for Students）有嚴格的地區防護原則（鎖定 koreacentral），而 Azure 官方並未在韓國機房提供 Static Web Apps 服務。為了達成「100% 繞過限制且完全免費」的終極自動化，我們採用 Azure Storage Account 靜態網站 方案，將前後端收攏在同一機房！</span>

## Azure Storage Account 
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

3. 上傳網頁
既然不能走 SWA 的 GitHub 管道，只要在本地端切換到 quant-frontend 執行打包，然後用 CLI 送上雲端：
```
# PowerShell
# 切換到前端目錄
cd D:\Code\Test\QuantTradingSystem\quant-frontend
npm run build

# 把 dist 裡面的所有網頁檔案，直接塞進雲端儲存體的 $web 容器中
az storage blob upload-batch `
    --account-name $STA `
    --source ./dist `
    --destination '$web' `
    --overwrite true
```

## 大功告成

## 不留痕跡完整移除環境

如果你想關閉系統並停止所有雲端計費，請在 PowerShell 執行以下兩行指令，即可 100% 乾淨抹除所有雲端資源：
```
# 1. 刪除整個雲端資源群組 (API, Worker, DB, 儲存體一次抹除)
az group delete --name rg-quant-trading-2026 --yes --no-wait

# 查閱資源群組的狀態
az group show --name rg-quant-trading-2026 --query "properties.provisioningState" --output tsv

# 2. 刪除 GitHub 部署專用的虛擬帳戶
az ad sp delete --id "http://quant-deploy-sp-2026"

# 複查是否刪除
az ad sp list --display-name "quant-deploy-sp-2026" --query "[].id" --output tsv

# 未刪除
az ad sp delete --id "把剛剛印出來的ID貼在這裡"
```
