using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using StayOnTarget.Models;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using StayOnTarget.Services;

namespace StayOnTarget.ViewModels;

public class NewTransactionViewModel : ViewModelBase {
    private readonly BudgetService _budgetService = null!;
    private Account _account = null!;
    private readonly Action<NewTransactionViewModel, bool>? _closeCallback;

    private ImportedTransactionViewModel? _selectedImported;

    public ImportedTransactionViewModel? SelectedImported {
        get => _selectedImported;
        set { SetProperty(ref _selectedImported, value); }
    }

    public NewTransactionViewModel(Account account, BudgetService budgetService,
        ImportedTransactionViewModel selectedImported, Action<NewTransactionViewModel, bool> closeCallback) {

        try {
            _account = account;
            _budgetService = budgetService;
            _closeCallback = closeCallback;
            
            CancelNewTransactionCommand = new RelayCommand(OnCancel);
            SaveNewTransactionCommand =
                new AsyncRelayCommand(OnSave, () => EditingTransactionClone != null);
            
            // 1. Validate data state
            bool isValid = 
                           !string.IsNullOrWhiteSpace(selectedImported.Payee) && 
                           selectedImported.Date != null && 
                           !string.IsNullOrWhiteSpace(selectedImported.BankId);

            if (!isValid)
            {
                // Queue the message box and close action onto the WPF Dispatcher AFTER constructor & render finishes
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() => 
                {
                    MessageBox.Show("The transaction lacks required fields of payee, transaction date, and a bank transaction id.");
                    _closeCallback?.Invoke(this, false); // Safely close the window!
                }, System.Windows.Threading.DispatcherPriority.Loaded);

                return; // Abort remaining initialization
            }
            
            if (EditingTransactionClone != null) {
                EditingTransactionClone.PropertyChanged -= EditingTransactionClone_PropertyChanged;
            }
            
            EditingTransactionClone = new Transaction {
                Description = selectedImported.Payee??"",
                Memo = "",
                Amount = Math.Abs(selectedImported.Amount),
                TransactionDate = selectedImported.Date!.Value,

                ToFitId = selectedImported.Amount > 0 ? selectedImported.BankId??"" : "",
                FromFitId = selectedImported.Amount > 0 ? "" : selectedImported.BankId??"",
                
                AccountId = selectedImported.Amount > 0 ? null : _account.Id,
                AccountName = selectedImported.Amount > 0 ? null : _account.Name,
                ToAccountId = selectedImported.Amount > 0 ? _account.Id : null,
                ToAccountName = selectedImported.Amount > 0 ? _account.Name : null
            };
            
            EditingTransactionClone.PropertyChanged += EditingTransactionClone_PropertyChanged;
            
            _ = LoadPaychecksAsync();

            Loaded = true;
        }
        catch (Exception ex) {
            Log.Fatal(ex, "Critical error initializing NewTransactionViewModel.");
            
        }
    }

    private bool _loaded;

    public bool Loaded {
        get => _loaded;
        set => SetProperty(ref _loaded, value);
    }

    public IRelayCommand CancelNewTransactionCommand { get; } = null!;
    public IAsyncRelayCommand SaveNewTransactionCommand { get; } = null!;

    private ObservableCollection<Account> _accounts = new();

    private ObservableCollection<Paycheck> _paychecks = new();
    
    private ObservableCollection<Paycheck> _periodPayChecks = new();

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

    private ObservableCollection<SubCategory> _subCategoriesWithNone = new();

    public ObservableCollection<SubCategory> SubCategoriesWithNone {
        get => _subCategoriesWithNone;
        set => SetProperty(ref _subCategoriesWithNone, value);
    }
        
    private ObservableCollection<Bill> _bills = new();

    public ObservableCollection<Bill> Bills {
        get => _bills;
        set => SetProperty(ref _bills, value);
    }

    private Transaction? _editingTransactionClone;

    public Transaction? EditingTransactionClone {
        get => _editingTransactionClone;
        set {
            try {
                if (SetProperty(ref _editingTransactionClone, value)) {
                    SaveNewTransactionCommand.NotifyCanExecuteChanged();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting EditingTransactionClone in NewTransactionViewModel.");
                
            }
        }
    }

    private ObservableCollection<PeriodBill> _currentPeriodBills = new();

    public ObservableCollection<PeriodBill> CurrentPeriodBills {
        get => _currentPeriodBills;
        set => SetProperty(ref _currentPeriodBills, value);
    }
    
    public DateTime CurrentPeriodDate {
        get => _currentPeriodDate;
        set {
            try {
                if (SetProperty(ref _currentPeriodDate, value)) {
                    OnCurrentPeriodDateChanged();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting CurrentPeriodDate in NewTransactionViewModel.");
                
            }
        }
    }

    private async void OnCurrentPeriodDateChanged()
    {
        try 
        {
            await LoadPeriodDataAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load period data for date {Date}", _currentPeriodDate);
            
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

    private async Task LoadPeriodDataAsync() {
        try {
            var accounts = (await _budgetService.GetAllAccountsAsync(true)).ToList();
            if (accounts.All(a => a.Name != "Household Cash" && a.Type != AccountType.Cash)) {
                var cashAccount = new Account {
                    Name = "Household Cash",
                    Type = AccountType.Cash,
                    Balance = 0,
                    IncludeInTotal = true
                };
                await _budgetService.UpsertAccountAsync(cashAccount);
                accounts = (await _budgetService.GetAllAccountsAsync(true)).ToList();
            }

            accounts = accounts.OrderBy(b => b.Name).ToList();

            var accountsWithNone = new List<Account> { new Account { Id = 0, Name = "(None)" } };
            accountsWithNone.AddRange(accounts.Where(a => !a.IsArchived));
            AccountsWithNone = new ObservableCollection<Account>(accountsWithNone);

            var bills = await _budgetService.GetAllBillsAsync(true);
            bills = bills.OrderBy(b => b.DueDay).ThenBy(b => b.Name).ToList();

            var billsWithNone = new List<Bill> { new Bill { Id = 0, Name = "(None)" } };
            billsWithNone.AddRange(bills.Where(b => !b.IsArchived));
            BillsWithNone = new ObservableCollection<Bill>(billsWithNone);

            var paychecks = await _budgetService.GetAllPaychecksAsync();
            paychecks = paychecks.OrderBy(b => b.Name).ToList();

            var paychecksWithNone = new List<Paycheck> { new Paycheck { Id = 0, Name = "(None)" } };
            paychecksWithNone.AddRange(paychecks);
            PaychecksWithNone = new ObservableCollection<Paycheck>(paychecksWithNone);

            var buckets = await _budgetService.GetAllBucketsAsync(true);
            buckets = buckets.OrderBy(b => b.Name).ToList();

            var bucketsWithNone = new List<BudgetBucket> { new BudgetBucket { Id = 0, Name = "(None)" } };
            bucketsWithNone.AddRange(buckets.Where(b => !b.IsArchived));
            BucketsWithNone = new ObservableCollection<BudgetBucket>(bucketsWithNone);
            
            var subCategories = await _budgetService.GetAllSubCategoriesAsync(true);
            subCategories = subCategories.OrderBy(b => b.Name).ToList();
            
            var subCategoriesWithNone = new List<SubCategory> { new SubCategory { Id = 0, Name = "(None)" } };
            subCategoriesWithNone.AddRange(subCategories.Where(b => !b.IsArchived));
            SubCategoriesWithNone = new ObservableCollection<SubCategory>(subCategoriesWithNone);
        }
        catch (Exception ex) {
            Log.Error(ex, "Failure while loading period data in NewTransactionViewModel.");
            
        }

        await LoadPeriodBillsAsync();
        await LoadPeriodTransactionsAsync();
    }

    private async Task LoadPeriodBillsAsync() {
        try {
            var pBills = (await _budgetService.GetPeriodBillsAsync(CurrentPeriodDate)).ToList();
            pBills = pBills.OrderBy(pb => pb.DueDate).ToList();

            CurrentPeriodBills = new ObservableCollection<PeriodBill>(pBills);
            OnPropertyChanged(nameof(CurrentPeriodBills));
        }
        catch (Exception ex) {
            Log.Error(ex, "Error loading period bills in NewTransactionViewModel.");
            
        }
    }
    
    private DateTime GetNextPeriodDate(DateTime currentPeriodStart) {
        try {
            var allPaycheckDates = new List<DateTime>();
            var end = currentPeriodStart.AddYears(1);
            foreach (var pay in Paychecks.Where(p => p.Id != 0)) {
                var nextPay = pay.StartDate;
                while (nextPay < end) {
                    allPaycheckDates.Add(nextPay);
                    nextPay = pay.Frequency switch {
                        Frequency.Weekly => nextPay.AddDays(7),
                        Frequency.BiWeekly => nextPay.AddDays(14),
                        Frequency.Monthly => nextPay.AddMonths(1),
                        _ => nextPay.AddYears(100)
                    };
                }
            }

            var sortedDates = allPaycheckDates.Distinct().OrderBy(d => d).ToList();
            var nextDate = sortedDates.FirstOrDefault(d => d > currentPeriodStart);

            return nextDate == DateTime.MinValue ? currentPeriodStart.AddDays(14) : nextDate;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error calculating next period date in NewTransactionViewModel.");
            
            return currentPeriodStart.AddDays(14);
        }
    }

    private async Task LoadPeriodTransactionsAsync() {
        try {
            var nextPeriodDate = GetNextPeriodDate(CurrentPeriodDate);
            var transactions = (await _budgetService.GetTransactionsAsync(CurrentPeriodDate, nextPeriodDate)).ToList();
            transactions = transactions.OrderBy(pb => pb.TransactionDate).ToList();
            CurrentPeriodTransactions = new ObservableCollection<Transaction>(transactions);
            OnPropertyChanged(nameof(CurrentPeriodTransactions));
        }
        catch (Exception ex) {
            Log.Error(ex, "Error loading period transactions in NewTransactionViewModel.");
            
        }
    }

    
    public ObservableCollection<Paycheck> PeriodPaychecks {
        get => _periodPayChecks;
        set => SetProperty(ref _periodPayChecks, value);
    }
    
    public ObservableCollection<Paycheck> Paychecks {
        get => _paychecks;
        set => SetProperty(ref _paychecks, value);
    }

    private async Task LoadPaychecksAsync() {
        try {
            var paychecks = await _budgetService.GetAllPaychecksAsync();
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
        catch (Exception ex) {
            Log.Error(ex, "Error loading paychecks asynchronously in NewTransactionViewModel.");
            
        }
    }

    private void SetCurrentPeriodDate(int? id = null) {
        try {
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
        }
        catch (Exception ex) {
            Log.Error(ex, "Error setting current period date in NewTransactionViewModel.");
            
        }
    }

    private async Task OnSave() {
        try {
            if (EditingTransactionClone == null) return;

            if (EditingTransactionClone.AccountId == 0) EditingTransactionClone.AccountId = null;
            if (EditingTransactionClone.ToAccountId == 0) EditingTransactionClone.ToAccountId = null;
            if (EditingTransactionClone.BillId == 0) EditingTransactionClone.BillId = null;
            if (EditingTransactionClone.BucketId == 0) EditingTransactionClone.BucketId = null;

            await _budgetService.UpsertTransactionAsync(EditingTransactionClone);

            _closeCallback?.Invoke(this, true);
      
            if (EditingTransactionClone != null) {
                EditingTransactionClone.PropertyChanged -= EditingTransactionClone_PropertyChanged;
            }
            
            EditingTransactionClone = null;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error saving transaction in NewTransactionViewModel.");
            
        }
    }

    private void OnCancel() {
        try {
            if (EditingTransactionClone != null) {
                EditingTransactionClone.PropertyChanged -= EditingTransactionClone_PropertyChanged;
            }
            EditingTransactionClone = null;
            _closeCallback?.Invoke(this, false);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error canceling transaction in NewTransactionViewModel.");
            
        }
    }
    
    private async void EditingTransactionClone_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        try {
            if (e.PropertyName == nameof(Transaction.SubCategoryId)) {
                ApplyDefaultBucketForSubCategory();
            }
            else if (e.PropertyName == nameof(Transaction.Description)) {
                await TryAutoSuggestSubCategoryAsync();
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling EditingTransactionClone_PropertyChanged in NewTransactionViewModel.");
            
        }
    }
    
    private void ApplyDefaultBucketForSubCategory() {
        try {
            if (EditingTransactionClone == null) return;
            if (EditingTransactionClone.Id == 0 &&
                EditingTransactionClone.SubCategoryId.HasValue &&
                !EditingTransactionClone.BucketId.HasValue) {
                var selectedSub = SubCategoriesWithNone?
                    .FirstOrDefault(s => s.Id == EditingTransactionClone.SubCategoryId.Value);

                if (selectedSub != null && selectedSub.DefaultBucketId.HasValue) {
                    EditingTransactionClone.BucketId = selectedSub.DefaultBucketId.Value;
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error applying default bucket for subcategory in NewTransactionViewModel.");
            
        }
    }

    private async Task TryAutoSuggestSubCategoryAsync() {
        try {
            if (EditingTransactionClone == null) return;
            if (EditingTransactionClone.Id == 0 &&
                !EditingTransactionClone.SubCategoryId.HasValue &&
                !string.IsNullOrWhiteSpace(EditingTransactionClone.Description)) {
                var suggestedSubId = await _budgetService.GetSuggestedSubCategoryIdAsync(
                    EditingTransactionClone.Description,
                    EditingTransactionClone.TransactionDate);

                if (suggestedSubId.HasValue) {
                    EditingTransactionClone.SubCategoryId = suggestedSubId.Value;
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error auto-suggesting subcategory in NewTransactionViewModel.");
            
        }
    }
}