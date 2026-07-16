using Serilog;
using StayOnTarget.Models;
using StayOnTarget.Services;
using StayOnTarget.Services.Projections;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Newtonsoft.Json;
using StayOnTarget.Views;

namespace StayOnTarget.ViewModels;

public class MainViewModel : ViewModelBase {
    private readonly BudgetService _budgetService;
    private readonly ReconciliationService _reconciliationService;
    private readonly IProjectionEngine _projectionEngine;
    private ObservableCollection<Account> _accounts = new();
    private ObservableCollection<Account> _accountsWithNone = new();
    private ObservableCollection<Bill> _bills = new();
    private ObservableCollection<Bill> _billsWithNone = new();
    private ObservableCollection<Paycheck> _paychecks = new();
    private ObservableCollection<Paycheck> _paychecksWithNone = new();
    private ObservableCollection<ProjectionItem> _projections = new();
    private ObservableCollection<PeriodBill> _currentPeriodBills = new();
    private ObservableCollection<BudgetBucket> _buckets = new();
    private ObservableCollection<BudgetBucket> _bucketsWithNone = new();
    private ObservableCollection<PeriodBucket> _currentPeriodBuckets = new();
    private ObservableCollection<Transaction> _currentPeriodTransactions = new();
    private int _pastDueCount;
    private int _upcomingCount;
    private int _budgetExceededCount;
    private int _envelopeNearingFullCount;
    private ObservableCollection<PeriodBill> _unpaidPastDueBills = new();
    private ObservableCollection<PeriodBucket> _budgetBustedBuckets = new();
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
    private bool _isBillDescriptionExpanded;
    private bool _isBucketDescriptionExpanded;
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
    private ObservableCollection<ToastViewModel> _toasts = new();
    private bool _isEditingPaycheck;
    private Paycheck? _selectedPaycheck;
    private bool _showReconciled = true;
    private string _toggleReconciliationText = "Show Reconciled";
    private DateTime _projectionEndDate = DateTime.Today.AddYears(1);
    private DateTime? _projectionStartDate;
    private int _selectedOuterTabIndex;
    private int _selectedInnerTabIndex;

    #region Properties

    public bool IsCalculatingProjections => _isCalculatingProjections;

    public bool IsBucketDescriptionExpanded {
        get => _isBucketDescriptionExpanded;
        set => SetProperty(ref _isBucketDescriptionExpanded, value);
    }

    public bool IsBillDescriptionExpanded {
        get => _isBillDescriptionExpanded;
        set => SetProperty(ref _isBillDescriptionExpanded, value);
    }

    public static MainViewModel? Instance { get; private set; }

    public MainViewModel(
        BudgetService budgetService, 
        ReconciliationService reconciliationService) {
        
        Instance = this;
        _budgetService = budgetService;
        _reconciliationService = reconciliationService;
        _projectionEngine = new ProjectionEngine();
        LoadData();
        InitializePeriod();
        LoadPeriodData();
        CalculateProjections();
    }

    public ObservableCollection<Bill> Bills {
        get => _bills;
        set => SetProperty(ref _bills, value);
    }

    public ObservableCollection<Paycheck> Paychecks {
        get => _paychecks;
        set => SetProperty(ref _paychecks, value);
    }

    public ObservableCollection<Paycheck> PaychecksWithNone {
        get => _paychecksWithNone;
        set => SetProperty(ref _paychecksWithNone, value);
    }

    public ObservableCollection<Account> Accounts {
        get => _accounts;
        set => SetProperty(ref _accounts, value);
    }

    public AccountType[] AccountTypes => (AccountType[])Enum.GetValues(typeof(AccountType));

    public ObservableCollection<Account> AccountsWithNone {
        get => _accountsWithNone;
        set => SetProperty(ref _accountsWithNone, value);
    }

    public ObservableCollection<Bill> BillsWithNone {
        get => _billsWithNone;
        set => SetProperty(ref _billsWithNone, value);
    }

    public ObservableCollection<BudgetBucket> BucketsWithNone {
        get => _bucketsWithNone;
        set => SetProperty(ref _bucketsWithNone, value);
    }

    public ObservableCollection<ProjectionItem> Projections {
        get => _projections;
        set => SetProperty(ref _projections, value);
    }

    public ObservableCollection<PeriodBill> CurrentPeriodBills {
        get => _currentPeriodBills;
        set {
            if (SetProperty(ref _currentPeriodBills, value)) {
                UpdateWarningMetrics();
            }
        }
    }

    public int PastDueCount {
        get => _pastDueCount;
        set => SetProperty(ref _pastDueCount, value);
    }

    public int UpcomingCount {
        get => _upcomingCount;
        set => SetProperty(ref _upcomingCount, value);
    }

    public ObservableCollection<PeriodBill> UnpaidPastDueBills {
        get => _unpaidPastDueBills;
        set => SetProperty(ref _unpaidPastDueBills, value);
    }

    private void UpdateWarningMetrics() {
        var today = DateTime.Today;
        var upcomingLimit = today.AddDays(2);

        var pastDue = CurrentPeriodBills.Where(pb => !pb.HasActualAmount && pb.DueDate < today && pb.ActualAmount != 0).ToList();
        var upcoming = CurrentPeriodBills.Where(pb => !pb.HasActualAmount && pb.DueDate >= today && pb.DueDate <= upcomingLimit && pb.ActualAmount != 0).ToList();

        PastDueCount = pastDue.Count;
        UpcomingCount = upcoming.Count;
        UnpaidPastDueBills = new ObservableCollection<PeriodBill>(pastDue);
        OnPropertyChanged(nameof(ShowWarningWidget));
    }

    public bool ShowWarningWidget => PastDueCount > 0 || UpcomingCount > 0;
    
    #region Warning Envelope
    
    public int BudgetExceededCount {
        get => _budgetExceededCount;
        set => SetProperty(ref _budgetExceededCount, value);
    }

    public int EnvelopeNearingFullCount {
        get => _envelopeNearingFullCount;
        set => SetProperty(ref _envelopeNearingFullCount, value);
    }

    public ObservableCollection<PeriodBucket> BudgetBustedBuckets {
        get => _budgetBustedBuckets;
        set => SetProperty(ref _budgetBustedBuckets, value);
    }

    private void UpdateBucketWarningMetrics() {
        var exceeded = CurrentPeriodBuckets.Where(pb => pb.HasActualAmount  && pb.ActualAmount != 0 && pb.BudgetExceeded && pb.TransactionAmount != 0).ToList();// && Math.Abs((double)pb.TransactionAmount / (double)pb.ActualAmount) > 1.10).ToList();
        var nearingfull = CurrentPeriodBuckets.Where(pb => pb.HasActualAmount && pb.ActualAmount != 0 && !pb.BudgetExceeded && pb.TransactionAmount != 0 && Math.Abs((double)pb.TransactionAmount / (double)pb.ActualAmount) > .80 ).ToList();

        BudgetExceededCount = exceeded.Count;
        EnvelopeNearingFullCount = nearingfull.Count;
        if (nearingfull.Count > 0) {
            var myList = exceeded;
            myList.AddRange(nearingfull);
            BudgetBustedBuckets = new ObservableCollection<PeriodBucket>(myList);
        }
        else {
            BudgetBustedBuckets = new ObservableCollection<PeriodBucket>(exceeded);
        }
        
        OnPropertyChanged(nameof(ShowEnvelopeWarningWidget));
    }

    public bool ShowEnvelopeWarningWidget => BudgetExceededCount > 0 || EnvelopeNearingFullCount > 0;
    
    #endregion

    public ObservableCollection<BudgetBucket> Buckets {
        get => _buckets;
        set => SetProperty(ref _buckets, value);
    }

    public ObservableCollection<PeriodBucket> CurrentPeriodBuckets {
        get => _currentPeriodBuckets;
        set {
            if (SetProperty(ref _currentPeriodBuckets, value)) {
                UpdateBucketWarningMetrics();
            }
        }
    }

    public ObservableCollection<Transaction> CurrentPeriodTransactions {
        get => _currentPeriodTransactions;
        set => SetProperty(ref _currentPeriodTransactions, value);
    }

    public string ToggleReconciliationText {
        get => _toggleReconciliationText;
        set => SetProperty(ref _toggleReconciliationText, value);
    }

