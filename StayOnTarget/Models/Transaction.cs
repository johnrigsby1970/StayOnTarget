using StayOnTarget.ViewModels;

namespace StayOnTarget.Models;

public class Transaction : ViewModelBase
{
    private string _description = string.Empty;
    private decimal _amount;
    private DateTime _date = DateTime.Today;
    private int? _accountId;
    private int? _toAccountId; // For transfers
    private int? _paycheckId; // For associating with a projected paycheck
    private DateTime? _paycheckOccurrenceDate; // The date of the projected paycheck occurrence being replaced
    private int? _billId; // For bill association
    private int? _bucketId; // For bucket association
    private DateTime _periodDate;
    private bool _isPrincipalOnly;
    private bool _isRebalance;
    private bool _isCashAdvance;
    private bool _isBalanceTransfer;
    private bool _isInterestAdjustment;
    private int? _fromAccountReconciledId;
    private int? _toAccountReconciledId;

    public int Id { get; set; }
    public Guid FitId { get; set; } = Guid.NewGuid();

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public decimal Amount
    {
        get => _amount;
        set => SetProperty(ref _amount, value);
    }

    public DateTime Date
    {
        get => _date;
        set => SetProperty(ref _date, value);
    }

    public int? AccountId
    {
        get => _accountId;
        set => SetProperty(ref _accountId, value);
    }

    public int? ToAccountId
    {
        get => _toAccountId;
        set => SetProperty(ref _toAccountId, value);
    }

    public int? PaycheckId
    {
        get => _paycheckId;
        set => SetProperty(ref _paycheckId, value);
    }

    public DateTime? PaycheckOccurrenceDate
    {
        get => _paycheckOccurrenceDate;
        set => SetProperty(ref _paycheckOccurrenceDate, value);
    }

    public int? BillId
    {
        get => _billId;
        set => SetProperty(ref _billId, value);
    }
    
    public int? BucketId
    {
        get => _bucketId;
        set => SetProperty(ref _bucketId, value);
    }

    public DateTime PeriodDate
    {
        get => _periodDate;
        set => SetProperty(ref _periodDate, value);
    }

    public bool IsPrincipalOnly
    {
        get => _isPrincipalOnly;
        set => SetProperty(ref _isPrincipalOnly, value);
    }

    public bool IsRebalance
    {
        get => _isRebalance;
        set => SetProperty(ref _isRebalance, value);
    }

    public bool IsCashAdvance
    {
        get => _isCashAdvance;
        set => SetProperty(ref _isCashAdvance, value);
    }

    public bool IsBalanceTransfer
    {
        get => _isBalanceTransfer;
        set => SetProperty(ref _isBalanceTransfer, value);
    }

    public bool IsInterestAdjustment
    {
        get => _isInterestAdjustment;
        set => SetProperty(ref _isInterestAdjustment, value);
    }

    public int? FromAccountReconciledId
    {
        get => _fromAccountReconciledId;
        set => SetProperty(ref _fromAccountReconciledId, value);
    }

    public int? ToAccountReconciledId
    {
        get => _toAccountReconciledId;
        set => SetProperty(ref _toAccountReconciledId, value);
    }

    // Helper for UI
    public string? AccountName { get; set; }
    public string? ToAccountName { get; set; }
    public string? BillName { get; set; }
    public string? BucketName { get; set; }
}

public class ReconciliationTransaction : Transaction {
    private decimal _runningBalance;
    public decimal RunningBalance
    {
        get => _runningBalance;
        set => SetProperty(ref _runningBalance, value);
    }
    
    private bool _isReconciled;
    public bool IsReconciled
    {
        get => _isReconciled;
        set => SetProperty(ref _isReconciled, value);
    }
}