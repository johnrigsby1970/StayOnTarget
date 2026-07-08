namespace StayOnTarget.Models;

public enum BalanceImpact { Muted, Increased, Decreased }

public class AccountDisplayInfo
{
    public decimal Balance { get; set; }
    public BalanceImpact Impact { get; set; }
    public string ArrowIndicator => Impact == BalanceImpact.Increased ? " ▲" : Impact == BalanceImpact.Decreased ? " ▼" : "";
}