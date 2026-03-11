using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using MyBudget.Models;
using MyBudget.Services;

namespace MyBudget.ViewModels;

public class MainViewModel : ViewModelBase {
    private readonly BudgetService _budgetService;
    private readonly IProjectionEngine _projectionEngine;
    private ObservableCollection<Bill> _bills = new();
    private ObservableCollection<Paycheck> _paychecks = new();
    private ObservableCollection<Account> _accounts = new();
    private ObservableCollection<ProjectionItem> _projections = new();
    private ObservableCollection<PeriodBill> _currentPeriodBills = new();
    private ObservableCollection<BudgetBucket> _buckets = new();
    private ObservableCollection<PeriodBucket> _currentPeriodBuckets = new();
    private ObservableCollection<Transaction> _currentPeriodTransactions = new();
    private Bill? _selectedBill;
    private BudgetBucket? _selectedBucket;
    private PeriodBill? _selectedPeriodBill;
    private PeriodBucket? _selectedPeriodBucket;
    private Account? _selectedAccount;
    private Transaction? _selectedTransaction;
    private bool _isEditingBill;
    private bool _isEditingBucket;
    private bool _isEditingPeriodBucket;
    private bool _isEditingPeriodBill;
    private bool _isEditingAccount;
    private bool _isEditingTransaction;
    private bool _isCalculatingProjections;
    private Bill? _editingBillClone;
    private PeriodBill? _editingPeriodBillClone;
    private BudgetBucket? _editingBucketClone;
    private PeriodBucket? _editingPeriodBucketClone;
    private Account? _editingAccountClone;
    private Transaction? _editingTransactionClone;
    private Paycheck? _editingPaycheckClone;
    private DateTime _currentPeriodDate = DateTime.MinValue;
    private bool _showByMonth;
    private int _selectedPeriodPaycheckId;
    private ObservableCollection<Paycheck> _periodPaychecks = new();
    private bool _isEditingPaycheck;
    private Paycheck? _selectedPaycheck;
    
    public bool IsCalculatingProjections => _isCalculatingProjections;

    public static MainViewModel? Instance { get; private set; }

    public MainViewModel() {
        Instance = this;
        _budgetService = new BudgetService();
        _projectionEngine = new ProjectionEngine();
        LoadData();
        InitializePeriod();
        LoadPeriodData();
        CalculateProjections();
    }

    #region Properties

    public ObservableCollection<Bill> Bills {
        get => _bills;
        set => SetProperty(ref _bills, value);
    }

    public ObservableCollection<Paycheck> Paychecks {
        get => _paychecks;
        set => SetProperty(ref _paychecks, value);
    }

    public ObservableCollection<Account> Accounts {
        get => _accounts;
        set => SetProperty(ref _accounts, value);
    }

    public AccountType[] AccountTypes => (AccountType[])Enum.GetValues(typeof(AccountType));

    public ObservableCollection<ProjectionItem> Projections {
        get => _projections;
        set => SetProperty(ref _projections, value);
    }

    public ObservableCollection<PeriodBill> CurrentPeriodBills {
        get => _currentPeriodBills;
        set => SetProperty(ref _currentPeriodBills, value);
    }

    public ObservableCollection<BudgetBucket> Buckets {
        get => _buckets;
        set => SetProperty(ref _buckets, value);
    }

    public ObservableCollection<PeriodBucket> CurrentPeriodBuckets {
        get => _currentPeriodBuckets;
        set => SetProperty(ref _currentPeriodBuckets, value);
    }

    public ObservableCollection<Transaction> CurrentPeriodTransactions {
        get => _currentPeriodTransactions;
        set => SetProperty(ref _currentPeriodTransactions, value);
    }

    public bool ShowByMonth {
        get => _showByMonth;
        set {
            if (SetProperty(ref _showByMonth, value)) {
                InitializePeriod();
                LoadPeriodData();
            }
        }
    }

    public int SelectedPeriodPaycheckId {
        get => _selectedPeriodPaycheckId;
        set {
            if (SetProperty(ref _selectedPeriodPaycheckId, value)) {
                SetCurrentPeriodDate(value);
                CalculateProjections();
            }
        }
    }

    public ObservableCollection<Paycheck> PeriodPaychecks {
        get => _periodPaychecks;
        set => SetProperty(ref _periodPaychecks, value);
    }

    public string PeriodDisplay {
        get {
            if (ShowByMonth) return _currentPeriodDate.ToString("MMMM yyyy");
            return $"Period: {_currentPeriodDate:d}";
        }
    }

    public DateTime CurrentPeriodDate {
        get => _currentPeriodDate;
        set {
            if (SetProperty(ref _currentPeriodDate, value)) {
                OnPropertyChanged(nameof(PeriodDisplay));
                LoadPeriodData();
            }
        }
    }

    public Bill? SelectedBill {
        get => _selectedBill;
        set {
            if (SetProperty(ref _selectedBill, value)) {
                OnPropertyChanged(nameof(CanEditBill));
            }
        }
    }
    
    public PeriodBill? SelectedPeriodBill {
        get => _selectedPeriodBill;
        set {
            if (SetProperty(ref _selectedPeriodBill, value)) {
                OnPropertyChanged(nameof(CanEditPeriodBill));
            }
        } 
    }

    public BudgetBucket? SelectedBucket {
        get => _selectedBucket;
        set {
            if (SetProperty(ref _selectedBucket, value)) {
                OnPropertyChanged(nameof(CanEditBucket));
            }
        }
    }

    public PeriodBucket? SelectedPeriodBucket {
        get => _selectedPeriodBucket;
        set {
            if (SetProperty(ref _selectedPeriodBucket, value)) {
                OnPropertyChanged(nameof(CanEditPeriodBucket));
            }
        } 
    }
    

    public Account? SelectedAccount {
        get => _selectedAccount;
        set {
            if (SetProperty(ref _selectedAccount, value)) {
                OnPropertyChanged(nameof(CanEditAccount));
            }
        }
    }

    public Transaction? SelectedTransaction {
        get => _selectedTransaction;
        set {
            if (SetProperty(ref _selectedTransaction, value)) {
                OnPropertyChanged(nameof(CanEditTransaction));
            }
        }
    }

    public Paycheck? SelectedPaycheck {
        get => _selectedPaycheck;
        set {
            if (SetProperty(ref _selectedPaycheck, value)) {
                OnPropertyChanged(nameof(CanEditPaycheck));
            }
        }
    }
    
