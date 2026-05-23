# 📈 QuantTradingSystem Backend Services

本專案為 0050 量化交易系統的後端微服務架構，採用 .NET 10 開發，並以 Docker 進行容器化部署。系統主要職責為抓取歷史行情、計算技術指標 (MA60, KD)、整合 AI 情緒分析，並提供前端視覺化所需的策略訊號。

## 🏗️ 系統架構

系統拆分為兩個獨立運行的微服務，透過 Azure File Share (本機為 Docker Volume) 共享 SQLite 資料庫：

* **QuantTrading.Worker**: 背景守護行程。每日定時向 FinMind API 抓取最新台股 0050 行情，並計算 AI 情緒分數後寫入資料庫。
* **QuantTrading.Api**: 對外提供 RESTful API 端點。具備 Read-Through Cache 機制，當快取未命中時能即時補齊資料，並透過 `StrategyEngine` 運算買賣訊號。
* **QuantTrading.Core**: 核心領域層。包含資料庫操作 (`StockRepository` 使用 Dapper 進行高效 Upsert) 與演算法邏輯。

## 🛡️ 資安與可靠性設計 (S-SDLC)

本系統在設計與開發階段即導入應用程式安全標準：

* **無伺服器持久化**: 使用 `UNIQUE INDEX` 與 `ON CONFLICT` 確保併發寫入時的資料完整性。
* **防範 SQL Injection**: 資料庫查詢全面採用 Dapper 參數化綁定 (`@Symbol`)。
* **最小權限原則 (Least Privilege)**: Dockerfile 採用 Alpine 映像檔，並強制切換至非特權使用者 (`USER app`) 執行。
* **嚴格的 CORS 策略**: API 端點嚴格限制來源 (AllowedOrigins) 與標頭白名單。
* **日誌稽核**: 導入 Serilog 進行持久化檔案日誌紀錄，隱藏真實 Exception 堆疊以防資訊洩漏。

## 🚀 快速啟動 (Local Development)

確保本機已安裝 Docker 與 Docker Compose。

1. 在專案根目錄下建立 `.env` 檔案（如需要設定 API Key）。
2. 執行以下指令一鍵啟動所有服務：
   ```bash
   docker-compose up -d --build