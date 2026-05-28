using System.Text.Json;
using System.ClientModel;
using Microsoft.Azure.Functions.Worker;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OpenAI; // 💡 沒錯，我們依然使用官方 OpenAI 套件！
using OpenAI.Chat;

namespace QuantTrading.Functions;

public class AiAgentFunction
{
    private readonly ChatClient _chatClient;
    private readonly ILogger<AiAgentFunction> _logger;

    public AiAgentFunction(ILogger<AiAgentFunction> logger)
    {
        _logger = logger;

        // 讀取 Groq 金鑰
        string apiKey = Environment.GetEnvironmentVariable("Groq__ApiKey")?.Trim() ?? "";

        // 💡 核心魔法：把 OpenAI 的底層連線網址改指向 Groq 的伺服器！
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri("https://api.groq.com/openai/v1")
        };

        // 💡 先把 apiKey 字串包裝成 SDK 規定的安全憑證型別
        var credential = new ApiKeyCredential(apiKey);

        // 💡 然後再傳進去
        _chatClient = new ChatClient("llama-3.3-70b-versatile", credential, options);
    }

    [Function("GetAiAdvice")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        _logger.LogInformation("⏳ 收到 Worker 的盤後數據，正在請求 Groq API 生成決策...");

        try
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(requestBody);

            string marketData = data != null && data.ContainsKey("marketData") ? data["marketData"] : "";

            var messages = new ChatMessage[]
            {
                // 特別強調「繁體中文」，因為開源模型的預設語系有時會偏向簡體
                new SystemChatMessage("你是一位精通台股與 0050 的頂尖量化交易分析師。請根據使用者提供的當日數據，給出一段極為精簡（100字內）的「繁體中文」操盤講評與明日潛在風險警示。"),
                new UserChatMessage($"今日盤後量化指標如下：{marketData}")
            };

            var options = new ChatCompletionOptions
            {
                Temperature = 0.3f,
                MaxOutputTokenCount = 200
            };

            var result = await _chatClient.CompleteChatAsync(messages, options);
            string aiAdvice = result.Value.Content[0].Text;

            _logger.LogInformation($"✅ Groq 成功生成投資講評：{aiAdvice}");

            return new OkObjectResult(new { advice = aiAdvice });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Groq API 呼叫失敗。");
            return new OkObjectResult(new { advice = "Groq API 呼叫失敗。" });
        }
    }
}