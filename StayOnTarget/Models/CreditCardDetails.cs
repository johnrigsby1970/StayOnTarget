using StayOnTarget.ViewModels;

namespace StayOnTarget.Models;

public class CreditCardDetails : ViewModelBase
{
    private decimal _apr;
    private int _statementDay = 1;
    private int _dueDay = 1;
    private decimal _minPayFloor;
    private bool _payPreviousMonthBalanceInFull = true;
    private bool _graceActive = true;

    public int Id { get; set; }
    public int AccountId { get; set; }
    
    public decimal MinPayFloor
    {
        get => _minPayFloor;
        set => SetProperty(ref _minPayFloor, value);
    }
    
    public int StatementDay
    {
        get => _statementDay;
        set => SetProperty(ref _statementDay, value);
    }
    
    public int DueDateOffset
    {
        get => _dueDay;
        set => SetProperty(ref _dueDay, value);
    }

    public bool PayPreviousMonthBalanceInFull
    {
        get => _payPreviousMonthBalanceInFull;
        set => SetProperty(ref _payPreviousMonthBalanceInFull, value);
    }
    
    public bool GraceActive
    {
        get => _graceActive;
        set => SetProperty(ref _graceActive, value);
    }
}