    public bool ShowReconciled {
        get => _showReconciled;
        set {
            if (SetProperty(ref _showReconciled, value)) {
                CalculateProjections();
            }
        }
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

    public ObservableCollection<ToastViewModel> Toasts {
        get => _toasts;
        set => SetProperty(ref _toasts, value);
    }

    public string PeriodDisplay {
        get {
            if (ShowByMonth) return _currentPeriodDate.ToString("MMMM yyyy");
            return $"Period: {_currentPeriodDate:d}";
        }
    }

    public DateTime ProjectionEndDate {
        get => _projectionEndDate;
        set {
            if (SetProperty(ref _projectionEndDate, value)) {
                CalculateProjections();
            }
        }
    }

    public DateTime? ProjectionStartDate {
        get => _projectionStartDate;
        set {
            if (SetProperty(ref _projectionStartDate, value)) {
                CalculateProjections();
            }
        }
    }

    public int SelectedOuterTabIndex {
        get => _selectedOuterTabIndex;
        set => SetProperty(ref _selectedOuterTabIndex, value);
    }

    public int SelectedInnerTabIndex {
        get => _selectedInnerTabIndex;
        set => SetProperty(ref _selectedInnerTabIndex, value);
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
            if (_selectedBill != value && IsEditingBill && EditingBillClone != null &&
                EditingBillClone?.Id != value?.Id) {
                CancelBill();
            }

            if (SetProperty(ref _selectedBill, value)) {
                OnPropertyChanged(nameof(CanEditBill));
            }
        }
    }

    public PeriodBill? SelectedPeriodBill {
        get => _selectedPeriodBill;
        set {
            if (_selectedPeriodBill != value && IsEditingPeriodBill && EditingPeriodBillClone != null &&
                EditingPeriodBillClone?.Id != value?.Id) {
                CancelPeriodBill();
            }

            if (SetProperty(ref _selectedPeriodBill, value)) {
                OnPropertyChanged(nameof(CanEditPeriodBill));
            }
        }
    }

    public BudgetBucket? SelectedBucket {
        get => _selectedBucket;
        set {
            if (_selectedBucket != value && IsEditingBucket && EditingBucketClone != null &&
                EditingBucketClone?.Id != value?.Id) {
                CancelBucket();
            }

            if (SetProperty(ref _selectedBucket, value)) {
                OnPropertyChanged(nameof(CanEditBucket));
            }
        }
    }

    public PeriodBucket? SelectedPeriodBucket {
        get => _selectedPeriodBucket;
        set {
            if (_selectedPeriodBucket != value && IsEditingPeriodBucket && EditingPeriodBucketClone != null &&
                EditingPeriodBucketClone?.Id != value?.Id) {
                CancelPeriodBucket();
            }

            if (SetProperty(ref _selectedPeriodBucket, value)) {
                OnPropertyChanged(nameof(CanEditPeriodBucket));
            }
        }
    }


    public Account? SelectedAccount {
        get => _selectedAccount;
        set {
            if (_selectedAccount != value && IsEditingAccount && EditingAccountClone != null &&
                EditingAccountClone?.Id != value?.Id) {
                CancelAccount();
            }

            if (SetProperty(ref _selectedAccount, value)) {
                OnPropertyChanged(nameof(CanEditAccount));
            }
        }
    }

    public Transaction? SelectedTransaction {
        get => _selectedTransaction;
        set {
            if (_selectedTransaction != value && IsEditingTransaction && EditingTransactionClone != null &&
                EditingTransactionClone?.Id != value?.Id) {
                CancelTransaction();
            }

            if (SetProperty(ref _selectedTransaction, value)) {
                OnPropertyChanged(nameof(CanEditTransaction));
            }
        }
    }

