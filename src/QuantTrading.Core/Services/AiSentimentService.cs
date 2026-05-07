using Azure;
using Azure.AI.TextAnalytics;
using Microsoft.Extensions.Configuration;

namespace QuantTrading.Core.Services;

public class AiSentimentService
{
    private readonly TextAnalyticsClient _client;

    // 透過建構子注入 IConfiguration，安全地讀取 User Secrets
    public AiSentimentService(IConfiguration configuration)
    {
        /*
        string endpoint = configuration["AzureAi:Endpoint"] ?? throw new ArgumentNullException("Azure AI Endpoint is missing");
        string apiKey = configuration["AzureAi:ApiKey"] ?? throw new ArgumentNullException("Azure AI ApiKey is missing");

        var credentials = new AzureKeyCredential(apiKey);
        var endpointUri = new Uri(endpoint);

        // 初始化 Azure AI 客戶端
        _client = new TextAnalyticsClient(endpointUri, credentials);
        */
        _client = null;
    }

    /// <summary>
    /// 傳入當日財經新聞標題或內文，回傳 0~1 的市場情緒分數 (越接近 1 代表越樂觀)
    /// </summary>
    public async Task<double> AnalyzeMarketSentimentAsync(string newsText)
    {
        return 0.5;
        try
        {
            // 呼叫 Azure AI 進行情緒分析 (指定語言為繁體中文)
            DocumentSentiment documentSentiment = await _client.AnalyzeSentimentAsync(newsText, language: "zh-Hant");

            // Azure 會回傳 Positive, Neutral, Negative 的信心分數 (0.00 ~ 1.00)
            // 我們這裡的量化邏輯：將正面情緒視為加分，負面情緒視為扣分，轉換為單一分數
            double score = documentSentiment.ConfidenceScores.Positive - documentSentiment.ConfidenceScores.Negative;
            
            // 將分數常態化到 0 ~ 1 之間 (0.5 為中性)
            return (score + 1.0) / 2.0;
        }
        catch (RequestFailedException ex)
        {
            // 資安實務：只記錄必要的錯誤代碼，不要將整包 Exception 或敏感數據噴到日誌中
            Console.WriteLine($"[Error] AI 分析失敗，狀態碼: {ex.Status}, 錯誤碼: {ex.ErrorCode}");
            return 0.5; // 發生異常時，預設回傳中立情緒，避免阻斷交易決策
        }
    }
}