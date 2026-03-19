using StayOnTarget.ViewModels;

namespace StayOnTarget.Models;

public class AccountReconciliation : ViewModelBase
{
    private int _accountId;
    private DateTime _reconciledAsOfDate = DateTime.Today;
    private decimal _reconciledBalance;
    private DateTime _reconciledOnDate = DateTime.Today;
    private bool _isInvalidated;

    public int Id { get; set; }

    public int AccountId
    {
        get => _accountId;
        set => SetProperty(ref _accountId, value);
    }

    public DateTime ReconciledAsOfDate
    {
        get => _reconciledAsOfDate;
        set => SetProperty(ref _reconciledAsOfDate, value);
    }

    public decimal ReconciledBalance
    {
        get => _reconciledBalance;
        set => SetProperty(ref _reconciledBalance, value);
    }

    public DateTime ReconciledOnDate
    {
        get => _reconciledOnDate;
        set => SetProperty(ref _reconciledOnDate, value);
    }

    public bool IsInvalidated
    {
        get => _isInvalidated;
        set => SetProperty(ref _isInvalidated, value);
    }

    // Helper for UI
    public string? AccountName { get; set; }
}
