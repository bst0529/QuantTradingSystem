using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using QuantTrading.Core.Models;

namespace QuantTrading.Core.Repositories;

public class StockRepository
{
    private readonly string _connectionString;
    private static bool _initialized = false;

    public StockRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public void EnsureTableExistsSafe()
    {
        if (_initialized)
            return;

        EnsureTableExists();
        _initialized = true;
    }

    public void EnsureTableExists()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(_connectionString);
        connection.Open();

        // 加 busy timeout
        connection.Execute("PRAGMA busy_timeout = 5000;");

        var command = connection.CreateCommand();
        // 建立 DailyPrice 表格 (包含我們策略需要的欄位)
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS DailyPrice (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Symbol TEXT NOT NULL,
                Date DATETIME NOT NULL,
                Open REAL,
                High REAL,
                Low REAL,
                Close REAL,
                Volume INTEGER,
                SentimentScore REAL,
                MLPredictionProb REAL
            );
            --CREATE INDEX IF NOT EXISTS idx_symbol_date ON DailyPrice (Symbol, Date);
            -- 🛡️ 關鍵：改用 UNIQUE INDEX，Upsert (ON CONFLICT) 才能生效
            CREATE UNIQUE INDEX IF NOT EXISTS idx_symbol_date_unique ON DailyPrice (Symbol, Date);
        ";
        command.ExecuteNonQuery();
    }  

    // 取得歷史資料 (供前端回測與圖表使用)
    public async Task<IEnumerable<DailyPrice>> GetHistoricalDataAsync(string symbol, DateTime startDate, DateTime endDate)
    {
        using IDbConnection db = new SqliteConnection(_connectionString);
        db.Open();
        db.Execute("PRAGMA busy_timeout = 5000;");

        // 🛡️ 資安防護：使用 @Symbol 等參數化寫法，絕對禁止使用字串拼接 (如 $"WHERE Symbol = '{symbol}'")
        const string sql = @"
            SELECT * FROM DailyPrice 
            WHERE Symbol = @Symbol AND Date >= @StartDate AND Date <= @EndDate
            ORDER BY Date ASC";

        return await db.QueryAsync<DailyPrice>(sql, new 
        { 
            Symbol = symbol, 
            StartDate = startDate, 
            EndDate = endDate 
        });
    }

    // 批次寫入每日新資料 (供爬蟲 Worker 使用)
    public async Task InsertPricesAsync(IEnumerable<DailyPrice> prices)
    {
        using IDbConnection db = new SqliteConnection(_connectionString);
        db.Open();
        db.Execute("PRAGMA busy_timeout = 5000;");

        using var transaction = db.BeginTransaction();
        
        // 高效能批次寫入 (Upsert)
        const string sql = @"
            INSERT INTO DailyPrice (Symbol, Date, Open, High, Low, Close, Volume, SentimentScore, MLPredictionProb)
            VALUES (@Symbol, @Date, @Open, @High, @Low, @Close, @Volume, @SentimentScore, @MLPredictionProb)
            ON CONFLICT(Symbol, Date) DO UPDATE SET
            Close = excluded.Close, Volume = excluded.Volume, SentimentScore = excluded.SentimentScore;";

        await db.ExecuteAsync(sql, prices, transaction);
        transaction.Commit();
    }
}