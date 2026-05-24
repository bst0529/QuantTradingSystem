using QuantTrading.Core.Repositories;
using QuantTrading.Core.Services;
using QuantTrading.Worker;
using Serilog;

Directory.CreateDirectory("/app/data");
Directory.CreateDirectory("/app/data/logs");
var builder = Host.CreateApplicationBuilder(args);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    // Worker 的 Log 存成 worker-log-.txt
    .WriteTo.File("/app/data/logs/worker-log-.txt",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30)
    .CreateLogger();

builder.Logging.ClearProviders(); // 清除預設的 Logger
builder.Logging.AddSerilog(Log.Logger); // 加入 Serilog

try
{
    Log.Information("QuantTrading.Worker 正在啟動...");

    // 註冊 AI 服務 (使用 Singleton，因為 Azure 客戶端建議重複使用以節省連線成本)
    builder.Services.AddSingleton<AiSentimentService>();

    // 註冊資料庫 Repository (讀取設定檔中的連線字串)
    builder.Services.AddSingleton(sp =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var connString = config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("未設定連線字串");
        return new StockRepository(connString);
    });

    builder.Services.AddHostedService<Worker>();

    var host = builder.Build();
    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "💀 Worker 啟動失敗或發生致命錯誤！");
}
finally
{
    Log.Information("🛑 Worker 服務已終止。");
    Log.CloseAndFlush();
}