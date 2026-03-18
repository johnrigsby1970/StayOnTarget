using StayOnTarget.ViewModels;

namespace StayOnTarget.Models;
    
public class AccountAprHistory : ViewModelBase
{
    private decimal _annualPercentageRate;
    private decimal _cashAdvanceRate;
    private decimal _balanceTransferRate;
    private DateTime _asOfDate;

    public int Id { get; set; }
    public int AccountId { get; set; }
    
    public decimal AnnualPercentageRate
    {
        get => _annualPercentageRate;
        set => SetProperty(ref _annualPercentageRate, value);
    }
    
    public decimal CashAdvanceRate
    {
        get => _cashAdvanceRate;
        set => SetProperty(ref _cashAdvanceRate, value);
    }
        
    public decimal BalanceTransferRate
    {
        get => _balanceTransferRate;
        set => SetProperty(ref _balanceTransferRate, value);
    }

    public DateTime AsOfDate
    {
        get => _asOfDate;
        set => SetProperty(ref _asOfDate, value);
    }
}
