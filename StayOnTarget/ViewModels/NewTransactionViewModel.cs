using System.Collections.ObjectModel;
using StayOnTarget.Models;
using System.Windows.Input;
using StayOnTarget.Services;

namespace StayOnTarget.ViewModels;

public class NewTransactionViewModel: ViewModelBase {
    private readonly BudgetService _budgetService;
    private Account _account;
    private readonly Action<NewTransactionViewModel, bool> _closeCallback;
    
    private ImportedTransactionViewModel? _selectedImported;

    public ImportedTransactionViewModel? SelectedImported {
        get => _selectedImported;
        set {
            SetProperty(ref _selectedImported, value);
        }
    }
    
    public NewTransactionViewModel(Account account, BudgetService budgetService, ImportedTransactionViewModel SelectedImported, Action<NewTransactionViewModel, bool> closeCallback) {
        _account = account;
        _budgetService = budgetService;
        _closeCallback = closeCallback;
        
        CancelNewTransactionCommand = new RelayCommand(param => OnCancel());
        SaveNewTransactionCommand =
            new RelayCommand(param => _ = OnSave(), param => EditingTransactionClone != null);
        
        EditingTransactionClone = new Transaction {
            Description = SelectedImported.Payee,
            Memo = "", Amount = Math.Abs(SelectedImported.Amount), 
            TransactionDate = SelectedImported.Date.Value,
            FitId = SelectedImported.BankId, 
            AccountId = SelectedImported.Amount > 0 ? null : _account.Id,
            AccountName = SelectedImported.Amount > 0 ? null : _account.Name,
            ToAccountName = SelectedImported.Amount > 0 ? _account.Name : null
        };
        
        LoadPaychecks();

        EditingTransactionClone.PeriodDate = CurrentPeriodDate;
        
        Loaded = true;
        
    }
    
    private bool _loaded;

    public bool Loaded {
        get => _loaded;
        set => SetProperty(ref _loaded, value);
    }
    
    public ICommand CancelNewTransactionCommand { get; }
    public ICommand SaveNewTransactionCommand { get; }
    
    private ObservableCollection<Account> _accounts = new();
    
    private ObservableCollection<Paycheck> _paychecks = new();
    
    private ObservableCollection<BudgetBucket> _buckets = new();
    private DateTime _currentPeriodDate = DateTime.MinValue;

    private ObservableCollection<Account> _accountsWithNone = new();

    public ObservableCollection<Account> AccountsWithNone {
        get => _accountsWithNone;
        set => SetProperty(ref _accountsWithNone, value);
    }

    private ObservableCollection<Bill> _billsWithNone = new();

    public ObservableCollection<Bill> BillsWithNone {
        get => _billsWithNone;
        set => SetProperty(ref _billsWithNone, value);
    }

    private ObservableCollection<BudgetBucket> _bucketsWithNone = new();

    public ObservableCollection<BudgetBucket> BucketsWithNone {
        get => _bucketsWithNone;
        set => SetProperty(ref _bucketsWithNone, value);
    }

    private ObservableCollection<Bill> _bills = new();

    public ObservableCollection<Bill> Bills {
        get => _bills;
        set => SetProperty(ref _bills, value);
    }

    private Transaction? _editingTransactionClone;

    public Transaction? EditingTransactionClone {
        get => _editingTransactionClone;
        set => SetProperty(ref _editingTransactionClone, value);
    }

    private ObservableCollection<PeriodBill> _currentPeriodBills = new();

    public ObservableCollection<PeriodBill> CurrentPeriodBills {
        get => _currentPeriodBills;
        set => SetProperty(ref _currentPeriodBills, value);
    }

    private ObservableCollection<PeriodBucket> _currentPeriodBuckets = new();

    public ObservableCollection<PeriodBucket> CurrentPeriodBuckets {
        get => _currentPeriodBuckets;
        set => SetProperty(ref _currentPeriodBuckets, value);
    }

    public DateTime CurrentPeriodDate {
        get => _currentPeriodDate;
        set {
            if (SetProperty(ref _currentPeriodDate, value)) {
                LoadPeriodData();
            }
        }
    }

    private ObservableCollection<Transaction> _currentPeriodTransactions = new();

    public ObservableCollection<Transaction> CurrentPeriodTransactions {
        get => _currentPeriodTransactions;
        set => SetProperty(ref _currentPeriodTransactions, value);
    }
    
