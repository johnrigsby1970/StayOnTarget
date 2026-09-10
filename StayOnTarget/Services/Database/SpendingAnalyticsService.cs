using Dapper;
using Serilog;
using StayOnTarget.Models;

namespace StayOnTarget.Services;

public partial class BudgetService : ISpendingAnalyticsService {
    public async Task<AnalyticsMetrics> GetPayeeAnalyticsAsync(string normalizedPayeeName, int monthsHistory = 24) {
        const string sql = @"
            SELECT 
                strftime('%Y-%m', TransactionDate) AS YearMonth,
                SUM(ABS(Amount)) AS TotalAmount,
                COUNT(*) AS TransactionCount
            FROM Transactions
            WHERE NormalizedDescription = @normalizedName 
              AND Amount < 0
              AND TransactionDate >= date('now', '-' || @monthsHistory || ' month')
            GROUP BY strftime('%Y-%m', TransactionDate)
            ORDER BY YearMonth ASC;";

        const string whereClause = "NormalizedDescription = @normalizedName";

        return await FetchAndCalculateMetricsAsync(sql, whereClause, new { normalizedName = normalizedPayeeName, monthsHistory });
    }

    public async Task<AnalyticsMetrics> GetSubCategoryAnalyticsAsync(int subCategoryId, int monthsHistory = 24) {
        const string sql = @"
            SELECT 
                strftime('%Y-%m', TransactionDate) AS YearMonth,
                SUM(ABS(Amount)) AS TotalAmount,
                COUNT(*) AS TransactionCount
            FROM Transactions
            WHERE SubCategoryId = @subCategoryId 
              AND Amount < 0
              AND TransactionDate >= date('now', '-' || @monthsHistory || ' month')
            GROUP BY strftime('%Y-%m', TransactionDate)
            ORDER BY YearMonth ASC;";

        const string whereClause = "SubCategoryId = @subCategoryId";

        return await FetchAndCalculateMetricsAsync(sql, whereClause, new { subCategoryId, monthsHistory });
    }

    public async Task<AnalyticsMetrics> GetBucketAnalyticsAsync(int bucketId, int monthsHistory = 24) {
        const string sql = @"
            SELECT 
                strftime('%Y-%m', TransactionDate) AS YearMonth,
                SUM(ABS(Amount)) AS TotalAmount,
                COUNT(*) AS TransactionCount
            FROM Transactions
            WHERE BucketId = @bucketId 
              AND Amount < 0
              AND TransactionDate >= date('now', '-' || @monthsHistory || ' month')
            GROUP BY strftime('%Y-%m', TransactionDate)
            ORDER BY YearMonth ASC;";

        const string whereClause = "BucketId = @bucketId";

        return await FetchAndCalculateMetricsAsync(sql, whereClause, new { bucketId, monthsHistory });
    }
    
    public async Task<AnalyticsMetrics> GetBillAnalyticsAsync(int billId, int monthsHistory = 24) {
        const string sql = @"
            SELECT 
                strftime('%Y-%m', TransactionDate) AS YearMonth,
                SUM(ABS(Amount)) AS TotalAmount,
                COUNT(*) AS TransactionCount
            FROM Transactions
            WHERE BillId = @billId 
              AND Amount < 0
              AND TransactionDate >= date('now', '-' || @monthsHistory || ' month')
            GROUP BY strftime('%Y-%m', TransactionDate)
            ORDER BY YearMonth ASC;";

        const string whereClause = "BillId = @billId";

        return await FetchAndCalculateMetricsAsync(sql, whereClause, new { billId, monthsHistory });
    }
    
    #region for Median
    private async Task<List<decimal>> FetchIndividualAmountsAsync(System.Data.Common.DbConnection conn, string whereClause, object parameters)
    {
        string sql = $@"
        SELECT ABS(Amount) 
        FROM Transactions 
        WHERE {whereClause} 
          AND Amount < 0 
          AND TransactionDate >= date('now', '-' || @monthsHistory || ' month')
        ORDER BY ABS(Amount) ASC;";

        try
        {
            return (await conn.QueryAsync<decimal>(sql, parameters)).ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error fetching individual transaction amounts for median calculation.");
            return new List<decimal>();
        }
    }
    
    private decimal CalculateMedian(List<decimal> sortedAmounts)
    {
        if (sortedAmounts == null || !sortedAmounts.Any())
            return 0m;

        int count = sortedAmounts.Count;
        int middle = count / 2;

        if (count % 2 == 0)
        {
            // Even number of elements: average the two middle values
            return Math.Round((sortedAmounts[middle - 1] + sortedAmounts[middle]) / 2m, 2);
        }
        else
        {
            // Odd number of elements: take the exact middle value
            return Math.Round(sortedAmounts[middle], 2);
        }
    }
    
    #endregion
    

