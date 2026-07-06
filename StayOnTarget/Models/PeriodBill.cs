using StayOnTarget.ViewModels;

namespace StayOnTarget.Models;

public class PeriodBill : ViewModelBase
{
    private int _billId;
    private DateTime _periodDate; // Usually the paycheck date this period starts
    private DateTime _dueDate;
    private decimal _actualAmount;
    private decimal _transactionAmount;
    private bool _isPaid;

    public int Id { get; set; }
    public Guid FitId { get; set; } = Guid.NewGuid();

    public int BillId
    {
        get => _billId;
        set => SetProperty(ref _billId, value);
    }

    public DateTime PeriodDate
    {
        get => _periodDate;
        set => SetProperty(ref _periodDate, value);
    }

    public DateTime DueDate
    {
        get => _dueDate;
        set => SetProperty(ref _dueDate, value);
    }

    public decimal ActualAmount {
        get => _actualAmount;
        set {
            // SetProperty returns true ONLY if the value actually changed
            if (SetProperty(ref _actualAmount, value)) 
            {
                OnPropertyChanged(nameof(HasActualAmount));
                OnPropertyChanged(nameof(BudgetExceeded));
            }
        }
    }

    public decimal TransactionAmount
    {
        get => _transactionAmount;
        set
        {
            if (SetProperty(ref _transactionAmount, value))
            {
                OnPropertyChanged(nameof(HasActualAmount));
                OnPropertyChanged(nameof(BudgetExceeded));
            }
        }
    }

    public bool HasActualAmount => _transactionAmount != 0;
    public bool BudgetExceeded => Math.Abs(_transactionAmount) > Math.Abs(_actualAmount);

    public bool IsPaid
    {
        get => _isPaid;
        set => SetProperty(ref _isPaid, value);
    }

    // Helper for UI
    public string? BillName { get; set; }
}