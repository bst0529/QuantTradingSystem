using Flurl.Http;
using QuantTrading.Core.Models;
using QuantTrading.Core.Repositories;
using QuantTrading.Core.Services;

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

        // 1. 先補全歷史資料 (已換成 FinMind 真實數據)
        await Task.Delay(10000, stoppingToken);  // 等待 volume mount

        try
        {
            _stockRepo.EnsureTableExists();
            _logger.LogInformation("✅ DB 初始化完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ DB 初始化失敗");
        }

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
        var existingData = await _stockRepo.GetHistoricalDataAsync(Symbol, DateTime.Today.AddYears(-1), DateTime.Today);
        var latestDbDate = existingData != null && existingData.Any() ? existingData.Max(d => d.Date) : DateTime.MinValue;

        // 如果資料庫最新資料離今天不到 5 天，代表不需要大範圍補資料
        if (latestDbDate >= DateTime.Today.AddDays(-5))
        {
            _logger.LogInformation($"✅ 資料庫已有近期資料 (最新至 {latestDbDate:yyyy-MM-dd})。");
            return;
        }

        _logger.LogInformation("🌐 啟動 FinMind API 抓取缺漏的歷史資料...");

        // 💡 優化：如果資料庫全空就抓過去一年，否則只抓「資料庫最後一天」到「今天」
        string startDate = existingData != null && existingData.Any()
            ? latestDbDate.AddDays(1).ToString("yyyy-MM-dd")
            : DateTime.Today.AddYears(-1).ToString("yyyy-MM-dd");
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