using StayOnTarget.Services;
using StayOnTarget.ViewModels;

namespace StayOnTarget.Models;

public class ProjectionGridItem : ViewModelBase {
    public ProjectionGridItem(
        DateTime date, 
        decimal amount, 
        string description, 
        int? fromAccountId, 
        int? toAccountId, 
        int? bucketId, 
        int? paycheckId, 
        DateTime? paycheckOccurrenceDate, 
        ProjectionEngine.ProjectionEventType type,
        bool isPrincipalOnly,
        bool isRebalance,
        bool isInterestAdjustment,
        bool isReconciled
        ) {
        Date = date;
        Amount = amount;
        Description = description;
        FromAccountId = fromAccountId;
        ToAccountId = toAccountId;
        BucketId = bucketId;
        PaycheckId = paycheckId;
        PaycheckOccurrenceDate = paycheckOccurrenceDate;
        Type = type;
        IsPrincipalOnly = isPrincipalOnly;
        IsRebalance = isRebalance;
        IsInterestAdjustment = isInterestAdjustment;
        IsReconciled = isReconciled;
    }
    private DateTime _date;
    public DateTime Date
    {
        get { return _date; }
        set
        {
            if (_date != value)
            {
                _date = value;
                OnPropertyChanged("Date");
            }
        }
    }
    
    private decimal _amount;
    public decimal Amount
    {
        get { return _amount; }
        set
        {
            if (_amount != value)
            {
                _amount = value;
                OnPropertyChanged("Amount");
            }
        }
    }
    
    private string _description = string.Empty;
    public string Description
    {
        get { return _description; }
        set
        {
            if (_description != value)
            {
                _description = value;
                OnPropertyChanged("Description");
            }
        }
    }
    
    private int? _fromAccountId;
    public int? FromAccountId
    {
        get { return _fromAccountId; }
        set
        {
            if (_fromAccountId != value) {
                _fromAccountId = value;
                OnPropertyChanged("FromAccountId");
            }
        }
    }
    
    private int? _toAccountId;
    public int? ToAccountId
    {
        get { return _toAccountId; }
        set
        {
            if (_toAccountId != value) {
                _toAccountId = value;
                OnPropertyChanged("ToAccountId");
            }
        }
    }
        
    private int? _bucketId;
    public int? BucketId
    {
        get { return _bucketId; }
        set
        {
            if (_bucketId != value) {
                _bucketId = value;
                OnPropertyChanged("BucketId");
            }
        }
    }
    
    private int? _paycheckId;
    public int? PaycheckId
    {
        get { return _paycheckId; }
        set
        {
            if (_paycheckId != value) {
                _paycheckId = value;
                OnPropertyChanged("PaycheckId");
            }
        }
    }
    
    private DateTime? _paycheckOccurrenceDate;
    public DateTime? PaycheckOccurrenceDate
    {
        get { return _paycheckOccurrenceDate; }
        set
        {
            if (_paycheckOccurrenceDate != value) {
                _paycheckOccurrenceDate = value;
                OnPropertyChanged("PaycheckOccurrenceDate");
            }
        }
    }
    
    private ProjectionEngine.ProjectionEventType _type;
    public ProjectionEngine.ProjectionEventType Type
    {
        get { return _type; }
        set
        {
            if (_type != value) {
                _type = value;
                OnPropertyChanged("Type");
            }
        }
    }

    private bool _isPrincipalOnly;
    public bool IsPrincipalOnly
    {
        get { return _isPrincipalOnly; }
        set
        {
            if (_isPrincipalOnly != value) {
                _isPrincipalOnly = value;
                OnPropertyChanged("IsPrincipalOnly");
            }
        }
    }
    
    private bool _isRebalance;
    public bool IsRebalance
    {
        get { return _isRebalance; }
        set
        {
            if (_isRebalance != value) {
                _isRebalance = value;
                OnPropertyChanged("IsRebalance");
            }
        }
    }
    
    private bool _isInterestAdjustment;
    public bool IsInterestAdjustment
    {
        get { return _isInterestAdjustment; }
        set
        {
            if (_isInterestAdjustment != value) {
                _isInterestAdjustment = value;
                OnPropertyChanged("IsInterestAdjustment");
            }
        }
    }
    
    private bool _isReconciled;
    public bool IsReconciled
    {
        get { return _isReconciled; }
        set
        {
            if (_isReconciled != value) {
                _isReconciled = value;
                OnPropertyChanged("IsReconciled");
            }
        }
    }
}