    private ObservableCollection<Paycheck> _paychecksWithNone = new();
    
    public ObservableCollection<Paycheck> PaychecksWithNone {
        get => _paychecksWithNone;
        set => SetProperty(ref _paychecksWithNone, value);
    }
    
    private void LoadPeriodData() {
        try {
            var accounts = _budgetService.GetAllAccounts().ToList();
            if (accounts.All(a => a.Name != "Household Cash")) {
                var cashAccount = new Account {
                    Name = "Household Cash",
                    Type = AccountType.Savings,
                    Balance = 0,
                    IncludeInTotal = true
                };
                _budgetService.UpsertAccount(cashAccount);
                accounts = _budgetService.GetAllAccounts().ToList();
            }

            accounts = accounts.OrderBy(b => b.Name).ToList();
            // foreach (var a in accounts) a.PropertyChanged += Item_PropertyChanged;
            // Accounts = new ObservableCollection<Account>(accounts);

            var accountsWithNone = new List<Account> { new Account { Id = 0, Name = "(None)" } };
            accountsWithNone.AddRange(accounts);
            AccountsWithNone = new ObservableCollection<Account>(accountsWithNone);

            var bills = _budgetService.GetAllBills();
            bills = bills.OrderBy(b => b.DueDay).ThenBy(b => b.Name).ToList();
            // foreach (var b in bills) b.PropertyChanged += Item_PropertyChanged;
            // Bills = new ObservableCollection<Bill>(bills);

            var billsWithNone = new List<Bill> { new Bill { Id = 0, Name = "(None)" } };
            billsWithNone.AddRange(bills);
            BillsWithNone = new ObservableCollection<Bill>(billsWithNone);

            var paychecks = _budgetService.GetAllPaychecks();
            paychecks = paychecks.OrderBy(b => b.Name).ToList();
            // foreach (var p in paychecks) p.PropertyChanged += Item_PropertyChanged;
            // Paychecks = new ObservableCollection<Paycheck>(paychecks);

            var paychecksWithNone = new List<Paycheck> { new Paycheck { Id = 0, Name = "(None)" } };
            paychecksWithNone.AddRange(paychecks);
            PaychecksWithNone = new ObservableCollection<Paycheck>(paychecksWithNone);

            var buckets = _budgetService.GetAllBuckets();
            buckets = buckets.OrderBy(b => b.Name).ToList();
            // foreach (var b in buckets) b.PropertyChanged += Item_PropertyChanged;
            // Buckets = new ObservableCollection<BudgetBucket>(buckets);

            var bucketsWithNone = new List<BudgetBucket> { new BudgetBucket { Id = 0, Name = "(None)" } };
            bucketsWithNone.AddRange(buckets);
            BucketsWithNone = new ObservableCollection<BudgetBucket>(bucketsWithNone);
        }
        finally {
            // _isLoadingData = false;
        }
        
        LoadPeriodBills();
        LoadPeriodBuckets();
        LoadPeriodTransactions();
    }

    private void LoadPeriodBills() {
        var pBills = _budgetService.GetPeriodBills(CurrentPeriodDate).ToList();
        pBills = pBills.OrderBy(pb => pb.DueDate).ToList();

        CurrentPeriodBills = new ObservableCollection<PeriodBill>(pBills);
        OnPropertyChanged(nameof(CurrentPeriodBills));
    }

    private void LoadPeriodBuckets() {
        var pBuckets = _budgetService.GetPeriodBucketsIncludingMonthly(CurrentPeriodDate).ToList();
        CurrentPeriodBuckets = new ObservableCollection<PeriodBucket>(pBuckets);
        OnPropertyChanged(nameof(CurrentPeriodBuckets));
    }

    private void LoadPeriodTransactions() {
        var transactions = _budgetService.GetTransactions(CurrentPeriodDate).ToList();
        transactions = transactions.OrderBy(pb => pb.TransactionDate).ToList();
        CurrentPeriodTransactions = new ObservableCollection<Transaction>(transactions);
        OnPropertyChanged(nameof(CurrentPeriodTransactions));
    }


    private ObservableCollection<Paycheck> PeriodPaychecks { get; set; }

    public ObservableCollection<Paycheck> Paychecks {
        get => _paychecks;
        set => SetProperty(ref _paychecks, value);
    }
    
