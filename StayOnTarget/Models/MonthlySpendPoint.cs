namespace StayOnTarget.Models;

public class MonthlySpendPoint
{
    public string YearMonth { get; set; } = string.Empty; // Format: "yyyy-MM"
    public decimal TotalAmount { get; set; }
    public int TransactionCount { get; set; }
}