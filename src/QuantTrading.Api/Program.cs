using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;
using QuantTrading.Core.Repositories;
using QuantTrading.Core.Services;

var builder = WebApplication.CreateBuilder(args);

// 🛡️ 資安設定 1：嚴格的 CORS 策略
var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() 
                     ?? new[] { "http://localhost:5173" }; // Vue 3 預設開發 Port

builder.Services.AddCors(options =>
{
    options.AddPolicy("StrictCorsPolicy", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// 註冊依賴服務
builder.Services.AddSingleton(sp => 
    new StockRepository(builder.Configuration.GetConnectionString("DefaultConnection")!));
builder.Services.AddSingleton<StrategyEngine>();

var app = builder.Build();

var repo = app.Services.GetRequiredService<StockRepository>();
repo.EnsureTableExists(); 

app.UseCors("StrictCorsPolicy");

// 🛡️ 資安設定 2：強制 HTTPS (生產環境必備)
app.UseHttpsRedirection();

// 📈 建立 Minimal API 端點
app.MapGet("/api/strategy/{symbol}", async (
    [FromRoute] string symbol, 
    [FromQuery] string startDate, 
    [FromQuery] string endDate,
    StockRepository repo, 
    StrategyEngine engine) =>
{
    // 🛡️ 資安設定 3：輸入參數嚴格驗證 (Input Validation)
    // 股票代碼只能是 2 到 6 碼的英數字，徹底杜絕 SQL Injection 變形攻擊
    if (!Regex.IsMatch(symbol, @"^[a-zA-Z0-9]{2,6}$"))
        return Results.BadRequest(new { Error = "無效的股票代碼格式" });

    if (!DateTime.TryParse(startDate, out var start) || !DateTime.TryParse(endDate, out var end))
        return Results.BadRequest(new { Error = "日期格式錯誤，請使用 YYYY-MM-DD" });

    if (start > end)
        return Results.BadRequest(new { Error = "起始日期不能大於結束日期" });

    try
    {
        // 1. 從 SQLite 高速撈取資料
        var historyData = await repo.GetHistoricalDataAsync(symbol.ToUpper(), start, end);
        
        if (!historyData.Any())
            return Results.NotFound(new { Message = $"找不到 {symbol} 在此區間的資料" });

        // 2. 丟入策略引擎計算 KD, MA60 與買賣點
        var strategyResults = engine.CalculateSignals(historyData.ToList());

        // 3. 回傳乾淨的 JSON 給 Vue 3 前端渲染
        return Results.Ok(new 
        {
            Symbol = symbol.ToUpper(),
            DataPoints = strategyResults.Count,
            Results = strategyResults
        });
    }
    catch (Exception ex)
    {
        // 🛡️ 資安設定 4：千萬不要把真實的 Exception Stack Trace 丟給前端
        Console.WriteLine($"[API Error] {ex.Message}");
        return Results.StatusCode(500); 
    }
});

app.Run();