    private void LoadPaychecks() {
        var paychecks = _budgetService.GetAllPaychecks();
        paychecks = paychecks.OrderBy(b => b.Name).ToList();
        Paychecks = new ObservableCollection<Paycheck>(paychecks);
        
        var allPaychecks = Paychecks.ToList();
        if (allPaychecks.Count == 0) {
            CurrentPeriodDate = DateTime.Today;
            return;
        }

        PeriodPaychecks = new ObservableCollection<Paycheck>(allPaychecks);
        
        var paychecksWithNone = new List<Paycheck> { new Paycheck { Id = 0, Name = "(None)" } };
        paychecksWithNone.AddRange(paychecks);
        PaychecksWithNone = new ObservableCollection<Paycheck>(paychecksWithNone);

        SetCurrentPeriodDate();
    }

    private void SetCurrentPeriodDate(int? id = null) {
        if (EditingTransactionClone == null) return;
        
        var allPaychecks = Paychecks.ToList();
        if (allPaychecks.Count == 0) {
            CurrentPeriodDate = DateTime.Today;
            return;
        }

        DateTime latestPayBeforeToday = DateTime.MinValue;
        foreach (var pay in allPaychecks.Where(p => id == null || p.Id == id)) {
            var nextPay = pay.StartDate;
            while (nextPay <= EditingTransactionClone.TransactionDate) {
                if (nextPay <= EditingTransactionClone.TransactionDate && nextPay > latestPayBeforeToday)
                    latestPayBeforeToday = nextPay;

                nextPay = pay.Frequency switch {
                    Frequency.Weekly => nextPay.AddDays(7),
                    Frequency.BiWeekly => nextPay.AddDays(14),
                    Frequency.Monthly => nextPay.AddMonths(1),
                    _ => nextPay.AddYears(100)
                };
            }
        }

        if (latestPayBeforeToday != DateTime.MinValue)
            CurrentPeriodDate = latestPayBeforeToday;
        else if (allPaychecks.Any())
            CurrentPeriodDate = allPaychecks.Min(p => p.StartDate);

        var currentPeriodPaychecks = new List<Paycheck>();
        foreach (var pay in allPaychecks.Where(p => id == null || p.Id == id)) {
            var nextPay = pay.StartDate;
            var found = false;
            while (nextPay <= CurrentPeriodDate) {
                if (nextPay.Date == CurrentPeriodDate.Date) {
                    found = true;
                    break;
                }

                nextPay = pay.Frequency switch {
                    Frequency.Weekly => nextPay.AddDays(7),
                    Frequency.BiWeekly => nextPay.AddDays(14),
                    Frequency.Monthly => nextPay.AddMonths(1),
                    _ => nextPay.AddYears(100)
                };
            }

            if (found) currentPeriodPaychecks.Add(pay);
        }

        // if (id == null && currentPeriodPaychecks.Any()) {
        //     EditingTransactionClone.PaycheckId = currentPeriodPaychecks.First().Id;
        //     OnPropertyChanged(nameof(EditingTransactionClone));
        // }
    }
    
    private async Task OnSave() {
        if (EditingTransactionClone == null) return;

        if (EditingTransactionClone.AccountId == 0) EditingTransactionClone.AccountId = null;
        if (EditingTransactionClone.ToAccountId == 0) EditingTransactionClone.ToAccountId = null;
        if (EditingTransactionClone.BillId == 0) EditingTransactionClone.BillId = null;
        if (EditingTransactionClone.BucketId == 0) EditingTransactionClone.BucketId = null;
        if (EditingTransactionClone.PaycheckId == 0) EditingTransactionClone.PaycheckId = null;
        
        await _budgetService.UpsertTransactionAsync(EditingTransactionClone);
        //if(SelectedImported!=null && SelectedImported.BankId==EditingTransactionClone.FitId) {
            // SelectedImported.IsReconciled = false;//for purposes of this screen. 
            // SelectedImported.Status = "Created";
            //ImportedTransactions.Remove(SelectedImported);
            _closeCallback?.Invoke(this, true);
        //}
        EditingTransactionClone = null;
    }
    
    private void OnCancel()
    {
        EditingTransactionClone = null;
        // Tell the parent to close us, passing 'false' because they cancelled
        _closeCallback?.Invoke(this, false);
    }
}