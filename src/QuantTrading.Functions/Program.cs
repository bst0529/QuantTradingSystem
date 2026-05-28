using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace QuantTrading.Functions;

public class Program
{
    public static async Task Main(string[] args)
    {
        // 🚀 建立 .NET 8.0 Azure Functions 的獨立伺服器宿主
        var host = new HostBuilder()
            .ConfigureFunctionsWebApplication() // 整合新一代的進程內 Web 核心
            .ConfigureServices(services =>
            {
                // 📊 註冊 Application Insights 雲端監控與監測元件
                services.AddApplicationInsightsTelemetryWorkerService();
                services.ConfigureFunctionsApplicationInsights();

                // 💡 未來如果你有自訂的 Service（例如 IStockRepository），可以直接在這裡注入：
                // services.AddScoped<IStockRepository, StockRepository>();
            })
            .Build();

        // 啟動 Function 引擎
        await host.RunAsync();
    }
}