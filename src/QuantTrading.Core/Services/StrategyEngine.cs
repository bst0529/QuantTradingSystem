using QuantTrading.Core.Models;

namespace QuantTrading.Core.Services;

public class StrategyEngine
{
    /// <summary>
    /// 高效能計算引擎：輸入歷史價格，輸出帶有 KD, MA60 與 AI 決策的訊號陣列
    /// </summary>
    public List<StrategyResult> CalculateSignals(List<DailyPrice> prices)
    {
        var results = new List<StrategyResult>(prices.Count); // 預先配置記憶體容量，減少 GC 負擔
        
        // 🛡️ 防禦性程式設計：確保資料長度足夠計算 MA60
        if (prices == null || prices.Count < 60) return results;

        double prevK = 50, prevD = 50;
        double sumClose60 = 0;

        // 預先加總前 59 天的收盤價 (為 MA60 滑動視窗做準備)
        for (int i = 0; i < 59; i++) sumClose60 += prices[i].Close;

        for (int i = 59; i < prices.Count; i++)
        {
            var today = prices[i];

            // 1. 高效計算 MA60 (滑動視窗: 加上今天，扣除 60 天前)
            sumClose60 += today.Close;
            double ma60 = sumClose60 / 60.0;
            
            // 2. 計算 KD (9,3,3)
            // 尋找過去 9 天的最高與最低價 (注意邊界防護)
            int startIdx = Math.Max(0, i - 8);
            double high9 = prices[startIdx].High;
            double low9 = prices[startIdx].Low;
            
            for (int j = startIdx + 1; j <= i; j++)
            {
                if (prices[j].High > high9) high9 = prices[j].High;
                if (prices[j].Low < low9) low9 = prices[j].Low;
            }

            double rsv = (high9 == low9) ? 0 : (today.Close - low9) / (high9 - low9) * 100.0;
            double k = (2.0 / 3.0) * prevK + (1.0 / 3.0) * rsv;
            double d = (2.0 / 3.0) * prevD + (1.0 / 3.0) * k;

            // 3. 核心策略邏輯 (結合 MA60 趨勢與 AI 情緒)
            TradingSignal signal = TradingSignal.None;
            string note = "";
            bool isDeadCross = (prevK > prevD) && (k < d);
            bool isGoldenCross = (prevK < prevD) && (k > d);

            if (isDeadCross) // 尋找買點
            {
                if (today.Close > ma60) // 強勢市場
                {
                    signal = TradingSignal.Buy;
                    note = "多頭回檔買進";
                }
                else if (today.Close <= ma60 && k < 10) // 弱勢市場：條件緊縮
                {
                    signal = TradingSignal.Buy;
                    note = "空頭極度超賣買進";
                }

                // 🤖 AI 一票否決機制：如果技術面出現買點，但 AI 情緒極度恐慌 (< 0.2)，則取消買進！
                if (signal == TradingSignal.Buy && today.SentimentScore > 0 && today.SentimentScore < 0.2)
                {
                    signal = TradingSignal.None;
                    note = "AI 偵測極度恐慌，否決買進訊號";
                }
            }
            else if (isGoldenCross && today.Close > ma60) // 強勢市場賣點
            {
                signal = TradingSignal.Sell;
                note = "波段獲利了結";
            }

            // 寫入結果並推進狀態
            results.Add(new StrategyResult
            {
                Date = today.Date,
                Open = today.Open,
                High = today.High,
                Low = today.Low,
                Close = today.Close,
                Volume = today.Volume,
                K = k,
                D = d,
                MA60 = ma60,
                SentimentScore = today.SentimentScore,
                Signal = signal,
                Note = note
            });
            
            prevK = k;
            prevD = d;
            sumClose60 -= prices[i - 59].Close; // 滑動視窗尾部剔除
        }

        return results;
    }
}