    public bool IsEditingBill {
        get => _isEditingBill;
        set {
            if (SetProperty(ref _isEditingBill, value)) {
                OnPropertyChanged(nameof(IsNotEditingBill));
                OnPropertyChanged(nameof(CanEditBill));
            }
        }
    }
    
    public bool IsNotEditingBill => !IsEditingBill;
    public bool CanEditBill => SelectedBill != null && !IsEditingBill;
    
    public bool IsEditingPaycheck {
        get => _isEditingPaycheck;
        set {
            if (SetProperty(ref _isEditingPaycheck, value)) {
                OnPropertyChanged(nameof(IsNotEditingPaycheck));
                OnPropertyChanged(nameof(CanEditPaycheck));
            }
        }
    }
    public bool IsNotEditingPaycheck => !IsEditingPaycheck;
    
    public bool CanEditPaycheck => SelectedPaycheck != null && !IsEditingPaycheck;
    
    public bool IsEditingPeriodBucket {
        get => _isEditingPeriodBucket;
        set {
            if (SetProperty(ref _isEditingPeriodBucket, value)) {
                OnPropertyChanged(nameof(IsNotEditingPeriodBucket));
                OnPropertyChanged(nameof(CanEditPeriodBucket));
            }
        }
    }
    
    public bool IsEditingBucket {
        get => _isEditingBucket;
        set {
            if (SetProperty(ref _isEditingBucket, value)) {
                OnPropertyChanged(nameof(IsNotEditingBucket));
                OnPropertyChanged(nameof(CanEditBucket));
            }
        }
    }

    public bool IsNotEditingBucket => !IsEditingBucket;
    
    public bool CanEditBucket => SelectedBucket != null && !IsEditingBucket;
    
    public bool IsNotEditingPeriodBill => !IsEditingPeriodBill;
    
    public bool IsEditingPeriodBill {
        get => _isEditingPeriodBill;
        set {
            if (SetProperty(ref _isEditingPeriodBill, value)) {
                OnPropertyChanged(nameof(IsNotEditingPeriodBill));
                OnPropertyChanged(nameof(CanEditPeriodBill));
            }
        }
    }
        
    public bool CanEditPeriodBill => SelectedPeriodBill != null && !IsEditingPeriodBill;
    
    public bool IsNotEditingPeriodBucket => !IsEditingPeriodBucket;
    
    public bool CanEditPeriodBucket => SelectedPeriodBucket != null && !IsEditingPeriodBucket;
    

    public bool IsEditingAccount {
        get => _isEditingAccount;
        set {
            if (SetProperty(ref _isEditingAccount, value)) {
                OnPropertyChanged(nameof(IsNotEditingAccount));
                OnPropertyChanged(nameof(CanEditAccount));
            }
        }
    }

    public bool IsNotEditingAccount => !IsEditingAccount;
    public bool CanEditAccount => SelectedAccount != null && !IsEditingAccount;

    public bool IsEditingTransaction {
        get => _isEditingTransaction;
        set {
            if (SetProperty(ref _isEditingTransaction, value)) {
                OnPropertyChanged(nameof(IsNotEditingTransaction));
                OnPropertyChanged(nameof(CanEditTransaction));
            }
        }
    }

    public bool IsNotEditingTransaction => !IsEditingTransaction;
    public bool CanEditTransaction => SelectedTransaction != null && !IsEditingTransaction;

    public Bill? EditingBillClone {
        get => _editingBillClone;
        set => SetProperty(ref _editingBillClone, value);
    }
    
    public PeriodBill? EditingPeriodBillClone {
        get => _editingPeriodBillClone;
        set => SetProperty(ref _editingPeriodBillClone, value);
    }
    
    public BudgetBucket? EditingBucketClone {
        get => _editingBucketClone;
        set => SetProperty(ref _editingBucketClone, value);
    }
    
    public PeriodBucket? EditingPeriodBucketClone {
        get => _editingPeriodBucketClone;
        set => SetProperty(ref _editingPeriodBucketClone, value);
    }
    
    public Account? EditingAccountClone {
        get => _editingAccountClone;
        set => SetProperty(ref _editingAccountClone, value);
    }

    public Transaction? EditingTransactionClone {
        get => _editingTransactionClone;
        set => SetProperty(ref _editingTransactionClone, value);
    }
    
    public Paycheck? EditingPaycheckClone {
        get => _editingPaycheckClone;
        set => SetProperty(ref _editingPaycheckClone, value);
    }
    
    
    public ICommand AddBillCommand => new RelayCommand(_ => AddBill(), _ => IsNotEditingBill);
    public ICommand EditBillCommand => new RelayCommand(_ => EditBill(), _ => CanEditBill);
    public ICommand SaveBillCommand => new RelayCommand(_ => SaveBill(), _ => IsEditingBill);
    public ICommand CancelBillCommand => new RelayCommand(_ => CancelBill(), _ => IsEditingBill);
    public ICommand DeleteBillCommand => new RelayCommand(b => DeleteBill(b as Bill));
    
    public ICommand EditPeriodBillCommand => new RelayCommand(_ => EditPeriodBill(), _ => CanEditPeriodBill);
    public ICommand SavePeriodBillCommand => new RelayCommand(_ => SavePeriodBill(), _ => IsEditingPeriodBill);
    public ICommand CancelPeriodBillCommand => new RelayCommand(_ => CancelPeriodBill(), _ => IsEditingPeriodBill);
    public ICommand DeletePeriodBillCommand => new RelayCommand(pb => DeletePeriodBill(pb as PeriodBill));
    
    public ICommand AddBucketCommand => new RelayCommand(_ => AddBucket(), _ => IsNotEditingBucket);
    public ICommand EditBucketCommand => new RelayCommand(_ => EditBucket(), _ => CanEditBucket);
    public ICommand SaveBucketCommand => new RelayCommand(_ => SaveBucket(), _ => IsEditingBucket);
    public ICommand CancelBucketCommand => new RelayCommand(_ => CancelBucket(), _ => IsEditingBucket);
    public ICommand DeleteBucketCommand => new RelayCommand(b => DeleteBucket(b as BudgetBucket));
    
    public ICommand EditPeriodBucketCommand => new RelayCommand(_ => EditPeriodBucket(), _ => CanEditPeriodBucket);
    public ICommand SavePeriodBucketCommand => new RelayCommand(_ => SavePeriodBucket(), _ => IsEditingPeriodBucket);
    public ICommand CancelPeriodBucketCommand => new RelayCommand(_ => CancelPeriodBucket(), _ => IsEditingPeriodBucket);
    public ICommand DeletePeriodBucketCommand => new RelayCommand(pb => DeletePeriodBucket(pb as PeriodBucket));
    
