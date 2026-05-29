using Flurl.Http;
using QuantTrading.Core.Models;
using QuantTrading.Core.Repositories;
using QuantTrading.Core.Services;
using Serilog;

Directory.CreateDirectory("/app/data");
Directory.CreateDirectory("/app/data/logs");

var dbPath = "/app/data/quant_data.db";

if (!File.Exists(dbPath))
{
    File.Create(dbPath).Dispose();
}


var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information() // 預設記錄 Information 以上的等級
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning) // 忽略 ASP.NET Core 碎碎念的底層 Log
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", Serilog.Events.LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    // 將 Log 寫入我們掛載的持久化磁碟區 /app/data/logs 底下
    .WriteTo.File("/app/data/logs/api-log-.txt",
        rollingInterval: RollingInterval.Day, // 每天自動開一個新檔案 (例如: api-log-20260523.txt)
        retainedFileCountLimit: 30)           // 最多保留 30 天，避免硬碟塞爆
    .CreateLogger();

// 告訴 ASP.NET Core 使用 Serilog
builder.Host.UseSerilog();

try
{
    Log.Information("QuantTrading.Api 正在啟動...");

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowAll", policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
    });

    // 註冊依賴服務
    builder.Services.AddSingleton(sp =>
        new StockRepository(builder.Configuration.GetConnectionString("DefaultConnection")!));
    builder.Services.AddSingleton<StrategyEngine>();

    var app = builder.Build();

    app.UseCors("AllowAll");

    // 確保主 API 啟動時，SQLite 內部一定有 DailyPrice 資料表
    using (var scope = app.Services.CreateScope())
    {
        var repo = app.Services.GetRequiredService<StockRepository>();
        try
        {
            repo.EnsureTableExists();
            Log.Information("✅ 主 API 端資料庫資料表初始化確認成功。");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ 主 API 初始化資料表失敗！");
        }
    }

    // 📈 建立 Minimal API 端點
    app.MapGet("/api/strategy/{symbol}", async (string symbol, DateTime startDate, DateTime endDate, StockRepository repo, StrategyEngine engine) =>
    {

        IEnumerable<DailyPrice> data = Enumerable.Empty<DailyPrice>();

        for (int i = 0; i < 5; i++)
        {
            try
            {
                data = await repo.GetHistoricalDataAsync(symbol, startDate, endDate);
                break;
            }
            catch
            {
                await Task.Delay(3000);
            }
        }

        var dataList = data.ToList();

        // 🌟 2. 智慧快取機制 (檢查資料是否夠新)
        var latestDbDate = dataList.Any() ? dataList.Max(d => d.Date) : DateTime.MinValue;

        // 如果沒資料，【或者】最新資料離查詢終點超過 5 天
        if (!dataList.Any() || latestDbDate < endDate.AddDays(-5))
        {
            Log.Information($"⚠️ 資料庫最新資料僅到 {latestDbDate:yyyy-MM-dd}，與目標 {endDate:yyyy-MM-dd} 存在落差，觸發即時從 FinMind 撈取缺漏數據...");

            try
            {
                // 💡 優化：只抓取「缺漏區間」的資料，避免重複抓取
                var fetchStartDate = dataList.Any() ? latestDbDate.AddDays(1) : startDate;

                string url = $"https://api.finmindtrade.com/api/v4/data?dataset=TaiwanStockPrice&data_id={symbol}&start_date={fetchStartDate:yyyy-MM-dd}&end_date={endDate:yyyy-MM-dd}";
                var response = await url.GetJsonAsync<FinMindResponse>();

                if (response?.msg == "success" && response.data != null && response.data.Any())
                {
                    var newPrices = response.data.Select(d => new DailyPrice
                    {
                        Symbol = symbol,
                        Date = DateTime.Parse(d.date),
                        Open = d.open,
                        High = d.max,
                        Low = d.min,
                        Close = d.close,
                        Volume = d.Trading_Volume,
                        SentimentScore = 0.5, // 預設情緒分數
                        MLPredictionProb = 0.5
                    }).ToList();

                    // 將抓回來的資料寫入 SQLite 緩存
                    bool inserted = false;

                    for (int i = 0; i < 5; i++)
                    {
                        try
                        {
                            await repo.InsertPricesAsync(newPrices);
                            inserted = true;
                            break;
                        }
                        catch (Exception ex) // 💡 1. 捕捉實體錯誤原因
                        {
                            Log.Warning($"⚠️ 第 {i + 1} 次嘗試寫入 SQLite 失敗: {ex.Message}");
                            await Task.Delay(3000);
                        }
                    }

                    if (!inserted)
                    {
                        Log.Error("❌ 嚴重錯誤：歷經 5 次重試，FinMind 數據依舊無法寫入 SQLite 資料庫！");
                    }
                    else
                    {
                        Log.Information($"✅ 即時撈取並成功寫入資料庫共 {newPrices.Count} 筆資料！");
                    }

                    // 💡 關鍵：將新抓到的資料跟原本的舊資料合併，並重新按日期排序
                    dataList.AddRange(newPrices);
                    dataList = dataList.OrderBy(d => d.Date).ToList();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "❌ 即時撈取 FinMind 資料失敗");
            }
        }

        // 3. 再次檢查，如果連外部 API 都沒資料，才真正回傳 404
        if (!dataList.Any())
        {
            // 確保前端有正確解析 await response.json() 就能看到這段文字
            return Results.NotFound(new { Message = $"找不到 {symbol} 在此區間的資料，且外部 API 也無回應。" });
        }

        // 4. 交給策略引擎計算指標與買賣點
        var results = engine.CalculateSignals(dataList);
        return Results.Ok(new { results });
    });

    app.Run("http://0.0.0.0:8080");
}
catch (Exception ex)
{
    // 萬一啟動時就炸掉 (例如資料庫連線字串寫錯)，這裡也能抓到
    Log.Fatal(ex, "💀 應用程式啟動失敗！");
}
finally
{
    Log.Information("🛑 應用程式已關閉。");
    Log.CloseAndFlush(); // 確保所有 Log 都已寫入檔案再關閉
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
    public double max { get; set; }
    public double min { get; set; }
    public double close { get; set; }
}