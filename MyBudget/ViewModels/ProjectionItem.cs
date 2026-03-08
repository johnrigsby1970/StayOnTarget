namespace MyBudget.ViewModels;

public class ProjectionItem : ViewModelBase
{
    private DateTime _date;
    private string _description = string.Empty;
    private decimal _amount;
    private decimal _balance;
    private bool _isWarning;
    private decimal? _periodNet;
    private int? _paycheckId;
    private Dictionary<string, decimal> _accountBalances = new();

    public DateTime Date { get => _date; set => SetProperty(ref _date, value); }
    public string Description { get => _description; set => SetProperty(ref _description, value); }
    public int? PaycheckId { get => _paycheckId; set => SetProperty(ref _paycheckId, value); }
    public bool NeedsAttention { get => _paycheckId.HasValue;  }
    public decimal Amount 
    { 
        get => _amount; 
        set
        {
            if (SetProperty(ref _amount, value))
            {
                // if (PaycheckId.HasValue && Date.Date <= DateTime.Today.Date)
                // {
                //     // Update or create Transaction
                //     var mainVM = MainViewModel.Instance;
                //     if (mainVM != null)
                //     {
                //         // Actually, we should use the service to find/create it based on Date and PaycheckId
                //         var existing = mainVM.GetTransactionForPaycheck(PaycheckId.Value, Date);
                //         if (existing != null)
                //         {
                //             existing.Amount = value;
                //         }
                //         else
                //         {
                //             // Create new one
                //             var cashAccount = mainVM.Accounts.FirstOrDefault(a => a.Name == "Household Cash");
                //             var paycheck = mainVM.Paychecks.FirstOrDefault(p => p.Id == PaycheckId.Value);
                //
                //             // Find the correct period date for this Date
                //             DateTime pDate = mainVM.FindPeriodDateFor(Date);
                //
                //             var transaction = new Transaction
                //             {
                //                 Description = Description,
                //                 Amount = value,
                //                 Date = Date,
                //                 PeriodDate = pDate,
                //                 ToAccountId = paycheck?.AccountId ?? cashAccount?.Id,
                //                 PaycheckId = PaycheckId.Value
                //             };
                //             mainVM.SaveNewTransaction(transaction);
                //         }
                //         if (mainVM.IsCalculatingProjections) return;
                //         mainVM.CalculateProjections();
                //     }
                // }
            }
        }
    }
    public decimal Balance { get => _balance; set => SetProperty(ref _balance, value); }
    public bool IsWarning { get => _isWarning; set => SetProperty(ref _isWarning, value); }
    public decimal? PeriodNet { get => _periodNet; set => SetProperty(ref _periodNet, value); }
    
    public Dictionary<string, decimal> AccountBalances
    {
        get => _accountBalances;
        set => SetProperty(ref _accountBalances, value);
    }

    public decimal GetAccountBalance(string accountName)
    {
        return _accountBalances.TryGetValue(accountName, out var bal) ? bal : 0;
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