    public ICommand AddTransactionCommand =>
        new RelayCommand(_ => AddTransaction(), _ => IsNotEditingTransaction);
    public ICommand EditTransactionCommand =>
        new RelayCommand(_ => EditTransaction(), _ => CanEditTransaction);
    public ICommand SaveTransactionCommand =>
        new RelayCommand(_ => SaveTransaction(), _ => IsEditingTransaction);
    public ICommand CancelTransactionCommand =>
        new RelayCommand(_ => CancelTransaction(), _ => IsEditingTransaction);
    public ICommand DeleteTransactionCommand =>
        new RelayCommand(t => DeleteTransaction(t as Transaction));
    
    public ICommand AddPaycheckCommand => new RelayCommand(_ => AddPaycheck());
    public ICommand EditPaycheckCommand => new RelayCommand(_ => EditPaycheck(), _ => CanEditPaycheck);
    public ICommand SavePaycheckCommand => new RelayCommand(_ => SavePaycheck(), _ => IsEditingPaycheck);
    public ICommand CancelPaycheckCommand => new RelayCommand(_ => CancelPaycheck(), _ => IsEditingPaycheck);
    public ICommand DeletePaycheckCommand => new RelayCommand(p => DeletePaycheck(p as Paycheck));

    public ICommand AddAccountCommand => new RelayCommand(_ => AddAccount(), _ => IsNotEditingAccount);
    public ICommand EditAccountCommand => new RelayCommand(_ => EditAccount(), _ => CanEditAccount);
    public ICommand SaveAccountCommand => new RelayCommand(_ => SaveAccount(), _ => IsEditingAccount);
    public ICommand CancelAccountCommand => new RelayCommand(_ => CancelAccount(), _ => IsEditingAccount);
    public ICommand DeleteAccountCommand =>
        new RelayCommand(a => DeleteAccount(a as Account), _ => IsNotEditingAccount);
    
    public ICommand NextPeriodCommand => new RelayCommand(_ => NavigatePeriod(1));
    public ICommand PrevPeriodCommand => new RelayCommand(_ => NavigatePeriod(-1));
    
    public ICommand ShowAmortizationCommand => new RelayCommand(a => ShowAmortization(a as Account ?? throw new InvalidOperationException()));
    public ICommand ShowAboutCommand => new RelayCommand(_ => ShowAbout());

    private bool _isLoadingData;

    #endregion

    #region Events

    private void Item_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (_isLoadingData) return;
        if (sender is Bill b) _budgetService.UpsertBill(b);
        if (sender is Paycheck p) {
            _budgetService.UpsertPaycheck(p);
            RefreshPaychecks();
            if (p.Id == _selectedPeriodPaycheckId) {
                OnPropertyChanged(nameof(SelectedPeriodPaycheckId));
                LoadPeriodData();
            }
        }

