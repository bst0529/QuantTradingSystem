namespace QuantTrading.Core.Models;

public class DailyPrice
{
    public string Symbol { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public long Volume { get; set; }
    
    // AI 擴充欄位
    public double SentimentScore { get; set; } 
    public double MLPredictionProb { get; set; }
}