    private async Task<AnalyticsMetrics> FetchAndCalculateMetricsAsync(string sql, string whereClause, object parameters) {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();

            var rawPoints = (await conn.QueryAsync<MonthlySpendPoint>(sql, parameters)).ToList();
            var individualAmounts = await FetchIndividualAmountsAsync(conn, whereClause, parameters);
            
            return AnalyzeSpendSeries(rawPoints, individualAmounts);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error fetching analytics data layer results.");
            return new AnalyticsMetrics();
        }
    }

    private AnalyticsMetrics AnalyzeSpendSeries(List<MonthlySpendPoint> series, List<decimal> individualAmounts) {
        var metrics = new AnalyticsMetrics();

        if (series == null || !series.Any()) {
            return metrics;
        }

        metrics.MonthlyHistory = series;
        var totals = series.Select(s => s.TotalAmount).ToList();

        // Basic Math Metrics
        metrics.AverageSpend = Math.Round(totals.Average(), 2);
        
        metrics.AverageBiweeklySpend = Math.Round(metrics.AverageSpend * 12 / 26, 2);
        metrics.AverageWeeklySpend = Math.Round(metrics.AverageSpend * 12 / 52, 2);
        metrics.AverageSemiMonthlySpend = Math.Round(metrics.AverageSpend * 12 / 2, 2);

        #region Per purchase metrics

        // 1. Total dollar amount across the entire evaluated series
        decimal totalSpendSum = series.Sum(s => s.TotalAmount);

        // 2. Total count of individual purchase transactions
        int totalTransactionCount = series.Sum(s => s.TransactionCount);
        int activeMonthsCount = series.Count;
        
        // 3. Average purchase amount per checkout trip
        metrics.AveragePerPurchase = totalTransactionCount > 0
            ? Math.Round(totalSpendSum / totalTransactionCount, 2)
            : 0m;

        // 4. Median purchase amount per checkout trip
        metrics.MedianPerPurchase = CalculateMedian(individualAmounts);
        
        // Average number of purchase trips per month
        metrics.AveragePurchasesPerMonth = activeMonthsCount > 0 
            ? Math.Round((decimal)totalTransactionCount / activeMonthsCount, 1) 
            : 0m;
        
        metrics.AveragePurchasesPerBiweekly = Math.Round(metrics.AveragePurchasesPerMonth * 12 / 26, 2);
        metrics.AveragePurchasesPerWeekly = Math.Round(metrics.AveragePurchasesPerMonth * 12 / 52, 2);
        metrics.AveragePurchasesPerSemiMonthly = Math.Round(metrics.AveragePurchasesPerMonth * 12 / 2, 2);

        #endregion
        
        metrics.PeakSpend = totals.Max();

        // Calculate Outlier Cap (Mean + 1.5 * StdDev) to filter one-off major spikes
        double avg = (double)metrics.AverageSpend;
        double sumSquares = totals.Sum(t => Math.Pow((double)t - avg, 2));
        double stdDev = Math.Sqrt(sumSquares / totals.Count);
        metrics.OutlierThreshold = Math.Round((decimal)(avg + (1.5 * stdDev)), 2);

        // Suggested Budget: 60% recent 3-mo avg + 40% full-period avg (excluding extreme spikes)
        var cappedTotals = totals.Select(t => Math.Min(t, metrics.OutlierThreshold)).ToList();
        var recent3Months = cappedTotals.TakeLast(3).ToList();

        decimal recentAvg = recent3Months.Any() ? recent3Months.Average() : metrics.AverageSpend;
        decimal baselineAvg = cappedTotals.Average();
        metrics.SuggestedBudget = Math.Round((recentAvg * 0.60m) + (baselineAvg * 0.40m), 2);
        metrics.SuggestedBiweeklyBudget = Math.Round(metrics.SuggestedBudget * 12 / 26, 2);

        // Trend / Inflation Analysis via Linear Regression (y = mx + b)
        if (series.Count >= 6) {
            metrics.HasEnoughDataForTrend = true;

            double n = series.Count;
            double sumX = 0;
            double sumY = 0;
            double sumXY = 0;
            double sumX2 = 0;

            for (int i = 0; i < series.Count; i++) {
                double x = i; // Month sequence
                double y = (double)series[i].TotalAmount;

                sumX += x;
                sumY += y;
                sumXY += x * y;
                sumX2 += x * x;
            }

            double denominator = (n * sumX2) - (sumX * sumX);
            if (Math.Abs(denominator) > 0.0001) {
                double slope = ((n * sumXY) - (sumX * sumY)) / denominator; // Dollar change per month
                double intercept = (sumY - (slope * sumX)) / n; // Starting baseline value (b)

                metrics.MonthlyInflationRate = Math.Round((decimal)slope, 2);

                // Calculate annual growth % based on the regression starting baseline (intercept)
                double startingValue = intercept > 0 ? intercept : (double)baselineAvg;
                if (startingValue > 0) {
                    double annualDollarDelta = slope * 12.0;
                    double annualRate = annualDollarDelta / startingValue; // e.g., 0.935 for 93.5%

                    // Clamp to reasonable fractional bounds (-100% to +500%)
                    double clampedRate = Math.Clamp(annualRate, -1.0, 5.0);

                    metrics.AnnualizedInflationPercentage = Math.Round((decimal)clampedRate, 3);
                }
            }
        }

        // Seasonality Check (detect recurring 3-month seasonal surges)
        if (series.Count >= 12) {
            DetectSeasonality(series, metrics);
        }

        return metrics;
    }

    private void DetectSeasonality(List<MonthlySpendPoint> series, AnalyticsMetrics metrics) {
        // Group amounts by calendar quarter/season
        var quarterly = series
            .GroupBy(s => DateTime.Parse(s.YearMonth + "-01").Month switch {
                12 or 1 or 2 => "Winter",
                3 or 4 or 5 => "Spring",
                6 or 7 or 8 => "Summer",
                _ => "Fall"
            })
            .ToDictionary(g => g.Key, g => g.Average(x => x.TotalAmount));

        if (quarterly.Count >= 4) {
            var maxSeason = quarterly.OrderByDescending(q => q.Value).First();
            var minSeason = quarterly.OrderBy(q => q.Value).First();

            // If peak season is >25% higher than lowest season, flag seasonality
            if (minSeason.Value > 0 && (maxSeason.Value / minSeason.Value) >= 1.25m) {
                metrics.IsSeasonal = true;
                metrics.PeakSeasonSummary =
                    $"{maxSeason.Key} peaks (~{maxSeason.Value:C0}/mo vs {minSeason.Key} low of {minSeason.Value:C0}/mo)";
            }
        }
    }
}