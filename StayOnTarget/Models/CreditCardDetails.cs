using StayOnTarget.ViewModels;

namespace StayOnTarget.Models;

public class CreditCardDetails : ViewModelBase
{
    private decimal _apr;
    private int _statementDay = 1;
    private int _dueDay = 1;
    private bool _payPreviousMonthBalanceInFull = true;

    public int Id { get; set; }
    public int AccountId { get; set; }

    public decimal Apr
    {
        get => _apr;
        set => SetProperty(ref _apr, value);
    }

    public int StatementDay
    {
        get => _statementDay;
        set => SetProperty(ref _statementDay, value);
    }
    
    public int DueDay
    {
        get => _dueDay;
        set => SetProperty(ref _dueDay, value);
    }

    public bool PayPreviousMonthBalanceInFull
    {
        get => _payPreviousMonthBalanceInFull;
        set => SetProperty(ref _payPreviousMonthBalanceInFull, value);
    }
}
