namespace StayOnTarget.Models;

public class AnalyticsMetrics
{
    public decimal AverageSpend { get; set; }
    public decimal AverageBiweeklySpend { get; set; }
    public decimal AverageWeeklySpend { get; set; }
    public decimal AverageSemiMonthlySpend { get; set; }
    public decimal AveragePerPurchase { get; set; }
    public decimal MedianPerPurchase { get; set; }
    public decimal AveragePurchasesPerMonth { get; set; }
    public decimal AveragePurchasesPerBiweekly { get; set; }
    public decimal AveragePurchasesPerWeekly { get; set; }
    public decimal AveragePurchasesPerSemiMonthly { get; set; }
    public decimal PeakSpend { get; set; }
    public decimal OutlierThreshold { get; set; }
    public decimal SuggestedBudget { get; set; }
    public decimal SuggestedBiweeklyBudget { get; set; }
    
    // Inflation / Trend Metrics
    public bool HasEnoughDataForTrend { get; set; }
    public decimal MonthlyInflationRate { get; set; } // Monthly change in dollars
    public decimal AnnualizedInflationPercentage { get; set; } // YoY rate
    
    // Seasonality Metrics
    public bool IsSeasonal { get; set; }
    public string PeakSeasonSummary { get; set; } = string.Empty;

    public List<MonthlySpendPoint> MonthlyHistory { get; set; } = new();
}