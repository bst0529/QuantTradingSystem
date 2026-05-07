using QuantTrading.Worker;
using QuantTrading.Core.Repositories;
using QuantTrading.Core.Services;

var builder = Host.CreateApplicationBuilder(args);

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