        if (sender is Account a) _budgetService.UpsertAccount(a);
        if (sender is BudgetBucket bb) _budgetService.UpsertBucket(bb);
        CalculateProjections();
    }

    private void PeriodBill_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (sender is PeriodBill pb) {
            _budgetService.UpsertPeriodBill(pb);
            LoadPeriodData();
            CalculateProjections();
        }
    }

    private void PeriodBucket_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (sender is PeriodBucket pb) {
            _budgetService.UpsertPeriodBucket(pb);
            LoadPeriodData();
            CalculateProjections();
        }
    }

    private void Transaction_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (sender is Transaction t) {
            _budgetService.UpsertTransaction(t);
            LoadPeriodData();
            CalculateProjections();
        }
    }

    #endregion

    #region Bill CRUD

    private void AddBill() {
        //var bill = new Bill { Name = "New Bill", ExpectedAmount = 0, DueDay = 1, IsActive = true };
        // _budgetService.UpsertBill(bill);
        // LoadData();
        // CalculateProjections();

        EditingBillClone = new Bill { Name = "New Bill", ExpectedAmount = 0, DueDay = 1, IsActive = true };
        SelectedBill = null;
        IsEditingBill = true;
    }

    private void EditBill() {
        if (SelectedBill != null) {
            EditingBillClone = new Bill {
                Id = SelectedBill.Id, Name = SelectedBill.Name, ExpectedAmount = SelectedBill.ExpectedAmount,
                Frequency = SelectedBill.Frequency, DueDay = SelectedBill.DueDay, AccountId = SelectedBill.AccountId,
                ToAccountId = SelectedBill.ToAccountId, NextDueDate = SelectedBill.NextDueDate,
                Category = SelectedBill.Category, IsActive = SelectedBill.IsActive
            };
            IsEditingBill = true;
        }
    }

    private void SaveBill() {
        //if (EditingBillClone != null && SelectedBill != null) {
        if (EditingBillClone != null) {
            if (SelectedBill != null) {
                UpdateBillFromClone(SelectedBill, EditingBillClone);
                _budgetService.UpsertBill(SelectedBill);
            }
            else {
                _budgetService.UpsertBill(EditingBillClone);
                LoadData();
            }

            IsEditingBill = false;
            EditingBillClone = null;
            CalculateProjections();
        }
    }

    private void UpdateBillFromClone(Bill target, Bill clone) {
        target.Name = clone.Name;
        target.ExpectedAmount = clone.ExpectedAmount;
        target.Frequency = clone.Frequency;
        target.DueDay = clone.DueDay;
        target.AccountId = clone.AccountId;
        target.ToAccountId = clone.ToAccountId;
        target.NextDueDate = clone.NextDueDate;
        target.Category = clone.Category;
        target.IsActive = clone.IsActive;
    }

    private void CancelBill() {
        IsEditingBill = false;
        EditingBillClone = null;
    }

    private void DeletePeriodBill(PeriodBill? pb) {
        if (pb != null) {
            MessageBoxResult messageBoxResult = MessageBox.Show(
                "Are you sure you want to delete this period's bill?", // Message
                "Delete Confirmation", // Title
                MessageBoxButton.YesNo, // Buttons
                MessageBoxImage.Warning // Icon
            );

            // Check the user's response
            if (messageBoxResult == MessageBoxResult.Yes) {
                // User confirmed deletion, proceed with your delete logic here
                _budgetService.DeletePeriodBill(pb.Id);
                IsEditingPeriodBill = false;
                EditingPeriodBillClone = null;
                LoadPeriodData();
                CalculateProjections();
            }
        }
    }
    
    
    
    private void EditPeriodBill() {
        //until a user customizes a bucket, it uses the budgeted bucket and the period bucket is a copy of that.
        if (SelectedPeriodBill != null) {
            EditingPeriodBillClone = new PeriodBill {
                Id = SelectedPeriodBill.Id, 
                BillName = SelectedPeriodBill.BillName,
                ActualAmount = SelectedPeriodBill.ActualAmount,
                BillId = SelectedPeriodBill.BillId,
                FitId = SelectedPeriodBill.FitId,
                DueDate = SelectedPeriodBill.DueDate,
                PeriodDate = SelectedPeriodBill.PeriodDate,
                IsPaid = SelectedPeriodBill.IsPaid
            };
            
            IsEditingPeriodBill = true;
        }
    }
    
    private void SavePeriodBill() {
        //until a user customizes a bucket, it uses the budgeted bucket and the period bucket is a copy of that.
        if (EditingPeriodBillClone != null) {
            if (SelectedPeriodBill != null) {
                UpdatePeriodBillFromClone(SelectedPeriodBill, EditingPeriodBillClone);
            }
            else {
                _budgetService.UpsertPeriodBill(EditingPeriodBillClone);
            }

            LoadPeriodData();
            IsEditingPeriodBill = false;
            EditingPeriodBillClone = null;
            CalculateProjections();
        }
    }
    

    private void CancelPeriodBill() {
        IsEditingPeriodBill = false;
        EditingPeriodBillClone = null;
    }
    
    
    
    private void UpdatePeriodBillFromClone(PeriodBill target, PeriodBill clone) {
        target.Id = clone.Id;
        target.ActualAmount = clone.ActualAmount;
        target.DueDate = clone.DueDate;
        target.IsPaid = clone.IsPaid;
    }
    
    private void DeleteBill(Bill? b) {
        if (b != null) {
            MessageBoxResult messageBoxResult = MessageBox.Show(
                "Are you sure you want to delete this bill?", // Message
                "Delete Confirmation", // Title
                MessageBoxButton.YesNo, // Buttons
                MessageBoxImage.Warning // Icon
            );

            // Check the user's response
            if (messageBoxResult == MessageBoxResult.Yes) {
                // User confirmed deletion, proceed with your delete logic here
                _budgetService.DeleteBill(b.Id);
                IsEditingBill = false;
                EditingBillClone = null;
                LoadData();
                CalculateProjections();
            }
        }
    }

    #endregion

    #region Bucket CRUD

    private void AddBucket() {
        // var bucket = new BudgetBucket { Name = "New Bucket", ExpectedAmount = 0 };
        // _budgetService.UpsertBucket(bucket);
        // LoadData();
        // CalculateProjections();
        EditingBucketClone = new BudgetBucket { Name = "New Bucket", ExpectedAmount = 0 };
        SelectedBucket = null;
        IsEditingBucket = true;
    }

    private void EditBucket() {
        if (SelectedBucket != null) {
            EditingBucketClone = new BudgetBucket {
                Id = SelectedBucket.Id, Name = SelectedBucket.Name, ExpectedAmount = SelectedBucket.ExpectedAmount,
                AccountId = SelectedBucket.AccountId
            };
            IsEditingBucket = true;
        }
    }
    
    private void SaveBucket() {
        //if (EditingBucketClone != null && SelectedBucket != null) {
        if (EditingBucketClone != null) {
            if (SelectedBucket != null) {
                UpdateBucketFromClone(SelectedBucket, EditingBucketClone);
                _budgetService.UpsertBucket(SelectedBucket);
            }
            else {
                _budgetService.UpsertBucket(EditingBucketClone);
                LoadData();
            }

            IsEditingBucket = false;
            EditingBucketClone = null;
            CalculateProjections();
        }
    }
    
    private void UpdateBucketFromClone(BudgetBucket target, BudgetBucket clone) {
        target.Name = clone.Name;
        target.ExpectedAmount = clone.ExpectedAmount;
        target.AccountId = clone.AccountId;
        target.PaycheckId = clone.PaycheckId;
    }
    
    private void CancelBucket() {
        IsEditingBucket = false;
        EditingBucketClone = null;
    }
    
    private void DeleteBucket(BudgetBucket? b) {
        if (b != null) {
            MessageBoxResult messageBoxResult = MessageBox.Show(
                "Are you sure you want to delete this bucket?", // Message
                "Delete Confirmation", // Title
                MessageBoxButton.YesNo, // Buttons
                MessageBoxImage.Warning // Icon
            );

            // Check the user's response
            if (messageBoxResult == MessageBoxResult.Yes) {
                // User confirmed deletion, proceed with your delete logic here
                _budgetService.DeleteBucket(b.Id);
                IsEditingBucket = false;
                EditingBucketClone = null;
                LoadData();
                CalculateProjections();
            }
        }
    }
    
    private void EditPeriodBucket() {
        //until a user customizes a bucket, it uses the budgeted bucket and the period bucket is a copy of that.
        if (SelectedPeriodBucket != null) {
            EditingPeriodBucketClone = new PeriodBucket {
                Id = SelectedPeriodBucket.Id, 
                BucketName = SelectedPeriodBucket.BucketName,
                ActualAmount = SelectedPeriodBucket.ActualAmount,
                BucketId = SelectedPeriodBucket.BucketId,
                FitId = SelectedPeriodBucket.FitId,
                PeriodDate = SelectedPeriodBucket.PeriodDate,
                IsPaid = SelectedPeriodBucket.IsPaid
            };
            IsEditingPeriodBucket = true;
        }
    }
    
    private void SavePeriodBucket() {
        //until a user customizes a bucket, it uses the budgeted bucket and the period bucket is a copy of that.
        if (EditingPeriodBucketClone != null) {
            if (SelectedPeriodBucket != null) {
                UpdatePeriodBucketFromClone(SelectedPeriodBucket, EditingPeriodBucketClone);
                //_budgetService.UpsertPeriodBucket(SelectedPeriodBucket);
            }
            else {
                _budgetService.UpsertPeriodBucket(EditingPeriodBucketClone);
            }

            LoadPeriodData();
            IsEditingPeriodBucket = false;
            EditingPeriodBucketClone = null;
            CalculateProjections();
        }
    }

    private void UpdatePeriodBucketFromClone(PeriodBucket target, PeriodBucket clone) {
        target.Id = clone.Id;
        target.BucketName = clone.BucketName;
        target.ActualAmount = clone.ActualAmount;
        target.BucketId = clone.BucketId;
        target.FitId = clone.FitId;
        target.PeriodDate = clone.PeriodDate;
        target.IsPaid = clone.IsPaid;
    }

    private void CancelPeriodBucket() {
        IsEditingPeriodBucket = false;
        EditingPeriodBucketClone = null;
    }
    
    private void DeletePeriodBucket(PeriodBucket? pb) {
        if (pb != null) {
            MessageBoxResult messageBoxResult = MessageBox.Show(
                "Are you sure you want to delete this period's bucket?\r\n\r\nIt will use the budgetted amount for the bucket instead. Save a $0 amount if you do not want to budget for this bucket for this period.", // Message
                "Delete Confirmation", // Title
                MessageBoxButton.YesNo, // Buttons
                MessageBoxImage.Warning // Icon
            );

            // Check the user's response
            if (messageBoxResult == MessageBoxResult.Yes) {
                // User confirmed deletion, proceed with your delete logic here
                _budgetService.DeletePeriodBucket(pb.Id);
                IsEditingPeriodBucket = false;
                EditingPeriodBucketClone = null;
                LoadPeriodData();
                CalculateProjections();
            }
        }
    }

    #endregion

    #region Transaction CRUD

    private void AddTransaction() {
        EditingTransactionClone = new Transaction
            { Description = "New Transaction", Amount = 0, Date = DateTime.Today, PeriodDate = CurrentPeriodDate };
        SelectedTransaction = null;
        IsEditingTransaction = true;
    }

    private void EditTransaction() {
        if (SelectedTransaction != null) {
            EditingTransactionClone = new Transaction {
                Id = SelectedTransaction.Id, 
                Description = SelectedTransaction.Description,
                Amount = SelectedTransaction.Amount, 
                Date = SelectedTransaction.Date,
                AccountId = SelectedTransaction.AccountId, 
                ToAccountId = SelectedTransaction.ToAccountId,
                BucketId = SelectedTransaction.BucketId, 
                PeriodDate = SelectedTransaction.PeriodDate,
                IsPrincipalOnly = SelectedTransaction.IsPrincipalOnly,
                IsRebalance = SelectedTransaction.IsRebalance, 
                PaycheckId = SelectedTransaction.PaycheckId,
                BillId = SelectedTransaction.BillId,
                BillName = SelectedTransaction.BillName,
                PaycheckOccurrenceDate = SelectedTransaction.PaycheckOccurrenceDate
            };
            IsEditingTransaction = true;
        }
    }

    private void SaveTransaction() {
        if (EditingTransactionClone != null) {
            if (SelectedTransaction != null) {
                UpdateTransactionFromClone(SelectedTransaction, EditingTransactionClone);
                _budgetService.UpsertTransaction(SelectedTransaction);
            }
            else {
                _budgetService.UpsertTransaction(EditingTransactionClone);
            }

            IsEditingTransaction = false;
            EditingTransactionClone = null;
            
            LoadPeriodData();
            CalculateProjections();
        }
    }

    private void UpdateTransactionFromClone(Transaction target, Transaction clone) {
        target.Description = clone.Description;
        target.Amount = clone.Amount;
        target.Date = clone.Date;
        target.AccountId = clone.AccountId;
        target.ToAccountId = clone.ToAccountId;
        target.BucketId = clone.BucketId;
        target.PeriodDate = clone.PeriodDate;
        target.IsPrincipalOnly = clone.IsPrincipalOnly;
        target.IsRebalance = clone.IsRebalance;
        target.PaycheckId = clone.PaycheckId;
        target.PaycheckOccurrenceDate = clone.PaycheckOccurrenceDate;
    }

    private void CancelTransaction() {
        if (SelectedTransaction != null && SelectedTransaction.Id == 0) {
            CurrentPeriodTransactions.Remove(SelectedTransaction);
        }

        IsEditingTransaction = false;
        EditingTransactionClone = null;
    }

    private void DeleteTransaction(Transaction? t) {
        if (t != null) {
            MessageBoxResult messageBoxResult = MessageBox.Show(
                "Are you sure you want to delete this transaction?", // Message
                "Delete Confirmation", // Title
                MessageBoxButton.YesNo, // Buttons
                MessageBoxImage.Warning // Icon
            );

            // Check the user's response
            if (messageBoxResult == MessageBoxResult.Yes) {
                // User confirmed deletion, proceed with your delete logic here
                _budgetService.DeleteTransaction(t.Id);
                IsEditingTransaction = false;
                EditingTransactionClone = null;
                LoadPeriodData();
                CalculateProjections();
            }
        }
    }

    #endregion

    #region Paycheck CRUD

    private void AddPaycheck() {
        
        EditingPaycheckClone = new Paycheck
            { Name = "New Paycheck", ExpectedAmount = 0, StartDate = DateTime.Today, Frequency = Frequency.BiWeekly };
        SelectedPaycheck = null;
        IsEditingPaycheck = true;
        
        // var p = new Paycheck
        //     { Name = "New Paycheck", ExpectedAmount = 0, StartDate = DateTime.Today, Frequency = Frequency.BiWeekly };
        // _budgetService.UpsertPaycheck(p);
        // LoadData();
        // RefreshPaychecks();
        // LoadPaychecks();
        // CalculateProjections();
    }
    
    private void EditPaycheck() {
        if (SelectedPaycheck != null) {
            EditingPaycheckClone = new Paycheck {
                Id = SelectedPaycheck.Id, 
                Name = SelectedPaycheck.Name,
                ExpectedAmount = SelectedPaycheck.ExpectedAmount, 
                Frequency = SelectedPaycheck.Frequency,
                StartDate = SelectedPaycheck.StartDate, 
                EndDate = SelectedPaycheck.EndDate,
                AccountId = SelectedPaycheck.AccountId, 
                IsBalanced = SelectedPaycheck.IsBalanced
            };
            IsEditingPaycheck = true;
        }
    }
    
    private void SavePaycheck() {
        if (EditingPaycheckClone != null) {
            if (SelectedPaycheck != null) {
                UpdatePaycheckFromClone(SelectedPaycheck, EditingPaycheckClone);
                _budgetService.UpsertPaycheck(SelectedPaycheck);
            }
            else {
                _budgetService.UpsertPaycheck(EditingPaycheckClone);
            }

            IsEditingPaycheck = false;
            EditingPaycheckClone = null;
            
            LoadData();
            RefreshPaychecks();
            LoadPaychecks();
            CalculateProjections();
        }
    }

    private void UpdatePaycheckFromClone(Paycheck target, Paycheck clone) {
        target.Name = clone.Name;
        target.ExpectedAmount = clone.ExpectedAmount;
        target.Frequency = clone.Frequency;
        target.StartDate = clone.StartDate;
        target.EndDate = clone.EndDate;
        target.AccountId = clone.AccountId;
        target.IsBalanced = clone.IsBalanced;
    }
    
    private void CancelPaycheck() {
        IsEditingPaycheck = false;
        EditingPaycheckClone = null;
    }
    
    private void DeletePaycheck(Paycheck? p) {
        if (p != null) {
            MessageBoxResult messageBoxResult = MessageBox.Show(
                "Are you sure you want to delete this paycheck?", // Message
                "Delete Confirmation", // Title
                MessageBoxButton.YesNo, // Buttons
                MessageBoxImage.Warning // Icon
            );

            // Check the user's response
            if (messageBoxResult == MessageBoxResult.Yes) {
                // User confirmed deletion, proceed with your delete logic here
                _budgetService.DeletePaycheck(p.Id);
                IsEditingPaycheck = false;
                EditingPaycheckClone = null;
                LoadData();
                RefreshPaychecks();
                CalculateProjections();
            }
        }
    }

    #endregion

    #region Account CRUD

    private void AddAccount() {
        EditingAccountClone = new Account {
            Name = "New Account", 
            Type = AccountType.Checking, 
            Balance = 0, 
            BalanceAsOf = DateTime.Today,
            IncludeInTotal = true,
            MortgageDetails = new MortgageDetails(),
            CreditCardDetails = new CreditCardDetails(),
            HexColor = "#FF808080"
        };
        SelectedAccount = null;
        IsEditingAccount = true;
    }

    private void EditAccount() {
        if (SelectedAccount != null) {
            EditingAccountClone = new Account {
                Id = SelectedAccount.Id, 
                Name = SelectedAccount.Name, 
                BankName = SelectedAccount.BankName,
                Balance = SelectedAccount.Balance, 
                BalanceAsOf = SelectedAccount.BalanceAsOf,
                AnnualGrowthRate = SelectedAccount.AnnualGrowthRate, 
                IncludeInTotal = SelectedAccount.IncludeInTotal,
                Type = SelectedAccount.Type, 
                HexColor = SelectedAccount.HexColor
            };
            if (SelectedAccount.MortgageDetails != null) {
                EditingAccountClone.MortgageDetails = new MortgageDetails {
                    Id = SelectedAccount.MortgageDetails.Id,
                    AccountId = SelectedAccount.MortgageDetails.AccountId,
                    InterestRate = SelectedAccount.MortgageDetails.InterestRate,
                    Escrow = SelectedAccount.MortgageDetails.Escrow,
                    MortgageInsurance = SelectedAccount.MortgageDetails.MortgageInsurance,
                    LoanPayment = SelectedAccount.MortgageDetails.LoanPayment,
                    PaymentDate = SelectedAccount.MortgageDetails.PaymentDate
                };
            }
            else {
                EditingAccountClone.MortgageDetails = new MortgageDetails();
            }

            if (SelectedAccount.CreditCardDetails != null) {
                EditingAccountClone.CreditCardDetails = new CreditCardDetails {
                    Id = SelectedAccount.CreditCardDetails.Id,
                    AccountId = SelectedAccount.CreditCardDetails.AccountId,
                    Apr = SelectedAccount.CreditCardDetails.Apr,
                    StatementDay = SelectedAccount.CreditCardDetails.StatementDay,
                    PayPreviousMonthBalanceInFull = SelectedAccount.CreditCardDetails.PayPreviousMonthBalanceInFull
                };
            }
            else {
                EditingAccountClone.CreditCardDetails = new CreditCardDetails();
            }

            IsEditingAccount = true;
        }
    }

    private void SaveAccount() {
        if (EditingAccountClone != null) {
            if (SelectedAccount != null) {
                UpdateAccountFromClone(SelectedAccount, EditingAccountClone);
                _budgetService.UpsertAccount(SelectedAccount);
            }
            else {
                _budgetService.UpsertAccount(EditingAccountClone);
                LoadData();
            }

            IsEditingAccount = false;
            EditingAccountClone = null;
            CalculateProjections();
            
            // Re-trigger Accounts collection change to update chart
            OnPropertyChanged(nameof(Accounts));
        }
    }

    private void UpdateAccountFromClone(Account target, Account clone) {
        target.Name = clone.Name;
        target.BankName = clone.BankName;
        target.Balance = clone.Balance;
        target.BalanceAsOf = clone.BalanceAsOf;
        target.AnnualGrowthRate = clone.AnnualGrowthRate;
        target.IncludeInTotal = clone.IncludeInTotal;
        target.Type = clone.Type;
        target.HexColor = clone.HexColor;

        if (clone.Type == AccountType.Mortgage && clone.MortgageDetails != null) {
            if (target.MortgageDetails == null) target.MortgageDetails = new MortgageDetails();
            target.MortgageDetails.InterestRate = clone.MortgageDetails.InterestRate;
            target.MortgageDetails.Escrow = clone.MortgageDetails.Escrow;
            target.MortgageDetails.MortgageInsurance = clone.MortgageDetails.MortgageInsurance;
            target.MortgageDetails.LoanPayment = clone.MortgageDetails.LoanPayment;
            target.MortgageDetails.PaymentDate = clone.MortgageDetails.PaymentDate;
        }

        if (clone.Type == AccountType.CreditCard && clone.CreditCardDetails != null) {
            if (target.CreditCardDetails == null) target.CreditCardDetails = new CreditCardDetails();
            target.CreditCardDetails.Apr = clone.CreditCardDetails.Apr;
            target.CreditCardDetails.StatementDay = clone.CreditCardDetails.StatementDay;
            target.CreditCardDetails.PayPreviousMonthBalanceInFull = clone.CreditCardDetails.PayPreviousMonthBalanceInFull;
        }
    }

    private void CancelAccount() {
        IsEditingAccount = false;
        EditingAccountClone = null;
    }

    private void DeleteAccount(Account? a) {
        if (a != null) {
            MessageBoxResult messageBoxResult = MessageBox.Show(
                "Are you sure you want to delete this account?", // Message
                "Delete Confirmation", // Title
                MessageBoxButton.YesNo, // Buttons
                MessageBoxImage.Warning // Icon
            );

            // Check the user's response
            if (messageBoxResult == MessageBoxResult.Yes) {
                // User confirmed deletion, proceed with your delete logic here
                _budgetService.DeleteAccount(a.Id);
                IsEditingAccount = false;
                EditingAccountClone = null;
                LoadData();
                CalculateProjections();
            }
        }
    }

    #endregion

    #region Helpers

    public void CalculateProjections() {
        if (_isCalculatingProjections) return;
        _isCalculatingProjections = true;
        try {
            var accounts = _budgetService.GetAllAccounts();
            var paychecks = _budgetService.GetAllPaychecks();
            var bills = _budgetService.GetAllBills();
            var buckets = _budgetService.GetAllBuckets();
            var periodBills = _budgetService.GetAllPeriodBills();
            var periodBuckets = _budgetService.GetAllPeriodBuckets();
            var transactions = _budgetService.GetAllTransactions();

            DateTime start = CurrentPeriodDate == DateTime.MinValue ? DateTime.Today : CurrentPeriodDate;
            DateTime end = start.AddYears(1);

            var results = _projectionEngine.CalculateProjections(
                start, end, accounts, paychecks, bills, buckets, periodBills, periodBuckets, transactions);

            Projections = new ObservableCollection<ProjectionItem>(results);
        }
        finally {
            _isCalculatingProjections = false;
        }
    }

    // public DateTime FindPeriodDateFor(DateTime date) {
    //     if (ShowByMonth) return new DateTime(date.Year, date.Month, 1);
    //
    //     var allPaycheckDates = new List<DateTime>();
    //     foreach (var pay in Paychecks) {
    //         DateTime nextPay = pay.StartDate;
    //         while (nextPay <= date) {
    //             allPaycheckDates.Add(nextPay);
    //             nextPay = pay.Frequency switch {
    //                 Frequency.Weekly => nextPay.AddDays(7),
    //                 Frequency.BiWeekly => nextPay.AddDays(14),
    //                 Frequency.Monthly => nextPay.AddMonths(1),
    //                 _ => nextPay.AddYears(100)
    //             };
    //         }
    //     }
    //
    //     return allPaycheckDates.Where(d => d <= date).OrderByDescending(d => d).FirstOrDefault();
    // }
    
    // public Transaction? GetForPaycheck(int paycheckId, DateTime date) {
    //     return _budgetService.GetAllTransactions()
    //         .FirstOrDefault(a => a.PaycheckId == paycheckId && a.Date.Date == date.Date);
    // }

    public List<PeriodBill> GetProjectedBillsForPeriod(DateTime periodStart) {
        DateTime periodEnd = periodStart.AddDays(14); // Default
        if (ShowByMonth) {
            periodEnd = periodStart.AddMonths(1);
        }
        else {
            var allPaycheckDates = new List<DateTime>();
            foreach (var pay in Paychecks) {
                DateTime nextPay = pay.StartDate;
                while (nextPay < periodStart.AddYears(1)) {
                    if (nextPay > periodStart) {
                        allPaycheckDates.Add(nextPay);
                        break;
                    }

                    nextPay = pay.Frequency switch {
                        Frequency.Weekly => nextPay.AddDays(7),
                        Frequency.BiWeekly => nextPay.AddDays(14),
                        Frequency.Monthly => nextPay.AddMonths(1),
                        _ => nextPay.AddYears(100)
                    };
                }
            }

            if (allPaycheckDates.Any()) periodEnd = allPaycheckDates.Min();
        }

        var result = new List<PeriodBill>();
        foreach (var bill in Bills) {
            DateTime nextDue;
            if (bill.NextDueDate.HasValue) {
                nextDue = bill.NextDueDate.Value;
                while (nextDue < periodStart) {
                    nextDue = bill.Frequency switch {
                        Frequency.Monthly => nextDue.AddMonths(1),
                        Frequency.Yearly => nextDue.AddYears(1),
                        Frequency.Weekly => nextDue.AddDays(7),
                        Frequency.BiWeekly => nextDue.AddDays(14),
                        _ => nextDue.AddYears(100)
                    };
                }
            }
            else {
                nextDue = new DateTime(periodStart.Year, periodStart.Month,
                    Math.Min(bill.DueDay, DateTime.DaysInMonth(periodStart.Year, periodStart.Month)));
                if (nextDue < periodStart) nextDue = nextDue.AddMonths(1);
            }

            while (nextDue < periodEnd) {
                if (nextDue >= periodStart) {
                    result.Add(new PeriodBill {
                        BillId = bill.Id,
                        BillName = bill.Name,
                        PeriodDate = periodStart,
                        DueDate = nextDue,
                        ActualAmount = bill.ExpectedAmount,
                        IsPaid = false
                    });
                }

                nextDue = bill.Frequency switch {
                    Frequency.Monthly => nextDue.AddMonths(1),
                    Frequency.Yearly => nextDue.AddYears(1),
                    Frequency.Weekly => nextDue.AddDays(7),
                    Frequency.BiWeekly => nextDue.AddDays(14),
                    _ => nextDue.AddYears(100)
                };
            }
        }

        return result;
    }

    private void LoadData() {
        _isLoadingData = true;
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
            foreach (var a in accounts) a.PropertyChanged += Item_PropertyChanged;
            Accounts = new ObservableCollection<Account>(accounts);

            var bills = _budgetService.GetAllBills();
            bills = bills.OrderBy(b => b.DueDay).ThenBy(b => b.Name).ToList();
            foreach (var b in bills) b.PropertyChanged += Item_PropertyChanged;
            Bills = new ObservableCollection<Bill>(bills);

            var paychecks = _budgetService.GetAllPaychecks();
            paychecks = paychecks.OrderBy(b => b.Name).ToList();
            foreach (var p in paychecks) p.PropertyChanged += Item_PropertyChanged;
            Paychecks = new ObservableCollection<Paycheck>(paychecks);

            var buckets = _budgetService.GetAllBuckets();
            buckets = buckets.OrderBy(b => b.Name).ToList();
            foreach (var b in buckets) b.PropertyChanged += Item_PropertyChanged;
            Buckets = new ObservableCollection<BudgetBucket>(buckets);
        }
        finally {
            _isLoadingData = false;
        }
    }

    private void LoadPaychecks() {
        var allPaychecks = Paychecks.ToList();
        if (!allPaychecks.Any()) {
            CurrentPeriodDate = DateTime.Today;
            return;
        }

        PeriodPaychecks = new ObservableCollection<Paycheck>(allPaychecks);

        SetCurrentPeriodDate();
    }
    
    private void LoadPeriodData() {
        LoadPeriodBills();
        LoadPeriodBuckets();
        LoadPeriodTransactions();
    }

    private void LoadPeriodBills() {
        var pBills = _budgetService.GetPeriodBills(CurrentPeriodDate).ToList();
        pBills = pBills.OrderBy(pb => pb.DueDate).ToList();
        // Always ensure projected bills for this period are in the database and collection
        var projectedBillsForPeriod = GetProjectedBillsForPeriod(CurrentPeriodDate);
        //bool addedAny = false;
        foreach (var pb in projectedBillsForPeriod) {
            if (!pBills.Any(existing => existing.BillId == pb.BillId && existing.PeriodDate.Date == pb.PeriodDate.Date)) {
                //Bills don't get added just because a period is viewed or is current. They get added if the amount differs
                //_budgetService.UpsertPeriodBill(pb);
                //addedAny = true;
                
            }
            else {
                var periodBill = pBills.SingleOrDefault(existing => existing.BillId == pb.BillId && existing.PeriodDate.Date == pb.PeriodDate.Date);
                UpdatePeriodBillFromClone(pb, periodBill!);
            }
        }

        // if (addedAny) {
        //     pBills = _budgetService.GetPeriodBills(CurrentPeriodDate).ToList();
        //     pBills = pBills.OrderBy(pb => pb.DueDate).ToList();
        // }
        projectedBillsForPeriod = projectedBillsForPeriod.OrderBy(pb => pb.DueDate).ToList();
        //CurrentPeriodBills = new ObservableCollection<PeriodBill>(pBills);
        CurrentPeriodBills = new ObservableCollection<PeriodBill>(projectedBillsForPeriod);
        foreach (var pb in CurrentPeriodBills) pb.PropertyChanged += PeriodBill_PropertyChanged;
    }

    private void LoadPeriodBuckets() {
        var pBuckets = _budgetService.GetPeriodBucketsIncludingMonthly(CurrentPeriodDate).ToList();

        // Same for buckets
        //bool addedAnyBucket = false;
        foreach (var bucket in Buckets.Where(b=>b.PaycheckId == null || (b.PaycheckId == SelectedPeriodPaycheckId && !ShowByMonth))) {
            if (!pBuckets.Any(existing => existing.BucketId == bucket.Id)) {
                var pb = new PeriodBucket {
                    BucketId = bucket.Id,
                    BucketName = bucket.Name,
                    PeriodDate = bucket.PaycheckId == null ? new DateTime(CurrentPeriodDate.Year, CurrentPeriodDate.Month, 1) : CurrentPeriodDate,
                    ActualAmount = bucket.ExpectedAmount,
                    IsPaid = false,
                    FitId = Guid.NewGuid() 
                };
                pBuckets.Add(pb);
                //addedAnyBucket = true;
            }
        }
        
        CurrentPeriodBuckets = new ObservableCollection<PeriodBucket>(pBuckets);
        foreach (var pb in CurrentPeriodBuckets) pb.PropertyChanged += PeriodBucket_PropertyChanged;
    }
        
    private void LoadPeriodTransactions() {
        var transactions = _budgetService.GetTransactions(CurrentPeriodDate).ToList();
        transactions = transactions.OrderBy(pb => pb.Date).ToList();
        CurrentPeriodTransactions = new ObservableCollection<Transaction>(transactions);
        foreach (var t in CurrentPeriodTransactions) t.PropertyChanged += Transaction_PropertyChanged;
    }
            
    private void InitializePeriod() {
        if (ShowByMonth) {
            CurrentPeriodDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            return;
        }

        LoadPaychecks();
    }
    
    private void NavigatePeriod(int direction) {
        if (ShowByMonth) {
            CurrentPeriodDate = CurrentPeriodDate.AddMonths(direction);
            LoadPeriodData();
            return;
        }

        var allPaycheckDates = new List<DateTime>();
        DateTime end = DateTime.Today.AddYears(1);
        foreach (var pay in Paychecks.Where(p => p.Id == SelectedPeriodPaycheckId)) {
            DateTime nextPay = pay.StartDate;
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
        int currentIndex = sortedDates.FindIndex(d => d.Date == CurrentPeriodDate.Date);

        if (currentIndex == -1) {
            if (direction > 0)
                CurrentPeriodDate = sortedDates.FirstOrDefault(d => d > CurrentPeriodDate);
            else
                CurrentPeriodDate = sortedDates.LastOrDefault(d => d < CurrentPeriodDate);
        }
        else {
            int nextIndex = currentIndex + direction;
            if (nextIndex >= 0 && nextIndex < sortedDates.Count)
                CurrentPeriodDate = sortedDates[nextIndex];
        }
        
        LoadPeriodData();
    }
    
    private void RefreshPaychecks() {
        var allPaychecks = Paychecks.ToList();
        if (!allPaychecks.Any()) {
            CurrentPeriodDate = DateTime.Today;
            return;
        }

        PeriodPaychecks = new ObservableCollection<Paycheck>(allPaychecks);
    }

    public void SaveNewTransaction(Transaction transaction) {
        _budgetService.UpsertTransaction(transaction);
    }
    
    private void SetCurrentPeriodDate(int? id = null) {
        var allPaychecks = Paychecks.ToList();
        if (!allPaychecks.Any()) {
            CurrentPeriodDate = DateTime.Today;
            return;
        }

        DateTime latestPayBeforeToday = DateTime.MinValue;
        foreach (var pay in allPaychecks.Where(p => id == null || p.Id == id)) {
            DateTime nextPay = pay.StartDate;
            while (nextPay <= DateTime.Today.AddDays(1)) {
                if (nextPay <= DateTime.Today && nextPay > latestPayBeforeToday)
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
            DateTime nextPay = pay.StartDate;
            bool found = false;
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


        //PeriodPaychecks = new ObservableCollection<Paycheck>(currentPeriodPaychecks);
        if (id == null && currentPeriodPaychecks.Any()) {
            _selectedPeriodPaycheckId = currentPeriodPaychecks.First().Id;
            OnPropertyChanged(nameof(SelectedPeriodPaycheckId));
        }
    }

    private void ShowAbout() {
        var about = new AboutWindow {
            Owner = Application.Current.MainWindow
        };
        about.ShowDialog();
    }
    
    private void ShowAmortization(Account account) {
        var amortization = new AmortizationWindow(account) {
            Owner = Application.Current.MainWindow
        };
        amortization.ShowDialog();
    }
    
    #endregion
}