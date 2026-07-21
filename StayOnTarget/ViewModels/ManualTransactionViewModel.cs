namespace StayOnTarget.ViewModels;

public class ManualTransactionViewModel : ViewModelBase {
    public string FitId { get; set; } = string.Empty;// Your internal Guid
    public string? TransactionId { get; set; } // Your internal Transaction Id
    public DateTime TransactionDate { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public int? BucketId { get; set; }
    public int? BillId { get; set; }
        
    private bool _isMatched;

    public bool IsMatched {
        get => _isMatched;
        set {
            _isMatched = value;
            SetProperty(ref _isMatched, value);
        }
    }
}