    public Paycheck? SelectedPaycheck {
        get => _selectedPaycheck;
        set {
            if (_selectedPaycheck != value && IsEditingPaycheck && EditingPaycheckClone != null &&
                EditingPaycheckClone?.Id != value?.Id) {
                CancelPaycheck();
            }

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
    public bool CanEditBill => SelectedBill != null;

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

    public bool CanEditPaycheck => SelectedPaycheck != null;

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

    public bool CanEditBucket => SelectedBucket != null;

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

    public bool CanEditPeriodBill => SelectedPeriodBill != null;

    public bool IsNotEditingPeriodBucket => !IsEditingPeriodBucket;

    public bool CanEditPeriodBucket => SelectedPeriodBucket != null;

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
    public bool CanEditAccount => SelectedAccount != null;

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
    public bool CanEditTransaction => SelectedTransaction != null;

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

    #endregion

    #region Commands

    public ICommand AddBillCommand => new RelayCommand(_ => AddBill(), _ => IsNotEditingBill);
    public ICommand EditBillCommand => new RelayCommand(_ => EditBill(), _ => CanEditBill);
    public ICommand SaveBillCommand => new RelayCommand(_ => SaveBill(), _ => IsEditingBill);

    public ICommand CancelBillCommand => new RelayCommand(_ => CancelBill(), _ => IsEditingBill);

    //public ICommand DeleteBillCommand => new RelayCommand(b => DeleteBill(b as Bill));
    public ICommand DeleteBillCommand => new RelayCommand(_ => DeleteBill(), _ => IsEditingBill);

    public ICommand EditPeriodBillCommand => new RelayCommand(_ => EditPeriodBill(), _ => CanEditPeriodBill);
    public ICommand SavePeriodBillCommand => new RelayCommand(_ => SavePeriodBill(), _ => IsEditingPeriodBill);

    public ICommand CancelPeriodBillCommand => new RelayCommand(_ => CancelPeriodBill(), _ => IsEditingPeriodBill);

    //public ICommand DeletePeriodBillCommand => new RelayCommand(pb => DeletePeriodBill(pb as PeriodBill));
    public ICommand DeletePeriodBillCommand => new RelayCommand(_ => DeletePeriodBill(), _ => IsEditingPeriodBill);

    public ICommand AddBucketCommand => new RelayCommand(_ => AddBucket(), _ => IsNotEditingBucket);
    public ICommand EditBucketCommand => new RelayCommand(_ => EditBucket(), _ => CanEditBucket);
    public ICommand SaveBucketCommand => new RelayCommand(_ => SaveBucket(), _ => IsEditingBucket);

    public ICommand CancelBucketCommand => new RelayCommand(_ => CancelBucket(), _ => IsEditingBucket);

    //public ICommand DeleteBucketCommand => new RelayCommand(b => DeleteBucket(b as BudgetBucket));
    public ICommand DeleteBucketCommand => new RelayCommand(_ => DeleteBucket());

    public ICommand EditPeriodBucketCommand => new RelayCommand(_ => EditPeriodBucket(), _ => CanEditPeriodBucket);
    public ICommand SavePeriodBucketCommand => new RelayCommand(_ => SavePeriodBucket(), _ => IsEditingPeriodBucket);

    public ICommand CancelPeriodBucketCommand =>
        new RelayCommand(_ => CancelPeriodBucket(), _ => IsEditingPeriodBucket);

    //public ICommand DeletePeriodBucketCommand => new RelayCommand(pb => DeletePeriodBucket(pb as PeriodBucket));
    public ICommand DeletePeriodBucketCommand =>
        new RelayCommand(_ => DeletePeriodBucket(), _ => IsEditingPeriodBucket);

    public ICommand AddTransactionCommand => new RelayCommand(_ => AddTransaction(), _ => IsNotEditingTransaction);
    public ICommand EditTransactionCommand => new RelayCommand(_ => EditTransaction(), _ => CanEditTransaction);
    public ICommand SaveTransactionCommand => new RelayCommand(_ => SaveTransaction(), _ => IsEditingTransaction);

    public ICommand CancelTransactionCommand => new RelayCommand(_ => CancelTransaction(), _ => IsEditingTransaction);

    // public ICommand DeleteTransactionCommand =>
    //     new RelayCommand(t => DeleteTransaction(t as Transaction));
    public ICommand DeleteTransactionCommand => new RelayCommand(_ => DeleteTransaction(), _ => IsEditingTransaction);

    public ICommand AddPaycheckCommand => new RelayCommand(_ => AddPaycheck());
    public ICommand EditPaycheckCommand => new RelayCommand(_ => EditPaycheck(), _ => CanEditPaycheck);
    public ICommand SavePaycheckCommand => new RelayCommand(_ => SavePaycheck(), _ => IsEditingPaycheck);

    public ICommand CancelPaycheckCommand => new RelayCommand(_ => CancelPaycheck(), _ => IsEditingPaycheck);

    //public ICommand DeletePaycheckCommand => new RelayCommand(p => DeletePaycheck(p as Paycheck));
    public ICommand DeletePaycheckCommand => new RelayCommand(_ => DeletePaycheck(), _ => IsEditingPaycheck);

    public ICommand AddAccountCommand => new RelayCommand(_ => AddAccount(), _ => IsNotEditingAccount);
    public ICommand EditAccountCommand => new RelayCommand(_ => EditAccount(), _ => CanEditAccount);
    public ICommand ReconcileAccountCommand => new RelayCommand(_ => ReconcileAccount(), _ => IsEditingAccount);
    public ICommand ImportAccountCommand => new RelayCommand(_ => ImportAccount(), _ => IsEditingAccount);
    
    public ICommand SetAccountAprRatesCommand => new RelayCommand(_ => SetAccountAprRates(), _ => IsEditingAccount);
    public ICommand SaveAccountCommand => new RelayCommand(_ => SaveAccount(), _ => IsEditingAccount);

    public ICommand CancelAccountCommand => new RelayCommand(_ => CancelAccount(), _ => IsEditingAccount);

    //public ICommand DeleteAccountCommand =>
    //     new RelayCommand(a => DeleteAccount(a as Account), _ => IsNotEditingAccount);
    public ICommand DeleteAccountCommand => new RelayCommand(_ => DeleteAccount(), _ => IsEditingAccount);

    public ICommand NextPeriodCommand => new RelayCommand(_ => NavigatePeriod(1));
    public ICommand PrevPeriodCommand => new RelayCommand(_ => NavigatePeriod(-1));

    public ICommand ShowAmortizationCommand =>
        new RelayCommand(a => ShowAmortization(a as Account ?? throw new InvalidOperationException()));

    public ICommand ShowAboutCommand => new RelayCommand(_ => ShowAbout());
    public ICommand ExitCommand => new RelayCommand(_ => Exit());
    public ICommand BackupCommand => new RelayCommand(_ => Backup());
    public ICommand SetOneYearCommand => new RelayCommand(_ => SetProjectionEndDate(1));

    public ICommand SetFiveYearCommand => new RelayCommand(_ => SetProjectionEndDate(5));

    public ICommand SetTenYearCommand => new RelayCommand(_ => SetProjectionEndDate(10));

    public ICommand SetThirtyYearCommand => new RelayCommand(_ => SetProjectionEndDate(30));

    public ICommand MapsToBillCommand => new RelayCommand(p => {
        if (p is PeriodBill pb) {
            SelectedPeriodBill = pb;
            SelectedOuterTabIndex = 0;
            SelectedInnerTabIndex = 1;
        }
    });

    public ICommand MapsToBucketCommand => new RelayCommand(p => {
        if (p is PeriodBucket pb) {
            SelectedPeriodBucket = pb;
            SelectedOuterTabIndex = 0;
            SelectedInnerTabIndex = 2;
        }
    });
    
    
        
    public ICommand ToggleBucketDescriptionCommand =>
        new RelayCommand(_ => IsBucketDescriptionExpanded = !IsBucketDescriptionExpanded);

    public ICommand ToggleBillDescriptionCommand =>
        new RelayCommand(_ => IsBillDescriptionExpanded = !IsBillDescriptionExpanded);

    private void SetProjectionEndDate(int years) {
        ProjectionEndDate = DateTime.Now.AddYears(years);
    }

    #endregion

    private bool _isLoadingData;

    #region Events

    private void Item_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (_isLoadingData) return;
        try {
            switch (sender) {
                case Bill b:
                    _budgetService.UpsertBill(b);
                    break;
                case Paycheck p: {
                    _budgetService.UpsertPaycheck(p);
                    RefreshPaychecks();
                    if (p.Id == _selectedPeriodPaycheckId) {
                        OnPropertyChanged(nameof(SelectedPeriodPaycheckId));
                        LoadPeriodData();
                    }

                    break;
                }
                case Account a:
                    _budgetService.UpsertAccount(a);
                    break;
                case BudgetBucket bb:
                    _budgetService.UpsertBucket(bb);
                    break;
            }

            CalculateProjections();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in Item_PropertyChanged for {SenderType}.", sender?.GetType().Name);
        }
    }

    private void PeriodBill_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (sender is not PeriodBill pb) return;
        try {
            if (e.PropertyName == nameof(PeriodBill.TransactionAmount)) {
                UpdateWarningMetrics();
                return;
            }

            if (e.PropertyName == nameof(PeriodBill.HasActualAmount)) {
                UpdateWarningMetrics();
                return;
            }

            if (e.PropertyName == nameof(PeriodBill.BudgetExceeded)) return;
            _budgetService.UpsertPeriodBill(pb);
            LoadPeriodData();
            CalculateProjections();
            
            if (e.PropertyName == nameof(PeriodBill.ActualAmount)) return; {
                UpdateWarningMetrics();
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in PeriodBill_PropertyChanged.");
        }
    }

    private void PeriodBucket_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (sender is not PeriodBucket pb) return;
        try {
            if (e.PropertyName == nameof(PeriodBill.TransactionAmount)) {
                UpdateBucketWarningMetrics();
                return;
            }

            if (e.PropertyName == nameof(PeriodBill.HasActualAmount)) {
                UpdateBucketWarningMetrics();
                return;
            }
            

            if (e.PropertyName == nameof(PeriodBucket.BudgetExceeded)) return;
            _budgetService.UpsertPeriodBucket(pb);
            LoadPeriodData();
            CalculateProjections();
            
            if (e.PropertyName == nameof(PeriodBill.ActualAmount)) return; {
                UpdateBucketWarningMetrics();
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in PeriodBucket_PropertyChanged.");
        }
    }

    private void Transaction_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (sender is not Transaction t) return;
        try {
            Task.Run(async () => {
                try {
                    await _budgetService.UpsertTransactionAsync(t);
                }
                catch (Exception ex) {
                    Log.Error(ex, "Error upserting transaction in PropertyChanged.");
                }
            }); // Run async work
            LoadPeriodData();
            CalculateProjections();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in Transaction_PropertyChanged.");
        }
    }

    #endregion

    #region Bill CRUD

    private void AddBill() {
        try {
            EditingBillClone = new Bill { Name = "New Bill", ExpectedAmount = 0, DueDay = 1, IsActive = true };
            SelectedBill = null;
            IsEditingBill = true;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error initializing new bill.");
        }
    }

    private void EditBill() {
        try {
            CancelBill();
            if (SelectedBill == null) return;
            EditingBillClone = new Bill {
                Id = SelectedBill.Id, Name = SelectedBill.Name, ExpectedAmount = SelectedBill.ExpectedAmount,
                Frequency = SelectedBill.Frequency, DueDay = SelectedBill.DueDay, AccountId = SelectedBill.AccountId,
                ToAccountId = SelectedBill.ToAccountId, NextDueDate = SelectedBill.NextDueDate,
                Category = SelectedBill.Category, IsActive = SelectedBill.IsActive
            };
            IsEditingBill = true;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error entering edit mode for bill.");
        }
    }

