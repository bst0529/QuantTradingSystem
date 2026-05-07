using YahooFinanceApi;
using Flurl.Http;
using QuantTrading.Core.Repositories;
using QuantTrading.Core.Services;
using QuantTrading.Core.Models;

namespace QuantTrading.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly StockRepository _stockRepo;
    private readonly AiSentimentService _aiService;
    private const string Symbol = "0050";
    private const string YahooSymbol = "0050.TW";

    public Worker(ILogger<Worker> logger, StockRepository stockRepo, AiSentimentService aiService)
    {
        _logger = logger;
        _stockRepo = stockRepo;
        _aiService = aiService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 量化交易 AI 數據 Worker 已啟動。");

        _stockRepo.EnsureTableExists();

        // 1. 先補全歷史資料 (已換成 FinMind 真實數據)
        await EnsureHistoricalDataAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("⏳ 開始今日市場掃描與 AI 分析...");

                // 1. 從 FinMind 抓取最新行情 (抓近 5 天以確保遇到假日也能拿到最後一個交易日)
                string startDate = DateTime.Today.AddDays(-10).ToString("yyyy-MM-dd");
                string endDate = DateTime.Today.ToString("yyyy-MM-dd");
                string url = $"https://api.finmindtrade.com/api/v4/data?dataset=TaiwanStockPrice&data_id=0050&start_date={startDate}&end_date={endDate}";
                
                // 使用 Flurl 套件呼叫 API (沿用我們剛才加在最下面的 FinMindResponse 模型)
                var response = await url.GetJsonAsync<FinMindResponse>(cancellationToken: stoppingToken);
                var latestData = response?.data?.LastOrDefault(); // 取最後一筆 (最新)

                if (latestData != null)
                {
                    // 2. 獲取新聞並進行 AI 情緒分析 (目前維持 Mock 版本，待日後接上真實 Azure AI)
                    string todayNews = "台股大盤強勢突破季線，外資大買台積電與0050，市場投資氣氛熱絡。";
                    double sentimentScore = await _aiService.AnalyzeMarketSentimentAsync(todayNews);

                    // 3. 組裝資料 (注意：High 和 Low 要對應 FinMind 的 max 和 min)
                    var todayData = new DailyPrice
                    {
                        Symbol = Symbol,
                        Date = DateTime.Parse(latestData.date),
                        Open = latestData.open,
                        High = latestData.max,
                        Low = latestData.min,
                        Close = latestData.close,
                        Volume = latestData.Trading_Volume,
                        SentimentScore = sentimentScore,
                        MLPredictionProb = 0.0 
                    };

                    // 4. 寫入資料庫
                    _logger.LogInformation($"正在將 {latestData.date} 的真實數據寫入 SQLite...");
                    await _stockRepo.InsertPricesAsync(new[] { todayData });
                    _logger.LogInformation($"✅ 最新數據同步成功：收盤價 {todayData.Close}, 情緒分數 {sentimentScore:F2}");
                }
                else
                {
                    _logger.LogWarning("⚠️ 無法從 FinMind 獲取最新的交易資料 (可能 API 正在維護)。");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 執行過程中發生未預期的錯誤。");
            }

            _logger.LogInformation("💤 進入休眠，等待下一次排程...");
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }   

    // 輔助方法：確保資料庫裡有基本的歷史數據
    private async Task EnsureHistoricalDataAsync(CancellationToken ct)
    {
        _logger.LogInformation("🔍 正在檢查資料庫資料...");
        var existingData = await _stockRepo.GetHistoricalDataAsync(Symbol, DateTime.Today.AddYears(-1), DateTime.Today);
        
        if (existingData != null && existingData.Any()) {
            _logger.LogInformation("✅ 資料庫已有資料。");
            return;
        }

        _logger.LogInformation("🌐 啟動 FinMind API 抓取真實 0050 歷史資料...");

        // 設定抓取區間：過去一年到今天
        string startDate = DateTime.Today.AddYears(-1).ToString("yyyy-MM-dd");
        string endDate = DateTime.Today.ToString("yyyy-MM-dd");
        string url = $"https://api.finmindtrade.com/api/v4/data?dataset=TaiwanStockPrice&data_id=0050&start_date={startDate}&end_date={endDate}";
        
        try
        {
            // 呼叫 API 並將 JSON 轉換為 C# 物件
            var response = await url.GetJsonAsync<FinMindResponse>(cancellationToken: ct);

            if (response.msg == "success" && response.data != null)
            {
                var realPrices = response.data.Select(d => new DailyPrice
                {
                    Symbol = Symbol,
                    Date = DateTime.Parse(d.date),
                    Open = d.open,
                    High = d.max,
                    Low = d.min,
                    Close = d.close,
                    Volume = d.Trading_Volume,
                    SentimentScore = 0.5, // 歷史資料暫時給中性情緒
                    MLPredictionProb = 0.5
                }).ToList();

                await _stockRepo.InsertPricesAsync(realPrices);
                _logger.LogInformation($"✨ 成功從 FinMind 寫入 {realPrices.Count} 筆真實歷史資料！");
            }
            else
            {
                _logger.LogWarning("⚠️ FinMind API 回傳失敗或無資料。");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 抓取 FinMind 資料時發生錯誤。");
        }
    }    
    private async Task EnsureHistoricalDataAsync1(CancellationToken ct)
    {
        _logger.LogInformation("🔍 正在檢查資料庫資料...");
        var existingData = await _stockRepo.GetHistoricalDataAsync(Symbol, DateTime.Today.AddYears(-1), DateTime.Today);
        
        if (existingData != null && existingData.Any()) {
            _logger.LogInformation("✅ 資料庫已有資料。");
            return;
        }

        _logger.LogInformation("⚠️ Yahoo API 401 故障，啟動 Mock 數據產生器以維持開發...");
        
        var mockPrices = new List<DailyPrice>();
        double lastClose = 140.0; // 起始價格
        var rng = new Random();

        for (int i = 365; i >= 0; i--)
        {
            var date = DateTime.Today.AddDays(-i);
            if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday) continue;

            // 模擬隨機震盪
            double change = (rng.NextDouble() * 4 - 2); // -2.0 ~ +2.0
            double open = lastClose + (rng.NextDouble() - 0.5);
            double close = open + change;
            double high = Math.Max(open, close) + rng.NextDouble();
            double low = Math.Min(open, close) - rng.NextDouble();

            mockPrices.Add(new DailyPrice
            {
                Symbol = Symbol,
                Date = date,
                Open = Math.Round(open, 2),
                High = Math.Round(high, 2),
                Low = Math.Round(low, 2),
                Close = Math.Round(close, 2),
                Volume = rng.Next(5000000, 20000000),
                SentimentScore = rng.NextDouble(),
                MLPredictionProb = 0.5
            });
            lastClose = close;
        }

        await _stockRepo.InsertPricesAsync(mockPrices);
        _logger.LogInformation("✨ Mock 數據注入完成！現在去刷網頁吧！");
    } 
    private async Task EnsureHistoricalDataAsync0(CancellationToken ct)
    {
        _logger.LogInformation("🔍 正在檢查資料庫中 0050 的歷史資料...");

        try
        {
            // 1. 檢查資料庫是否已有資料 (假設 Repository 有提供 GetPricesAsync 或類似方法)
            // 如果你的 Repository 還沒有檢查功能，可以先簡單用一個 Count 查詢
            var existingData = await _stockRepo.GetHistoricalDataAsync(Symbol, DateTime.Today.AddYears(-1), DateTime.Today);
            
            if (existingData.Any())
            {
                _logger.LogInformation($"✅ 已有 {existingData.Count()} 筆資料，跳過歷史初始化。");
                return;
            }

            // 2. 如果沒資料，開始抓取過去一年的歷史行情
            _logger.LogInformation("Empty database detected! 正在從 Yahoo Finance 補全過去一年的歷史資料...");
            
            var endDate = DateTime.Now;
            var startDate = endDate.AddYears(-1);
            
            // 從 Yahoo Finance 抓取
            var history = await Yahoo.GetHistoricalAsync(YahooSymbol, startDate, endDate, Period.Daily, ct);

            // 3. 轉換為 DailyPrice 物件列表
            var historicalPrices = history.Select(candle => new DailyPrice
            {
                Symbol = Symbol,
                Date = candle.DateTime,
                Open = (double)candle.Open,
                High = (double)candle.High,
                Low = (double)candle.Low,
                Close = (double)candle.Close,
                Volume = (long)candle.Volume,
                SentimentScore = 0.5, // 歷史資料我們給予中性分數 0.5
                MLPredictionProb = 0.0
            }).ToList();

            // 4. 批次寫入資料庫 (使用你原有的 InsertPricesAsync)
            _logger.LogInformation($"正在寫入 {historicalPrices.Count} 筆歷史數據到 SQLite...");
            await _stockRepo.InsertPricesAsync(historicalPrices);
            
            _logger.LogInformation("✨ 歷史資料補全完成！");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 初始化歷史資料時發生錯誤。");
        }
    }    
}

public class FinMindResponse
{
    public string msg { get; set; } = string.Empty;
    public List<FinMindData>? data { get; set; }
}

public class FinMindData
{
    public string date { get; set; } = string.Empty;
    public long Trading_Volume { get; set; }
    public double open { get; set; }
    public double max { get; set; } // FinMind 最高價叫 max
    public double min { get; set; } // FinMind 最低價叫 min
    public double close { get; set; }
}