namespace QuantTrading.Core.Models;

public enum TradingSignal { None, Buy, Sell }

public class StrategyResult
{
    public DateTime Date { get; set; }
    
    // 💡 補上這四個！
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public long Volume { get; set; }
    
    public double K { get; set; }
    public double D { get; set; }
    public double MA60 { get; set; }
    public double SentimentScore { get; set; }
    public TradingSignal Signal { get; set; }
    public string Note { get; set; }
}