    private void SaveBill() {
        if (EditingBillClone == null) return;

        try {
            if (EditingBillClone.AccountId == 0) EditingBillClone.AccountId = null;
            if (EditingBillClone.ToAccountId == 0) EditingBillClone.ToAccountId = null;

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
        catch (Exception ex) {
            Log.Error(ex, "Error saving bill.");
            MessageBox.Show("Failed to save bill. See log for details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateBillFromClone(Bill target, Bill clone) {
        target.Name = clone.Name;
        target.ExpectedAmount = clone.ExpectedAmount;
        target.Frequency = clone.Frequency;
        target.DueDay = clone.DueDay;
        target.AccountId = clone.AccountId == 0 ? null : clone.AccountId;
        target.ToAccountId = clone.ToAccountId == 0 ? null : clone.ToAccountId;
        target.NextDueDate = clone.NextDueDate;
        target.Category = clone.Category;
        target.IsActive = clone.IsActive;
    }

    private void CancelBill() {
        try {
            IsEditingBill = false;
            EditingBillClone = null;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error cancelling bill edit.");
        }
    }

    // private void DeletePeriodBill(PeriodBill? pb) {
    //     if (pb != null) {
    //         MessageBoxResult messageBoxResult = MessageBox.Show(
    //             "Are you sure you want to delete this period's bill?", // Message
    //             "Delete Confirmation", // Title
    //             MessageBoxButton.YesNo, // Buttons
    //             MessageBoxImage.Warning // Icon
    //         );
    //
    //         // Check the user's response
    //         if (messageBoxResult == MessageBoxResult.Yes) {
    //             // User confirmed deletion, proceed with your delete logic here
    //             _budgetService.DeletePeriodBill(pb.Id);
    //             IsEditingPeriodBill = false;
    //             EditingPeriodBillClone = null;
    //             LoadPeriodData();
    //             CalculateProjections();
    //         }
    //     }
    // }

    private void DeletePeriodBill() {
        if (EditingPeriodBillClone == null) return;
        var messageBoxResult = MessageBox.Show(
            "Are you sure you want to delete this period's bill?", // Message
            "Delete Confirmation", // Title
            MessageBoxButton.YesNo, // Buttons
            MessageBoxImage.Warning // Icon
        );

        // Check the user's response
        if (messageBoxResult == MessageBoxResult.Yes) {
            try {
                // User confirmed deletion, proceed with your delete logic here
                _budgetService.DeletePeriodBill(EditingPeriodBillClone.Id);
                IsEditingPeriodBill = false;
                EditingPeriodBillClone = null;
                LoadPeriodData();
                CalculateProjections();
            }
            catch (Exception ex) {
                Log.Error(ex, "Error deleting period bill.");
                MessageBox.Show("Failed to delete period bill. See log for details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }


    private void EditPeriodBill() {
        try {
            CancelPeriodBill();
            //until a user customizes a bucket, it uses the budgeted bucket and the period bucket is a copy of that.
            if (SelectedPeriodBill == null) return;
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
        catch (Exception ex) {
            Log.Error(ex, "Error entering edit mode for period bill.");
        }
    }

    private void SavePeriodBill() {
        //until a user customizes a bucket, it uses the budgeted bucket and the period bucket is a copy of that.
        if (EditingPeriodBillClone == null) return;
        try {
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
        catch (Exception ex) {
            Log.Error(ex, "Error saving period bill.");
            MessageBox.Show("Failed to save period bill. See log for details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }


    private void CancelPeriodBill() {
        try {
            IsEditingPeriodBill = false;
            EditingPeriodBillClone = null;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error cancelling period bill edit.");
        }
    }


    private void UpdatePeriodBillFromClone(PeriodBill target, PeriodBill clone) {
        target.Id = clone.Id;
        target.ActualAmount = clone.ActualAmount;
        target.DueDate = clone.DueDate;
        target.IsPaid = clone.IsPaid;
    }

    // private void DeleteBill(Bill? b) {
    //     if (b != null) {
    //         MessageBoxResult messageBoxResult = MessageBox.Show(
    //             "Are you sure you want to delete this bill?", // Message
    //             "Delete Confirmation", // Title
    //             MessageBoxButton.YesNo, // Buttons
    //             MessageBoxImage.Warning // Icon
    //         );
    //
    //         // Check the user's response
    //         if (messageBoxResult == MessageBoxResult.Yes) {
    //             // User confirmed deletion, proceed with your delete logic here
    //             _budgetService.DeleteBill(b.Id);
    //             IsEditingBill = false;
    //             EditingBillClone = null;
    //             LoadData();
    //             CalculateProjections();
    //         }
    //     }
    // }

    private void DeleteBill() {
        if (EditingBillClone == null) return;
        var messageBoxResult = MessageBox.Show(
            "Are you sure you want to delete this bill?", // Message
            "Delete Confirmation", // Title
            MessageBoxButton.YesNo, // Buttons
            MessageBoxImage.Warning // Icon
        );

        // Check the user's response
        if (messageBoxResult == MessageBoxResult.Yes) {
            try {
                // User confirmed deletion, proceed with your delete logic here
                _budgetService.DeleteBill(EditingBillClone.Id);
                IsEditingBill = false;
                EditingBillClone = null;
                LoadData();
                CalculateProjections();
            }
            catch (Exception ex) {
                Log.Error(ex, "Error deleting bill.");
                MessageBox.Show("Failed to delete bill. See log for details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    #endregion

    #region Bucket CRUD

    private void AddBucket() {
        try {
            EditingBucketClone = new BudgetBucket { Name = "New Bucket", ExpectedAmount = 0 };
            SelectedBucket = null;
            IsEditingBucket = true;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error initializing new bucket.");
        }
    }

    private void EditBucket() {
        try {
            CancelBucket();
            if (SelectedBucket == null) return;
            EditingBucketClone = new BudgetBucket {
                Id = SelectedBucket.Id,
                Name = SelectedBucket.Name,
                ExpectedAmount = SelectedBucket.ExpectedAmount,
                AccountId = SelectedBucket.AccountId,
                PaycheckId = SelectedBucket.PaycheckId
            };
            IsEditingBucket = true;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error entering edit mode for bucket.");
        }
    }

    private void SaveBucket() {
        if (EditingBucketClone == null) return;

        try {
            if (EditingBucketClone.AccountId == 0) EditingBucketClone.AccountId = null;
            if (EditingBucketClone.PaycheckId == 0) EditingBucketClone.PaycheckId = null;

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
        catch (Exception ex) {
            Log.Error(ex, "Error saving bucket.");
            MessageBox.Show("Failed to save bucket. See log for details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateBucketFromClone(BudgetBucket target, BudgetBucket clone) {
        target.Name = clone.Name;
        target.ExpectedAmount = clone.ExpectedAmount;
        target.AccountId = clone.AccountId == 0 ? null : clone.AccountId;
        target.PaycheckId = clone.PaycheckId == 0 ? null : clone.PaycheckId;
    }

    private void CancelBucket() {
        try {
            IsEditingBucket = false;
            EditingBucketClone = null;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error cancelling bucket edit.");
        }
    }

    // private void DeleteBucket(BudgetBucket? b) {
    //     if (b != null) {
    //         MessageBoxResult messageBoxResult = MessageBox.Show(
    //             "Are you sure you want to delete this bucket?", // Message
    //             "Delete Confirmation", // Title
    //             MessageBoxButton.YesNo, // Buttons
    //             MessageBoxImage.Warning // Icon
    //         );
    //
    //         // Check the user's response
    //         if (messageBoxResult == MessageBoxResult.Yes) {
    //             // User confirmed deletion, proceed with your delete logic here
    //             _budgetService.DeleteBucket(b.Id);
    //             IsEditingBucket = false;
    //             EditingBucketClone = null;
    //             LoadData();
    //             CalculateProjections();
    //         }
    //     }
    // }

    private void DeleteBucket() {
        if (EditingBucketClone == null) return;
        var messageBoxResult = MessageBox.Show(
            "Are you sure you want to delete this bucket?", // Message
            "Delete Confirmation", // Title
            MessageBoxButton.YesNo, // Buttons
            MessageBoxImage.Warning // Icon
        );

        // Check the user's response
        if (messageBoxResult == MessageBoxResult.Yes) {
            try {
                // User confirmed deletion, proceed with your delete logic here
                _budgetService.DeleteBucket(EditingBucketClone.Id);
                IsEditingBucket = false;
                EditingBucketClone = null;
                LoadData();
                CalculateProjections();
            }
            catch (Exception ex) {
                Log.Error(ex, "Error deleting bucket.");
                MessageBox.Show("Failed to delete bucket. See log for details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void EditPeriodBucket() {
        try {
            CancelPeriodBucket();
            //until a user customizes a bucket, it uses the budgeted bucket and the period bucket is a copy of that.
            if (SelectedPeriodBucket == null) return;
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
        catch (Exception ex) {
            Log.Error(ex, "Error entering edit mode for period bucket.");
        }
    }

    private void SavePeriodBucket() {
        //until a user customizes a bucket, it uses the budgeted bucket and the period bucket is a copy of that.
        if (EditingPeriodBucketClone == null) return;
        try {
            if (SelectedPeriodBucket != null) {
                UpdatePeriodBucketFromClone(SelectedPeriodBucket, EditingPeriodBucketClone);
            }
            else {
                _budgetService.UpsertPeriodBucket(EditingPeriodBucketClone);
            }

            LoadPeriodData();
            IsEditingPeriodBucket = false;
            EditingPeriodBucketClone = null;
            CalculateProjections();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error saving period bucket.");
            MessageBox.Show("Failed to save period bucket. See log for details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
        try {
            IsEditingPeriodBucket = false;
            EditingPeriodBucketClone = null;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error cancelling period bucket edit.");
        }
    }

    // private void DeletePeriodBucket(PeriodBucket? pb) {
    //     if (pb != null) {
    //         MessageBoxResult messageBoxResult = MessageBox.Show(
    //             "Are you sure you want to delete this period's bucket?\r\n\r\nIt will use the budgetted amount for the bucket instead. Save a $0 amount if you do not want to budget for this bucket for this period.", // Message
    //             "Delete Confirmation", // Title
    //             MessageBoxButton.YesNo, // Buttons
    //             MessageBoxImage.Warning // Icon
    //         );
    //
    //         // Check the user's response
    //         if (messageBoxResult == MessageBoxResult.Yes) {
    //             // User confirmed deletion, proceed with your delete logic here
    //             _budgetService.DeletePeriodBucket(pb.Id);
    //             IsEditingPeriodBucket = false;
    //             EditingPeriodBucketClone = null;
    //             LoadPeriodData();
    //             CalculateProjections();
    //         }
    //     }
    // }

    private void DeletePeriodBucket() {
        if (EditingPeriodBucketClone == null) return;
        var messageBoxResult = MessageBox.Show(
            "Are you sure you want to delete this period's bucket?\r\n\r\nIt will use the budgetted amount for the bucket instead. Save a $0 amount if you do not want to budget for this bucket for this period.", // Message
            "Delete Confirmation", // Title
            MessageBoxButton.YesNo, // Buttons
            MessageBoxImage.Warning // Icon
        );

        // Check the user's response
        if (messageBoxResult == MessageBoxResult.Yes) {
            try {
                // User confirmed deletion, proceed with your delete logic here
                _budgetService.DeletePeriodBucket(EditingPeriodBucketClone.Id);
                IsEditingPeriodBucket = false;
                EditingPeriodBucketClone = null;
                LoadPeriodData();
                CalculateProjections();
            }
            catch (Exception ex) {
                Log.Error(ex, "Error deleting period bucket.");
                MessageBox.Show("Failed to delete period bucket. See log for details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    #endregion

    #region Transaction CRUD

    private void AddTransaction() {
        try {
            var guid = Guid.NewGuid().ToString();
            EditingTransactionClone = new Transaction {
                Description = "", Memo = "", Amount = 0, TransactionDate = DateTime.Today, PeriodDate = CurrentPeriodDate,
                FitId = guid
            };
            SelectedTransaction = null;
            IsEditingTransaction = true;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error initializing new transaction.");
        }
    }

    private void EditTransaction() {
        try {
            CancelTransaction();
            if (SelectedTransaction == null) return;
            EditingTransactionClone = new Transaction {
                Id = SelectedTransaction.Id,
                Description = SelectedTransaction.Description,
                Memo = SelectedTransaction.Memo,
                Amount = SelectedTransaction.Amount,
                TransactionDate = SelectedTransaction.TransactionDate,
                AccountId = SelectedTransaction.AccountId,
                ToAccountId = SelectedTransaction.ToAccountId,
                BucketId = SelectedTransaction.BucketId,
                PeriodDate = SelectedTransaction.PeriodDate,
                IsPrincipalOnly = SelectedTransaction.IsPrincipalOnly,
                IsRebalance = SelectedTransaction.IsRebalance,
                PaycheckId = SelectedTransaction.PaycheckId,
                BillId = SelectedTransaction.BillId,
                BillName = SelectedTransaction.BillName,
                PaycheckOccurrenceDate = SelectedTransaction.PaycheckOccurrenceDate,
                FitId = SelectedTransaction.FitId,
                TransactionId = SelectedTransaction.TransactionId,
                FromAccountReconciledId = SelectedTransaction.FromAccountReconciledId,
                ToAccountReconciledId = SelectedTransaction.ToAccountReconciledId
            };
            IsEditingTransaction = true;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error entering edit mode for transaction.");
        }
    }

    private async Task SaveTransaction() {
        if (EditingTransactionClone == null) return;

        try {
            if (EditingTransactionClone.AccountId == 0) EditingTransactionClone.AccountId = null;
            if (EditingTransactionClone.ToAccountId == 0) EditingTransactionClone.ToAccountId = null;
            if (EditingTransactionClone.BillId == 0) EditingTransactionClone.BillId = null;
            if (EditingTransactionClone.BucketId == 0) EditingTransactionClone.BucketId = null;

            if (SelectedTransaction != null) {
                UpdateTransactionFromClone(SelectedTransaction, EditingTransactionClone);
                await _budgetService.UpsertTransactionAsync(SelectedTransaction);
            }
            else {
                await _budgetService.UpsertTransactionAsync(EditingTransactionClone);
            }

            IsEditingTransaction = false;
            EditingTransactionClone = null;

            LoadPeriodData();
            CalculateProjections();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error saving transaction.");
            MessageBox.Show("Failed to save transaction. See log for details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateTransactionFromClone(Transaction target, Transaction clone) {
        target.TransactionId = clone.TransactionId; // Keep the Guid chain bound
        target.FitId = clone.FitId; // Keep the Guid chain bound
        target.Description = clone.Description;
        target.Memo = clone.Memo;
        target.Amount = clone.Amount;
        target.TransactionDate = clone.TransactionDate;
        target.AccountId = clone.AccountId == 0 ? null : clone.AccountId;
        target.ToAccountId = clone.ToAccountId == 0 ? null : clone.ToAccountId;
        target.BucketId = clone.BucketId == 0 ? null : clone.BucketId;
        target.BillId = clone.BillId == 0 ? null : clone.BillId;
        target.PeriodDate = clone.PeriodDate;
        target.IsPrincipalOnly = clone.IsPrincipalOnly;
        target.IsRebalance = clone.IsRebalance;
        target.PaycheckId = clone.PaycheckId;
        target.PaycheckOccurrenceDate = clone.PaycheckOccurrenceDate;
        target.FromAccountReconciledId = clone.FromAccountReconciledId;
        target.ToAccountReconciledId = clone.ToAccountReconciledId;
    }

    private void CancelTransaction() {
        try {
            if (SelectedTransaction != null && SelectedTransaction.TransactionId == Guid.Empty) {
                CurrentPeriodTransactions.Remove(SelectedTransaction);
            }

            IsEditingTransaction = false;
            EditingTransactionClone = null;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error cancelling transaction edit.");
        }
    }

    // private void DeleteTransaction(Transaction? t) {
    //     if (t != null) {
    //         MessageBoxResult messageBoxResult = MessageBox.Show(
    //             "Are you sure you want to delete this transaction?", // Message
    //             "Delete Confirmation", // Title
    //             MessageBoxButton.YesNo, // Buttons
    //             MessageBoxImage.Warning // Icon
    //         );
    //
    //         // Check the user's response
    //         if (messageBoxResult == MessageBoxResult.Yes) {
    //             // User confirmed deletion, proceed with your delete logic here
    //             _budgetService.DeleteTransaction(t.Id);
    //             IsEditingTransaction = false;
    //             EditingTransactionClone = null;
    //             LoadPeriodData();
    //             CalculateProjections();
    //         }
    //     }
    // }
    private void DeleteTransaction() {
        if (EditingTransactionClone == null) return;
        var messageBoxResult = MessageBox.Show(
            "Are you sure you want to delete this transaction?", // Message
            "Delete Confirmation", // Title
            MessageBoxButton.YesNo, // Buttons
            MessageBoxImage.Warning // Icon
        );

        // Check the user's response
        if (messageBoxResult == MessageBoxResult.Yes) {
            try {
                // User confirmed deletion, proceed with your delete logic here
                _budgetService.DeleteTransaction(EditingTransactionClone.TransactionId);
                IsEditingTransaction = false;
                EditingTransactionClone = null;
                LoadPeriodData();
                CalculateProjections();
            }
            catch (Exception ex) {
                Log.Error(ex, "Error deleting transaction.");
                MessageBox.Show("Failed to delete transaction. See log for details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    #endregion

    #region Paycheck CRUD

    private void AddPaycheck() {
        try {
            EditingPaycheckClone = new Paycheck
                { Name = "New Paycheck", ExpectedAmount = 0, StartDate = DateTime.Today, Frequency = Frequency.BiWeekly };
            SelectedPaycheck = null;
            IsEditingPaycheck = true;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error initializing new paycheck.");
        }
    }

    private void EditPaycheck() {
        try {
            CancelPaycheck();
            if (SelectedPaycheck == null) return;
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
        catch (Exception ex) {
            Log.Error(ex, "Error entering edit mode for paycheck.");
        }
    }

    private void SavePaycheck() {
        if (EditingPaycheckClone == null) return;
        try {
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
        catch (Exception ex) {
            Log.Error(ex, "Error saving paycheck.");
            MessageBox.Show("Failed to save paycheck. See log for details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
        try {
            IsEditingPaycheck = false;
            EditingPaycheckClone = null;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error cancelling paycheck edit.");
        }
    }

    // private void DeletePaycheck(Paycheck? p) {
    //     if (p != null) {
    //         MessageBoxResult messageBoxResult = MessageBox.Show(
    //             "Are you sure you want to delete this paycheck?", // Message
    //             "Delete Confirmation", // Title
    //             MessageBoxButton.YesNo, // Buttons
    //             MessageBoxImage.Warning // Icon
    //         );
    //
    //         // Check the user's response
    //         if (messageBoxResult == MessageBoxResult.Yes) {
    //             // User confirmed deletion, proceed with your delete logic here
    //             _budgetService.DeletePaycheck(p.Id);
    //             IsEditingPaycheck = false;
    //             EditingPaycheckClone = null;
    //             LoadData();
    //             RefreshPaychecks();
    //             CalculateProjections();
    //         }
    //     }
    // }

    private void DeletePaycheck() {
        if (EditingPaycheckClone == null) return;
        var messageBoxResult = MessageBox.Show(
            "Are you sure you want to delete this paycheck?", // Message
            "Delete Confirmation", // Title
            MessageBoxButton.YesNo, // Buttons
            MessageBoxImage.Warning // Icon
        );

        // Check the user's response
        if (messageBoxResult == MessageBoxResult.Yes) {
            try {
                // User confirmed deletion, proceed with your delete logic here
                _budgetService.DeletePaycheck(EditingPaycheckClone.Id);
                IsEditingPaycheck = false;
                EditingPaycheckClone = null;
                LoadData();
                RefreshPaychecks();
                CalculateProjections();
            }
            catch (Exception ex) {
                Log.Error(ex, "Error deleting paycheck.");
                MessageBox.Show("Failed to delete paycheck. See log for details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    #endregion

    #region Account CRUD

    private void AddAccount() {
        try {
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
        catch (Exception ex) {
            Log.Error(ex, "Error initializing new account.");
        }
    }

    private void EditAccount() {
        try {
            CancelAccount();
            if (SelectedAccount == null) return;
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
                    StatementDay = SelectedAccount.CreditCardDetails.StatementDay,
                    DueDateOffset = SelectedAccount.CreditCardDetails.DueDateOffset,
                    GraceActive = SelectedAccount.CreditCardDetails.GraceActive,
                    MinPayFloor = SelectedAccount.CreditCardDetails.MinPayFloor,
                    PayPreviousMonthBalanceInFull = SelectedAccount.CreditCardDetails.PayPreviousMonthBalanceInFull
                };
            }
            else {
                EditingAccountClone.CreditCardDetails = new CreditCardDetails();
            }

            IsEditingAccount = true;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error entering edit mode for account.");
        }
    }

    private void SaveAccount() {
        if (EditingAccountClone != null) {
            try {
                if (EditingAccountClone.Type == AccountType.Checking && (EditingAccountClone.AccountAprHistory == null ||
                                                                         EditingAccountClone.AccountAprHistory.Count ==
                                                                         0)) {
                    MessageBox.Show(
                        "Before you can save this credit card, you need to set up your interest rates.", // Message
                        "Incomplete Setup", // Title
                        MessageBoxButton.OK, // Buttons
                        MessageBoxImage.Warning // Icon
                    );
                    SetAccountAprRatesCommand.Execute(EditingAccountClone);
                    return;
                }

                if (SelectedAccount != null) {
                    UpdateAccountFromClone(SelectedAccount, EditingAccountClone);
                    _budgetService.UpsertAccount(SelectedAccount);
                }
                else {
                    EditingAccountClone.Id = _budgetService.UpsertAccount(EditingAccountClone);
                    var openingBalance = new Transaction() {
                        AccountId = EditingAccountClone.Id, 
                        Amount = EditingAccountClone.Balance, 
                        TransactionDate = EditingAccountClone.BalanceAsOf , 
                        TransactionId = Guid.NewGuid(),
                        FitId = Guid.NewGuid().ToString(),
                        Description = "Opening Balance", 
                        Memo = "Opening Balance", 
                        PeriodDate = EditingAccountClone.BalanceAsOf
                    };

                    if (openingBalance.Amount != 0) {
                        Task.Run(async () => {
                            try {
                                await _budgetService.UpsertTransactionAsync(openingBalance);
                            }
                            catch (Exception ex) {
                                Log.Error(ex, "Error upserting transaction in PropertyChanged.");
                            }
                        }); // Run async work

                        var transactions = _budgetService.GetAccountTransactions(openingBalance.AccountId.Value);

                        string json = JsonConvert.SerializeObject(transactions.ToList());
                        var reconciliationTransactions =
                            JsonConvert.DeserializeObject<List<ReconciliationTransaction>>(json);
                        if (reconciliationTransactions != null) {
                            _reconciliationService.ReconcileAccount(
                                openingBalance.AccountId.Value,
                                reconciliationTransactions,
                                openingBalance.Amount,
                                openingBalance.TransactionDate);
                        }
                    }

                    LoadData();
                }

                IsEditingAccount = false;
                EditingAccountClone = null;
                CalculateProjections();

                // Re-trigger Accounts collection change to update chart
                OnPropertyChanged(nameof(Accounts));
            }
            catch (Exception ex) {
                Log.Error(ex, "Error saving account.");
                MessageBox.Show("Failed to save account. See log for details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

        if (clone is { Type: AccountType.Mortgage, MortgageDetails: not null }) {
            target.MortgageDetails ??= new MortgageDetails();
            target.MortgageDetails.InterestRate = clone.MortgageDetails.InterestRate;
            target.MortgageDetails.Escrow = clone.MortgageDetails.Escrow;
            target.MortgageDetails.MortgageInsurance = clone.MortgageDetails.MortgageInsurance;
            target.MortgageDetails.LoanPayment = clone.MortgageDetails.LoanPayment;
            target.MortgageDetails.PaymentDate = clone.MortgageDetails.PaymentDate;
        }

        if (clone is { Type: AccountType.CreditCard, CreditCardDetails: not null }) {
            target.CreditCardDetails ??= new CreditCardDetails();
            target.CreditCardDetails.StatementDay = clone.CreditCardDetails.StatementDay;
            target.CreditCardDetails.DueDateOffset = clone.CreditCardDetails.DueDateOffset;
            target.CreditCardDetails.GraceActive = clone.CreditCardDetails.GraceActive;
            target.CreditCardDetails.MinPayFloor = clone.CreditCardDetails.MinPayFloor;
            target.CreditCardDetails.PayPreviousMonthBalanceInFull =
                clone.CreditCardDetails.PayPreviousMonthBalanceInFull;
        }
    }

    private void CancelAccount() {
        try {
            IsEditingAccount = false;
            EditingAccountClone = null;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error cancelling account edit.");
        }
    }

    // private void DeleteAccount(Account? a) {
    //     if (a != null) {
    //         MessageBoxResult messageBoxResult = MessageBox.Show(
    //             "Are you sure you want to delete this account?", // Message
    //             "Delete Confirmation", // Title
    //             MessageBoxButton.YesNo, // Buttons
    //             MessageBoxImage.Warning // Icon
    //         );
    //
    //         // Check the user's response
    //         if (messageBoxResult == MessageBoxResult.Yes) {
    //             // User confirmed deletion, proceed with your delete logic here
    //             _budgetService.DeleteAccount(a.Id);
    //             IsEditingAccount = false;
    //             EditingAccountClone = null;
    //             LoadData();
    //             CalculateProjections();
    //         }
    //     }
    // }

    private void DeleteAccount() {
        if (EditingAccountClone == null) return;
        var messageBoxResult = MessageBox.Show(
            "Are you sure you want to delete this account?", // Message
            "Delete Confirmation", // Title
            MessageBoxButton.YesNo, // Buttons
            MessageBoxImage.Warning // Icon
        );

        // Check the user's response
        if (messageBoxResult == MessageBoxResult.Yes) {
            try {
                // User confirmed deletion, proceed with your delete logic here
                _budgetService.DeleteAccount(EditingAccountClone.Id);
                IsEditingAccount = false;
                EditingAccountClone = null;
                LoadData();
                CalculateProjections();
            }
            catch (Exception ex) {
                Log.Error(ex, "Error deleting account.");
                MessageBox.Show("Failed to delete account. See log for details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    #endregion

    #region Helpers

    public void CalculateProjections() {
        if (_isCalculatingProjections) return;
        _isCalculatingProjections = true;
        try {
            
            var paychecks = _budgetService.GetAllPaychecks();
            var bills = _budgetService.GetAllBills();
            var buckets = _budgetService.GetAllBuckets();
            var periodBills = _budgetService.GetAllPeriodBills();
            var periodBuckets = _budgetService.GetAllPeriodBuckets();
            var transactions = ShowReconciled
                ? _budgetService.GetAllTransactions()
                : _budgetService.GetAllUnreconciledTransactions();
            var reconciliations = !ShowReconciled ? _budgetService.GetAllAccountReconciliations() : null;
            reconciliations = null;
            var start = CurrentPeriodDate == DateTime.MinValue ? DateTime.Today : CurrentPeriodDate;
            if (ProjectionStartDate.HasValue) start = ProjectionStartDate.Value;
            var accounts = _budgetService.GetAllAccountsAsOf(start.AddDays(-1)).ToList();
            var end = ProjectionEndDate;
            if (end < start) end = start.AddYears(1);
            // start = new DateTime(2026, 2, 19);
            // end = new DateTime(2027, 2, 19);
            var allPaycheckTransactions = _budgetService.GetAllPaycheckTransactions();
            var allBillTransactions = _budgetService.GetBillTransactions();
            var allBucketTransactions = _budgetService.GetBucketTransactions();
            var allTransactions = _budgetService.GetAllTransactions().ToList();
            var paycheckTransactions = allPaycheckTransactions.ToList();
            
            #region Massage paycheck transaction date
            //Whatever date the paycheck may have come in on, for purposes of this projection, it came in on its expected date.
            //So that it can be attributed to the pay period.
            foreach (var allPaycheckTransaction in paycheckTransactions) {
                if (allPaycheckTransaction.PaycheckOccurrenceDate != null && allPaycheckTransaction.TransactionDate!=allPaycheckTransaction.PaycheckOccurrenceDate) {
                    allPaycheckTransaction.TransactionDate = allPaycheckTransaction.PaycheckOccurrenceDate.Value;
                }
            }

            allTransactions.Where(x => x.PaycheckId != null).ToList().ForEach(x => {
                if (x.PaycheckOccurrenceDate != null && x.TransactionDate != x.PaycheckOccurrenceDate) {
                    x.TransactionDate = x.PaycheckOccurrenceDate.Value;
                }
            });
            #endregion
            
            var results = _projectionEngine.CalculateProjections(
                paycheckTransactions,
                allBillTransactions.ToList(),
                allBucketTransactions.ToList(),
                allTransactions,
                start, end, accounts.ToList(), paychecks.ToList(), bills.ToList(), buckets.ToList(),
                periodBills.ToList(), periodBuckets.ToList(), transactions.ToList(), reconciliations?.ToList(),
                ShowReconciled, true);

            var resultList = results.ToList();
            Projections = new ObservableCollection<ProjectionItem>(resultList);

            // Check for negative checking/savings accounts
            var negativeAccounts = new HashSet<string>();
            foreach (var item in resultList) {
                foreach (var acc in accounts) {
                    if (acc.Type is not (AccountType.Checking or AccountType.Savings)) continue;
                    if (item.AccountBalances.TryGetValue(acc.Name, out decimal balance) && balance < 0) {
                        negativeAccounts.Add(acc.Name);
                    }
                }
            }

            if (negativeAccounts.Any()) {
                string message =
                    $"Warning: The following accounts go negative in the projection: {string.Join(", ", negativeAccounts)}";
                ShowToast(message);
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error calculating projections.");
            ShowToast("Failed to calculate projections. Check logs.");
        }
        finally {
            _isCalculatingProjections = false;
        }
    }

    public void ShowToast(string message) {
        Application.Current.Dispatcher.Invoke(() => {
            // Avoid duplicate toasts with the same message
            if (Toasts.Any(t => t.Message == message)) return;

            var toast = new ToastViewModel(message,
                t => { Application.Current.Dispatcher.Invoke(() => Toasts.Remove(t)); });
            Toasts.Add(toast);
        });
    }
    
    public List<PeriodBill> GetProjectedBillsForPeriod(DateTime periodStart) {
        try {
            var periodEnd = periodStart.AddDays(14); // Default
            if (ShowByMonth) {
                periodEnd = periodStart.AddMonths(1);
            }
            else {
                var allPaycheckDates = new List<DateTime>();
                foreach (var pay in Paychecks) {
                    var nextPay = pay.StartDate;
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
        catch (Exception ex) {
            Log.Error(ex, "Error getting projected bills for period starting {PeriodStart}.", periodStart);
            return new List<PeriodBill>();
        }
    }

    private void LoadData() {
        Log.Information("Loading all budget data.");
        _isLoadingData = true;
        try {
            var accounts = _budgetService.GetAllAccounts().ToList();
            if (accounts.All(a => a.Name != "Household Cash")) {
                Log.Information("Household Cash account not found. Creating default.");
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

            var accountsWithNone = new List<Account> { new Account { Id = 0, Name = "(None)" } };
            accountsWithNone.AddRange(accounts);
            AccountsWithNone = new ObservableCollection<Account>(accountsWithNone);

            var bills = _budgetService.GetAllBills();
            bills = bills.OrderBy(b => b.DueDay).ThenBy(b => b.Name).ToList();
            foreach (var b in bills) b.PropertyChanged += Item_PropertyChanged;
            Bills = new ObservableCollection<Bill>(bills);

            var billsWithNone = new List<Bill> { new Bill { Id = 0, Name = "(None)" } };
            billsWithNone.AddRange(bills);
            BillsWithNone = new ObservableCollection<Bill>(billsWithNone);

            var paychecks = _budgetService.GetAllPaychecks();
            paychecks = paychecks.OrderBy(b => b.Name).ToList();
            foreach (var p in paychecks) p.PropertyChanged += Item_PropertyChanged;
            Paychecks = new ObservableCollection<Paycheck>(paychecks);

            var paychecksWithNone = new List<Paycheck> { new Paycheck { Id = 0, Name = "(None)" } };
            paychecksWithNone.AddRange(paychecks);
            PaychecksWithNone = new ObservableCollection<Paycheck>(paychecksWithNone);

            var buckets = _budgetService.GetAllBuckets();
            buckets = buckets.OrderBy(b => b.Name).ToList();
            foreach (var b in buckets) b.PropertyChanged += Item_PropertyChanged;
            Buckets = new ObservableCollection<BudgetBucket>(buckets);

            var bucketsWithNone = new List<BudgetBucket> { new BudgetBucket { Id = 0, Name = "(None)" } };
            bucketsWithNone.AddRange(buckets);
            BucketsWithNone = new ObservableCollection<BudgetBucket>(bucketsWithNone);
            
            Log.Information("Budget data loaded successfully. Accounts: {AccountCount}, Bills: {BillCount}", Accounts.Count, Bills.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load budget data.");
            MessageBox.Show("Failed to load budget data. See log for details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally {
            _isLoadingData = false;
        }
    }

    private void LoadPaychecks() {
        try {
            var allPaychecks = Paychecks.ToList();
            if (allPaychecks.Count == 0) {
                CurrentPeriodDate = DateTime.Today;
                return;
            }

            PeriodPaychecks = new ObservableCollection<Paycheck>(allPaychecks);

            SetCurrentPeriodDate();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error loading paychecks into period view.");
        }
    }

    private void LoadPeriodData() {
        try {
            LoadPeriodBills();
            LoadPeriodBuckets();
            LoadPeriodTransactions();
            ApplyTransactionAmounts();
            UpdateWarningMetrics();
            UpdateBucketWarningMetrics();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error loading period data.");
        }
    }

    private void ApplyTransactionAmounts() {
        try {
            foreach (var pb in CurrentPeriodBills) {
                pb.TransactionAmount = CurrentPeriodTransactions
                    .Where(t => t.BillId == pb.BillId)
                    .Sum(t => t.Amount);
            }

            foreach (var pb in CurrentPeriodBuckets) {
                pb.TransactionAmount = CurrentPeriodTransactions
                    .Where(t => t.BucketId == pb.BucketId)
                    .Sum(t => t.Amount);
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error applying transaction amounts to period items.");
        }
    }

    private void LoadPeriodBills() {
        try {
            var pBills = _budgetService.GetPeriodBills(CurrentPeriodDate).ToList();
            pBills = pBills.OrderBy(pb => pb.DueDate).ToList();
            // Always ensure projected bills for this period are in the database and collection
            var projectedBillsForPeriod = GetProjectedBillsForPeriod(CurrentPeriodDate);

            foreach (var pb in projectedBillsForPeriod) {
                if (!pBills.Any(existing =>
                        existing.BillId == pb.BillId && existing.PeriodDate.Date == pb.PeriodDate.Date)) { }
                else {
                    var periodBill = pBills.SingleOrDefault(existing =>
                        existing.BillId == pb.BillId && existing.PeriodDate.Date == pb.PeriodDate.Date);
                    UpdatePeriodBillFromClone(pb, periodBill!);
                }
            }

            projectedBillsForPeriod = projectedBillsForPeriod.OrderBy(pb => pb.DueDate).ToList();

            CurrentPeriodBills = new ObservableCollection<PeriodBill>(projectedBillsForPeriod);
            foreach (var pb in CurrentPeriodBills) pb.PropertyChanged += PeriodBill_PropertyChanged;
            UpdateWarningMetrics();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error loading period bills.");
        }
    }

    private void LoadPeriodBuckets() {
        try {
            var pBuckets = _budgetService.GetPeriodBucketsIncludingMonthly(CurrentPeriodDate).ToList();

            foreach (var bucket in Buckets.Where(b =>
                         b.PaycheckId == null || (b.PaycheckId == SelectedPeriodPaycheckId && !ShowByMonth))) {
                if (pBuckets.All(existing => existing.BucketId != bucket.Id)) {
                    var pb = new PeriodBucket {
                        BucketId = bucket.Id,
                        BucketName = bucket.Name,
                        PeriodDate = bucket.PaycheckId == null
                            ? new DateTime(CurrentPeriodDate.Year, CurrentPeriodDate.Month, 1)
                            : CurrentPeriodDate,
                        ActualAmount = bucket.ExpectedAmount,
                        IsPaid = false,
                        FitId = Guid.NewGuid()
                    };
                    pBuckets.Add(pb);
                }
            }

            CurrentPeriodBuckets = new ObservableCollection<PeriodBucket>(pBuckets);
            foreach (var pb in CurrentPeriodBuckets) pb.PropertyChanged += PeriodBucket_PropertyChanged;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error loading period buckets.");
        }
    }

    private void LoadPeriodTransactions() {
        try {
            var transactions = _budgetService.GetTransactions(CurrentPeriodDate).ToList();
            transactions = transactions.OrderBy(pb => pb.TransactionDate).ToList();
            CurrentPeriodTransactions = new ObservableCollection<Transaction>(transactions);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error loading period transactions.");
        }
    }

    private void InitializePeriod() {
        try {
            if (ShowByMonth) {
                CurrentPeriodDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                return;
            }

            LoadPaychecks();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error initializing period.");
        }
    }

    private void NavigatePeriod(int direction) {
        try {
            if (ShowByMonth) {
                CurrentPeriodDate = CurrentPeriodDate.AddMonths(direction);
                LoadPeriodData();
                return;
            }

            var allPaycheckDates = new List<DateTime>();
            var end = DateTime.Today.AddYears(1);
            foreach (var pay in Paychecks.Where(p => p.Id == SelectedPeriodPaycheckId)) {
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
            var currentIndex = sortedDates.FindIndex(d => d.Date == CurrentPeriodDate.Date);

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
        catch (Exception ex) {
            Log.Error(ex, "Error navigating period.");
        }
    }

    private void ReconcileAccount() {
        if (EditingAccountClone == null) return;
        try {
            var window = new ReconciliationWindow(EditingAccountClone, _budgetService) {
                Owner = Application.Current.MainWindow
            };
            window.ShowDialog();
            CalculateProjections();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error showing reconciliation window.");
            MessageBox.Show("Failed to open reconciliation window. See log for details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private void ImportAccount() {
        if (EditingAccountClone == null) return;
        try {
            var window = new ImportReconciliationWindow(EditingAccountClone, _budgetService) {
                Owner = Application.Current.MainWindow
            };
            window.ShowDialog();
            CalculateProjections();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error showing import window.");
            MessageBox.Show("Failed to open import window. See log for details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private void SetAccountAprRates() {
        if (EditingAccountClone is not { Type: AccountType.CreditCard }) return;
        try {
            EditingAccountClone.AccountAprHistory ??= [];
            var window = new AccountAprHistoryWindow(EditingAccountClone, _budgetService) {
                Owner = Application.Current.MainWindow
            };
            window.ShowDialog();
            CalculateProjections();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error showing APR history window.");
            MessageBox.Show("Failed to open interest rate window. See log for details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }


    private void RefreshPaychecks() {
        try {
            var allPaychecks = Paychecks.ToList();
            if (allPaychecks.Count == 0) {
                CurrentPeriodDate = DateTime.Today;
                return;
            }

            PeriodPaychecks = new ObservableCollection<Paycheck>(allPaychecks);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error refreshing paychecks list.");
        }
    }

    private void SetCurrentPeriodDate(int? id = null) {
        try {
            var allPaychecks = Paychecks.ToList();
            if (allPaychecks.Count == 0) {
                CurrentPeriodDate = DateTime.Today;
                return;
            }

            DateTime latestPayBeforeToday = DateTime.MinValue;
            foreach (var pay in allPaychecks.Where(p => id == null || p.Id == id)) {
                var nextPay = pay.StartDate;
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

            if (id == null && currentPeriodPaychecks.Any()) {
                _selectedPeriodPaycheckId = currentPeriodPaychecks.First().Id;
                OnPropertyChanged(nameof(SelectedPeriodPaycheckId));
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error setting current period date.");
        }
    }

    private void ShowAbout() {
        try {
            var about = new AboutWindow {
                Owner = Application.Current.MainWindow
            };
            about.ShowDialog();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error showing about window.");
        }
    }

    private void Exit() {
        try {
            Application.Current.Shutdown();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error during exit.");
        }
    }
    
    private void Backup() {
        try {
            var file = _budgetService.BackupDatabase();
            MessageBox.Show($"Database backup saved successfully to {file}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) {
            MessageBox.Show(ex.Message, "Backup Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowAmortization(Account account) {
        try {
            var amortization = new AmortizationWindow(account) {
                Owner = Application.Current.MainWindow
            };
            amortization.ShowDialog();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error showing amortization window.");
        }
    }

    #endregion
}