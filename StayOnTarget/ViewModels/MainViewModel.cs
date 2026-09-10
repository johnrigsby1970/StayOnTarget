using Serilog;
using StayOnTarget.Models;
using StayOnTarget.Services;
using StayOnTarget.Services.Projections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Media;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using StayOnTarget.Extensions;
using StayOnTarget.Helpers;
using StayOnTarget.Views;

#pragma warning disable CS0414 // Field is assigned but its value is never used

namespace StayOnTarget.ViewModels;

public class MainViewModel : ViewModelBase {
    private readonly BudgetService _budgetService = null!;
    private readonly ReconciliationService _reconciliationService = null!;
    private readonly IProjectionEngine _projectionEngine = null!;
    private RangeObservableCollection<PeriodBill> _currentPeriodBills = new();
    private RangeObservableCollection<PeriodBucket> _currentPeriodBuckets = new();
    private int _pastDueCount;
    private int _upcomingCount;
    private int _budgetExceededCount;
    private int _envelopeNearingFullCount;
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
    private ObservableCollection<ToastViewModel> _toasts = new();
    private bool _isEditingPaycheck;
    private Paycheck? _selectedPaycheck;
    private string _toggleReconciliationText = "Show Reconciled";
    private DateTime _projectionEndDate = DateTime.Today.AddYears(1);
    private DateTime? _projectionStartDate;
    private int _selectedOuterTabIndex;
    private int _selectedInnerTabIndex;
    private int _selectedProjectionTabIndex;
    private SnowballStrategyOptions _snowballOptions = new();
    private NavigationItemViewModel? _selectedNavigationItem;

    #region Properties

    #region Split Details Properties

    private bool _isSplitTransaction;

    public bool IsSplitTransaction {
        get => _isSplitTransaction;
        set {
            if (SetProperty(ref _isSplitTransaction, value)) {
                if (value) {
                    // Only add an initial leg if the split details list is empty!
                    if (!EditingSplitDetails.Any()) {
                        AddSplitDetailLeg();
                    }
                }

                OnPropertyChanged(nameof(UnassignedSplitAmount));
                OnPropertyChanged(nameof(CanSaveTransaction));
            }
        }
    }

    public ObservableCollection<TransactionDetail> EditingSplitDetails { get; } = new();

    public decimal UnassignedSplitAmount {
        get {
            if (EditingTransactionClone == null) return 0m;
            decimal totalAllocated = EditingSplitDetails.Sum(d => d.Amount);
            return Math.Abs(EditingTransactionClone.Amount) - totalAllocated;
        }
    }

    public IRelayCommand ToggleSplitTransactionCommand { get; } = null!;
    public IRelayCommand AddSplitDetailLegCommand { get; } = null!;
    public IRelayCommand<TransactionDetail> RemoveSplitDetailLegCommand { get; } = null!;

    #endregion

    #region Overlay Staged Collections

    public RangeObservableCollection<Transaction> StagedTransactions { get; } = new();

    public IRelayCommand AddAnotherTransactionCommand { get; } = null!;
    public IAsyncRelayCommand DeleteSelectedTransactionCommand { get; } = null!;
    public IRelayCommand<Transaction> RemoveStagedTransactionCommand { get; } = null!;
    public IRelayCommand ClearCurrentTransactionDraftCommand { get; } = null!;

    public RangeObservableCollection<Account> StagedAccounts { get; } = new();

    public IRelayCommand AddAnotherAccountCommand { get; } = null!;
    public IAsyncRelayCommand DeleteSelectedAccountCommand { get; } = null!;
    public IRelayCommand<Account> RemoveStagedAccountCommand { get; } = null!;
    public IRelayCommand ClearCurrentAccountDraftCommand { get; } = null!;

    public RangeObservableCollection<Bill> StagedBills { get; } = new();

    public IRelayCommand AddAnotherBillCommand { get; } = null!;
    public IAsyncRelayCommand DeleteSelectedBillCommand { get; } = null!;
    public IRelayCommand<Bill> RemoveStagedBillCommand { get; } = null!;
    public IRelayCommand ClearCurrentBillDraftCommand { get; } = null!;

    public RangeObservableCollection<BudgetBucket> StagedBuckets { get; } = new();

    public IRelayCommand AddAnotherBucketCommand { get; } = null!;
    public IAsyncRelayCommand DeleteSelectedBucketCommand { get; } = null!;
    public IRelayCommand<BudgetBucket> RemoveStagedBucketCommand { get; } = null!;
    public IRelayCommand ClearCurrentBucketDraftCommand { get; } = null!;

    public RangeObservableCollection<Paycheck> StagedPaychecks { get; } = new();

    public IRelayCommand AddAnotherPaycheckCommand { get; } = null!;
    public IAsyncRelayCommand DeleteSelectedPaycheckCommand { get; } = null!;
    public IRelayCommand<Paycheck> RemoveStagedPaycheckCommand { get; } = null!;
    public IRelayCommand ClearCurrentPaycheckDraftCommand { get; } = null!;

    public RangeObservableCollection<Category> StagedCategories { get; } = new();

    public IRelayCommand AddAnotherCategoryCommand { get; } = null!;
    public IAsyncRelayCommand DeleteSelectedCategoryCommand { get; } = null!;
    public IRelayCommand<Category> RemoveStagedCategoryCommand { get; } = null!;
    public IRelayCommand ClearCurrentCategoryDraftCommand { get; } = null!;

    public RangeObservableCollection<SubCategory> StagedSubCategories { get; } = new();

    public IRelayCommand<SubCategory> RemoveStagedSubCategoryCommand { get; } = null!;

    public bool HasStagedCategoryItems => (StagedCategories?.Count > 0) || (StagedSubCategories?.Count > 0);

    public IEnumerable<Category> AvailableParentCategories {
        get {
            var list = CategoriesWithNone.ToList();

            // Include active category clone if present and not already in the list
            if (EditingCategoryClone != null) {
                var displayName = string.IsNullOrWhiteSpace(EditingCategoryClone.Name)
                    ? "(New Category)"
                    : EditingCategoryClone.Name;

                if (!list.Any(c => c.Name.Equals(displayName, StringComparison.OrdinalIgnoreCase))) {
                    list.Add(new Category { Id = 0, Name = displayName });
                }
            }

            // Add staged categories if not already present
            foreach (var staged in StagedCategories) {
                if (!string.IsNullOrWhiteSpace(staged.Name) &&
                    !list.Any(c => c.Name.Equals(staged.Name, StringComparison.OrdinalIgnoreCase))) {
                    list.Add(new Category { Id = staged.Id, Name = staged.Name });
                }
            }

            return list;
        }
    }

    #endregion

    #region Reconciliation Sub-Panel Fields & Properties

    private int? _originalFromAccountReconciledId;
    private int? _originalToAccountReconciledId;

    public ObservableCollection<TransactionStatusItemViewModel> EditingTransactionStatusItems { get; } = new();

    #endregion

    public List<MonthOption> MonthOptions { get; } = new() {
        new MonthOption { Key = "01", Name = "January" },
        new MonthOption { Key = "02", Name = "February" },
        new MonthOption { Key = "03", Name = "March" },
        new MonthOption { Key = "04", Name = "April" },
        new MonthOption { Key = "05", Name = "May" },
        new MonthOption { Key = "06", Name = "June" },
        new MonthOption { Key = "07", Name = "July" },
        new MonthOption { Key = "08", Name = "August" },
        new MonthOption { Key = "09", Name = "September" },
        new MonthOption { Key = "10", Name = "October" },
        new MonthOption { Key = "11", Name = "November" },
        new MonthOption { Key = "12", Name = "December" }
    };

    // Commands
    public IRelayCommand AddBucketOverrideCommand { get; } = null!;
    public IRelayCommand<OverrideItem> RemoveBucketOverrideCommand { get; } = null!;

    // Commands
    public IRelayCommand AddBillOverrideCommand { get; } = null!;
    public IRelayCommand<OverrideItem> RemoveBillOverrideCommand { get; } = null!;

    private bool _isFlyoutOpen;

    public bool IsFlyoutOpen {
        get => _isFlyoutOpen;
        set => SetProperty(ref _isFlyoutOpen, value);
    }

    private void ToggleFlyout() {
        try {
            IsFlyoutOpen = !IsFlyoutOpen;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error toggling flyout in MainViewModel.");
        }
    }

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; } = new();

    public NavigationItemViewModel? SelectedNavigationItem {
        get => _selectedNavigationItem;
        set {
            try {
                if (SetProperty(ref _selectedNavigationItem, value) && value != null) {
                    SelectedOuterTabIndex = value.TabIndex;
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting SelectedNavigationItem in MainViewModel.");
            }
        }
    }

    public IEnumerable<TargetFrequencyType> TargetFrequencyTypes =>
        Enum.GetValues(typeof(TargetFrequencyType)).Cast<TargetFrequencyType>();

    public IEnumerable<BucketType> BucketTypes => Enum.GetValues(typeof(BucketType)).Cast<BucketType>();

    public SnowballStrategyOptions SnowballOptions {
        get => _snowballOptions;
        set {
            try {
                if (_snowballOptions != null) {
                    _snowballOptions.PropertyChanged -= OnSnowballOptionsPropertyChanged;
                }

                if (SetProperty(ref _snowballOptions, value)) {
                    if (_snowballOptions != null) {
                        _snowballOptions.PropertyChanged += OnSnowballOptionsPropertyChanged;
                    }

                    RequestProjectionRecalculation();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting SnowballOptions in MainViewModel.");
            }
        }
    }

    private async void OnSnowballOptionsPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (_isLoadingData || IsLoading) return;

        RequestProjectionRecalculation();

        try {
            var optionsToSerialize = sender as SnowballStrategyOptions ?? this.SnowballOptions;
            if (optionsToSerialize != null) {
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(optionsToSerialize);
                await _budgetService.SaveSettingAsync("SnowballStrategyOptions", json);
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Failed to save SnowballOptions setting.");
        }
    }

    public RangeObservableCollection<BucketPaycheckAllocation> EditableAllocations { get; }
        = new RangeObservableCollection<BucketPaycheckAllocation>();

    public RangeObservableCollection<SelectableSubCategory> EditableSubCategories { get; }
        = new RangeObservableCollection<SelectableSubCategory>();

    public RangeObservableCollection<ProjectionItem> SnowballProjections { get; } = new();

    public bool IsBucketDescriptionExpanded {
        get => _isBucketDescriptionExpanded;
        set => SetProperty(ref _isBucketDescriptionExpanded, value);
    }

    public bool IsBillDescriptionExpanded {
        get => _isBillDescriptionExpanded;
        set => SetProperty(ref _isBillDescriptionExpanded, value);
    }

    private bool _isDarkMode;

    public bool IsDarkMode {
        get => _isDarkMode;
        set => SetProperty(ref _isDarkMode, value);
    }

    public static MainViewModel? Instance { get; private set; }

    // private readonly ISpendingAnalyticsService _analyticsService;
    private CancellationTokenSource? _hoverCancellationTokenSource;

    private AnalyticsMetrics? _hoveredPayeeMetrics;

    public AnalyticsMetrics? HoveredPayeeMetrics {
        get => _hoveredPayeeMetrics;
        set {
            if (_hoveredPayeeMetrics != value) {
                _hoveredPayeeMetrics = value;
                OnPropertyChanged(nameof(HoveredPayeeMetrics));
            }
        }
    }

    private AnalyticsMetrics? _hoveredBucketMetrics;

    public AnalyticsMetrics? HoveredBucketMetrics {
        get => _hoveredBucketMetrics;
        set {
            if (_hoveredBucketMetrics != value) {
                _hoveredBucketMetrics = value;
                OnPropertyChanged(nameof(HoveredBucketMetrics));
            }
        }
    }

    private AnalyticsMetrics? _hoveredBillMetrics;

    public AnalyticsMetrics? HoveredBillMetrics {
        get => _hoveredBillMetrics;
        set {
            if (_hoveredBillMetrics != value) {
                _hoveredBillMetrics = value;
                OnPropertyChanged(nameof(HoveredBillMetrics));
            }
        }
    }

    public IAsyncRelayCommand OnTransactionRowHoveredCommand { get; set; } = null!;
    public IAsyncRelayCommand OnBucketRowHoveredCommand { get; set; } = null!;

    public IAsyncRelayCommand OnBillRowHoveredCommand { get; set; } = null!;

    private void EditableAllocations_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
        if (e.OldItems != null) {
            foreach (BucketPaycheckAllocation item in e.OldItems) {
                item.PropertyChanged -= AllocationItem_PropertyChanged;
            }
        }

        if (e.NewItems != null) {
            foreach (BucketPaycheckAllocation item in e.NewItems) {
                item.PropertyChanged += AllocationItem_PropertyChanged;
            }
        }

        OnPropertyChanged(nameof(CanSaveBucket));
        SaveBucketCommand.NotifyCanExecuteChanged();
    }

// 2. Trigger notification whenever any row's AllocationValue or PaycheckId updates
    private void AllocationItem_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        OnPropertyChanged(nameof(TotalBucketItemsToSaveCount));
        OnPropertyChanged(nameof(CanSaveBucket));
        SaveBucketCommand.NotifyCanExecuteChanged();
    }

    public MainViewModel(
        BudgetService budgetService,
        ReconciliationService reconciliationService) {
        try {
            Instance = this;
            _budgetService = budgetService;
            _reconciliationService = reconciliationService;
            _projectionEngine = new ProjectionEngine();
            //_analyticsService = _budgetService;


            OnTransactionRowHoveredCommand =
                new AsyncRelayCommand<Transaction>(async (tx) => await OnTransactionRowHoveredAsync(tx));
            OnBucketRowHoveredCommand =
                new AsyncRelayCommand<PeriodBucket>(async (tx) => await OnBucketRowHoveredAsync(tx));
            OnBillRowHoveredCommand = new AsyncRelayCommand<PeriodBill>(async (pb) => await OnBillRowHoveredAsync(pb));

            AddAnotherTransactionCommand = new RelayCommand(AddAnotherTransaction,
                () => IsEditingTransaction && IsEditingTransactionEnabled);

            DeleteSelectedTransactionCommand =
                new AsyncRelayCommand(DeleteSelectedTransactionAsync, () => CanEditTransaction);
            RemoveStagedTransactionCommand = new RelayCommand<Transaction>(RemoveStagedTransaction);
            ClearCurrentTransactionDraftCommand = new RelayCommand(ClearCurrentTransactionDraft);

            AddAnotherAccountCommand = new RelayCommand(AddAnotherAccount,
                () => IsEditingAccount && IsEditingAccountEnabled);

            DeleteSelectedAccountCommand = new AsyncRelayCommand(DeleteSelectedAccountAsync, () => CanEditAccount);
            RemoveStagedAccountCommand = new RelayCommand<Account>(RemoveStagedAccount);
            ClearCurrentAccountDraftCommand = new RelayCommand(ClearCurrentAccountDraft);

            AddAnotherBillCommand = new RelayCommand(AddAnotherBill,
                () => IsEditingBill && IsEditingBillEnabled);

            DeleteSelectedBillCommand = new AsyncRelayCommand(DeleteSelectedBillAsync, () => CanEditBill);
            RemoveStagedBillCommand = new RelayCommand<Bill>(RemoveStagedBill);
            ClearCurrentBillDraftCommand = new RelayCommand(ClearCurrentBillDraft);

            AddAnotherBucketCommand = new RelayCommand(AddAnotherBucket,
                () => IsEditingBucket && IsEditingBucketEnabled);

            DeleteSelectedBucketCommand = new AsyncRelayCommand(DeleteSelectedBucketAsync, () => CanEditBucket);
            RemoveStagedBucketCommand = new RelayCommand<BudgetBucket>(RemoveStagedBucket);
            ClearCurrentBucketDraftCommand = new RelayCommand(ClearCurrentBucketDraft);

            AddAnotherPaycheckCommand = new RelayCommand(AddAnotherPaycheck,
                () => IsEditingPaycheck && IsEditingPaycheckEnabled);

            DeleteSelectedPaycheckCommand = new AsyncRelayCommand(DeleteSelectedPaycheckAsync, () => CanEditPaycheck);
            RemoveStagedPaycheckCommand = new RelayCommand<Paycheck>(RemoveStagedPaycheck);
            ClearCurrentPaycheckDraftCommand = new RelayCommand(ClearCurrentPaycheckDraft);

            AddAnotherCategoryCommand = new RelayCommand(
                AddAnotherCategory,
                () => IsEditingCategory &&
                      IsEditingCategoryEnabled &&
                      ((EditingCategoryClone != null && !string.IsNullOrWhiteSpace(EditingCategoryClone.Name)) ||
                       (EditingSubCategoryClone != null && !string.IsNullOrWhiteSpace(EditingSubCategoryClone.Name)))
            );

            DeleteSelectedCategoryCommand = new AsyncRelayCommand(DeleteSelectedCategoryAsync, () => CanEditCategory);
            RemoveStagedCategoryCommand = new RelayCommand<Category>(RemoveStagedCategory);
            ClearCurrentCategoryDraftCommand = new RelayCommand(ClearCurrentCategoryDraft);

            ToggleFlyoutCommand = new RelayCommand(ToggleFlyout);

            ImportAccountCommand = new AsyncRelayCommand(ImportAccountAsync, () => CanEditAccount);
            ReconcileAccountCommand =
                new AsyncRelayCommand(ReconcileAccountAsync, () => CanEditAccount);

            SetAccountAprRatesCommand =
                new AsyncRelayCommand(SetAccountAprRatesAsync, () => IsEditingAccount);

            NextPeriodCommand = new AsyncRelayCommand(() => NavigatePeriodAsync(1));
            PrevPeriodCommand = new AsyncRelayCommand(() => NavigatePeriodAsync(-1));

            AddBillCommand = new RelayCommand(AddBill, () => IsNotEditingBill);
            EditBillCommand = new RelayCommand(EditBill, () => CanEditBill);
            CancelBillCommand = new RelayCommand(CancelBill, () => IsEditingBill);
            SaveBillCommand = new AsyncRelayCommand(SaveBillAsync, () => IsEditingBill);
            DeleteBillCommand = new AsyncRelayCommand(DeleteBillAsync, () => IsEditingBill);

            StagedBills.CollectionChanged += (s, e) => {
                OnPropertyChanged(nameof(TotalBillItemsToSaveCount));
                OnPropertyChanged(nameof(CanSaveBill));
                SaveBillCommand.NotifyCanExecuteChanged();
            };

            EditPeriodBillCommand = new RelayCommand(EditPeriodBill, () => CanEditPeriodBill);
            CancelPeriodBillCommand = new RelayCommand(CancelPeriodBill, () => IsEditingPeriodBill);
            SavePeriodBillCommand =
                new AsyncRelayCommand(SavePeriodBillAsync, () => IsEditingPeriodBill);
            DeletePeriodBillCommand =
                new AsyncRelayCommand(DeletePeriodBillAsync, () => IsEditingPeriodBill);

            AddBucketCommand = new RelayCommand(AddBucket, () => IsNotEditingBucket);
            EditBucketCommand = new RelayCommand(EditBucket, () => CanEditBucket);
            CancelBucketCommand = new RelayCommand(CancelBucket, () => IsEditingBucket);
            SaveBucketCommand = new AsyncRelayCommand(SaveBucketAsync, () => IsEditingBucket);
            DeleteBucketCommand = new AsyncRelayCommand(DeleteBucketAsync);

            StagedBuckets.CollectionChanged += (s, e) => {
                OnPropertyChanged(nameof(TotalBucketItemsToSaveCount));
                OnPropertyChanged(nameof(CanSaveBucket));
                SaveBucketCommand.NotifyCanExecuteChanged();
            };

            AddSubCategoryCommand = new RelayCommand(AddSubCategory, () => IsNotEditingSubCategory);
            EditSubCategoryCommand = new RelayCommand(EditSubCategory, () => CanEditSubCategory);
            CancelSubCategoryCommand = new RelayCommand(CancelSubCategory, () => IsEditingSubCategory);
            SaveSubCategoryCommand = new AsyncRelayCommand(SaveSubCategoryAsync, () => IsEditingSubCategory);
            DeleteSubCategoryCommand = new AsyncRelayCommand(DeleteSubCategoryAsync, () => CanEditSubCategory);

            DeleteSelectedSubCategoryCommand =
                new AsyncRelayCommand(DeleteSelectedSubCategoryAsync, () => CanEditSubCategory);

            AddCategoryCommand = new RelayCommand(AddCategory, () => IsNotEditingCategory);
            EditCategoryCommand = new RelayCommand(EditCategory, () => CanEditCategory);
            CancelCategoryCommand = new RelayCommand(CancelCategory, () => IsEditingCategory);
            SaveCategoryCommand = new AsyncRelayCommand(SaveCategoryAsync, () => IsEditingCategory);
            DeleteCategoryCommand = new AsyncRelayCommand(DeleteCategoryAsync);

            RemoveStagedSubCategoryCommand = new RelayCommand<SubCategory>(RemoveStagedSubCategory);

            StagedSubCategories.CollectionChanged += (s, e) => {
                OnPropertyChanged(nameof(TotalCategoryItemsToSaveCount));
                OnPropertyChanged(nameof(CanSaveCategory));
                OnPropertyChanged(nameof(HasStagedCategoryItems));
                SaveCategoryCommand.NotifyCanExecuteChanged();
            };

            StagedCategories.CollectionChanged += (s, e) => {
                OnPropertyChanged(nameof(TotalCategoryItemsToSaveCount));
                OnPropertyChanged(nameof(CanSaveCategory));
                OnPropertyChanged(nameof(HasStagedCategoryItems));
                SaveCategoryCommand.NotifyCanExecuteChanged();
            };

            EditPeriodBucketCommand = new RelayCommand(EditPeriodBucket, () => CanEditPeriodBucket);
            CancelPeriodBucketCommand =
                new RelayCommand(CancelPeriodBucket, () => IsEditingPeriodBucket);
            SavePeriodBucketCommand =
                new AsyncRelayCommand(SavePeriodBucketAsync, () => IsEditingPeriodBucket);
            DeletePeriodBucketCommand =
                new AsyncRelayCommand(DeletePeriodBucketAsync, () => IsEditingPeriodBucket);

            AddTransactionCommand = new RelayCommand(AddTransaction, () => IsNotEditingTransaction);
            EditTransactionCommand = new RelayCommand(EditTransaction, () => CanEditTransaction);
            CancelTransactionCommand = new RelayCommand(CancelTransaction, () => IsEditingTransaction);
            SaveTransactionCommand =
                new AsyncRelayCommand(_ => SaveTransactionAsync(), () => IsEditingTransaction);
            // DeleteTransactionCommand =
            //     new AsyncRelayCommand(DeleteTransactionAsync, () => IsEditingTransaction);

            DeleteTransactionCommand =
                new AsyncRelayCommand(DeleteTransactionAsync, () => SelectedTransaction != null);

            StagedTransactions.CollectionChanged += (s, e) => {
                OnPropertyChanged(nameof(TotalTransactionItemsToSaveCount));
                OnPropertyChanged(nameof(CanSaveTransaction));
                SaveTransactionCommand.NotifyCanExecuteChanged();
            };

            AddPaycheckCommand = new RelayCommand(AddPaycheck);
            EditPaycheckCommand = new RelayCommand(EditPaycheck, () => CanEditPaycheck);
            CancelPaycheckCommand = new RelayCommand(CancelPaycheck, () => IsEditingPaycheck);
            SavePaycheckCommand = new AsyncRelayCommand(SavePaycheckAsync, () => IsEditingPaycheck);
            DeletePaycheckCommand =
                new AsyncRelayCommand(DeletePaycheckAsync, () => IsEditingPaycheck);

            StagedPaychecks.CollectionChanged += (s, e) => {
                OnPropertyChanged(nameof(TotalPaycheckItemsToSaveCount));
                OnPropertyChanged(nameof(CanSavePaycheck));
                SavePaycheckCommand.NotifyCanExecuteChanged();
            };

            AddAccountCommand = new RelayCommand(AddAccount, () => IsNotEditingAccount);
            EditAccountCommand = new RelayCommand(EditAccount, () => CanEditAccount);
            CancelAccountCommand = new RelayCommand(CancelAccount, () => IsEditingAccount);
            SaveAccountCommand = new AsyncRelayCommand(SaveAccountAsync, () => IsEditingAccount);
            DeleteAccountCommand = new AsyncRelayCommand(DeleteAccountAsync, () => IsEditingAccount);

            StagedAccounts.CollectionChanged += (s, e) => {
                OnPropertyChanged(nameof(TotalAccountItemsToSaveCount));
                OnPropertyChanged(nameof(CanSaveAccount));
                SaveAccountCommand.NotifyCanExecuteChanged();
            };

            ShowAmortizationCommand =
                new RelayCommand<Account>(a => ShowAmortization(a as Account ?? throw new InvalidOperationException()));
            ShowAboutCommand = new RelayCommand(ShowAbout);
            SetThemeCommand = new RelayCommand(ToggleTheme);
            ExitCommand = new RelayCommand(Exit);
            BackupCommand = new RelayCommand(Backup);
            SetOneYearCommand = new RelayCommand(() => SetProjectionEndDate(1));
            SetFiveYearCommand = new RelayCommand(() => SetProjectionEndDate(5));
            SetTenYearCommand = new RelayCommand(() => SetProjectionEndDate(10));
            SetThirtyYearCommand = new RelayCommand(() => SetProjectionEndDate(30));

            PayBillCommand = new AsyncRelayCommand<ProjectionItem>(PayBillAsync);
            PayPeriodBillCommand = new AsyncRelayCommand<PeriodBill>(PayPeriodBillAsync);
            FundEnvelopeCommand = new AsyncRelayCommand<ProjectionItem>(FundEnvelopeAsync);
            SkipFundEnvelopeCommand = new AsyncRelayCommand<ProjectionItem>(SkipFundEnvelopeAsync);
            MapsToBillCommand = new RelayCommand<PeriodBill>(pb => {
                if (pb is null) return;
                SelectedPeriodBill = pb;
                SelectedOuterTabIndex = 0;
                SelectedInnerTabIndex = 1;
            });
            MapsToBucketCommand = new RelayCommand<PeriodBucket>(pb => {
                if (pb is null) return;
                SelectedPeriodBucket = pb;
                SelectedOuterTabIndex = 0;
                SelectedInnerTabIndex = 2;
            });
            ToggleBucketDescriptionCommand =
                new RelayCommand(() => IsBucketDescriptionExpanded = !IsBucketDescriptionExpanded);
            ToggleBillDescriptionCommand =
                new RelayCommand(() => IsBillDescriptionExpanded = !IsBillDescriptionExpanded);
            ExportTransactionsCommand = new RelayCommand(ExportTransactions);

            InitializeDataCommand = new AsyncRelayCommand(InitializeDataAsync);

            AddBillOverrideCommand = new RelayCommand(AddBillOverride);
            RemoveBillOverrideCommand = new RelayCommand<OverrideItem>(RemoveBillOverride);

            AddBucketOverrideCommand = new RelayCommand(AddBucketOverride);
            RemoveBucketOverrideCommand = new RelayCommand<OverrideItem>(RemoveBucketOverride);

            InitializeNavigationMenu();

            OpenManageExcludedAccountsCommand = new RelayCommand(OpenManageExcludedAccounts);
            CloseManageExcludedAccountsCommand = new RelayCommand(CloseManageExcludedAccounts);
            ToggleAccountExclusionCommand = new RelayCommand<int>(ToggleAccountExclusion);

            _filteredBillsView = CollectionViewSource.GetDefaultView(BillsWithNone);
            _filteredBillsView.Filter = FilterBillItem;

            #region Splits

            ToggleSplitTransactionCommand = new RelayCommand(() => IsSplitTransaction = !IsSplitTransaction);

            AddSplitDetailLegCommand = new RelayCommand(AddSplitDetailLeg);

            RemoveSplitDetailLegCommand = new RelayCommand<TransactionDetail>(item => {
                if (item != null && EditingSplitDetails.Contains(item)) {
                    EditingSplitDetails.Remove(item);
                    OnPropertyChanged(nameof(UnassignedSplitAmount));
                    OnPropertyChanged(nameof(CanSaveTransaction));
                }
            });

            #endregion

            EditableAllocations.CollectionChanged += EditableAllocations_CollectionChanged;

            IsFlyoutOpen = true;
        }
        catch (Exception ex) {
            Log.Fatal(ex, "Critical error initializing MainViewModel.");
        }
    }

    private async Task OnTransactionRowHoveredAsync(Transaction? transaction) {
        if (transaction == null || string.IsNullOrWhiteSpace(transaction.NormalizedDescription)) {
            HoveredPayeeMetrics = null;
            return;
        }

        // Cancel previous pending hover tasks while scrubbing through rows
        _hoverCancellationTokenSource?.Cancel();
        _hoverCancellationTokenSource = new CancellationTokenSource();
        var token = _hoverCancellationTokenSource.Token;

        try {
            // 150ms debounce before hitting SQLite
            await Task.Delay(150, token);

            if (!token.IsCancellationRequested) {
                HoveredPayeeMetrics = await _budgetService.GetPayeeAnalyticsAsync(transaction.NormalizedDescription);
            }
        }
        catch (TaskCanceledException) {
            // Hovered away before delay elapsed
        }
    }

    private async Task OnBucketRowHoveredAsync(PeriodBucket? bucket) {
        if (bucket == null) {
            HoveredBucketMetrics = null;
            return;
        }

        // Cancel previous pending hover tasks while scrubbing through rows
        _hoverCancellationTokenSource?.Cancel();
        _hoverCancellationTokenSource = new CancellationTokenSource();
        var token = _hoverCancellationTokenSource.Token;

        try {
            // 150ms debounce before hitting SQLite
            await Task.Delay(150, token);

            if (!token.IsCancellationRequested) {
                HoveredBucketMetrics = await _budgetService.GetBucketAnalyticsAsync(bucket.BucketId);
            }
        }
        catch (TaskCanceledException) {
            // Hovered away before delay elapsed
        }
    }

    private async Task OnBillRowHoveredAsync(PeriodBill? periodBill) {
        if (periodBill == null || periodBill.BillId <= 0) {
            HoveredBillMetrics = null;
            return;
        }

        // Cancel previous pending hover tasks while scrubbing through rows
        _hoverCancellationTokenSource?.Cancel();
        _hoverCancellationTokenSource = new CancellationTokenSource();
        var token = _hoverCancellationTokenSource.Token;

        try {
            // 150ms debounce before executing query
            await Task.Delay(150, token);

            if (!token.IsCancellationRequested) {
                // Query analytics by underlying BillId or Bill Name
                HoveredBillMetrics = await _budgetService.GetBillAnalyticsAsync(periodBill.BillId);
            }
        }
        catch (TaskCanceledException) {
            // Hovered away before delay elapsed
        }
    }

    public void ToggleTheme() {
        try {
            IsDarkMode = !IsDarkMode;
            SetTheme(IsDarkMode);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error toggling theme in MainViewModel.");
        }
    }

    private CancellationTokenSource? _recalculationCts;

    public async void RequestProjectionRecalculation() {
        try {
            if (_recalculationCts != null) {
                _recalculationCts.Cancel();
                _recalculationCts.Dispose();
            }

            _recalculationCts = new CancellationTokenSource();

            var token = _recalculationCts.Token;

            _ = RunDebouncedProjectionsAsync(token);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error requesting projection recalculation in MainViewModel.");
        }
    }

    private async Task RunDebouncedProjectionsAsync(CancellationToken cancellationToken) {
        try {
            await Task.Delay(350, cancellationToken);

            if (cancellationToken.IsCancellationRequested) return;

            await CalculateProjectionsAsync(cancellationToken);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) {
            Log.Error(ex, "Error running debounced projections.");
        }
    }


    public IRelayCommand InitializeDataCommand { get; } = null!;
    public IRelayCommand ExportTransactionsCommand { get; } = null!;

    public IAsyncRelayCommand<ProjectionItem> PayBillCommand { get; } = null!;
    public IAsyncRelayCommand<ProjectionItem> FundEnvelopeCommand { get; } = null!;
    public IAsyncRelayCommand<ProjectionItem> SkipFundEnvelopeCommand { get; } = null!;

    public IAsyncRelayCommand<PeriodBill> PayPeriodBillCommand { get; } = null!;

    private async Task InitializeDataAsync() {
        await Task.Yield();

        IsLoading = true;
        IsGatheringData = true;
        IsProjecting = true;
        await Task.Yield();

        try {
            await LoadSnowballOptionsAsync();

            await LoadDataAsync();

            await Task.Yield();

            RefreshExcludableAccounts();

            await Task.Yield();

            InitializePeriod();

            await Task.Yield();

            await LoadPeriodDataAsync();

            await Task.Yield();

            IsGatheringData = false;

            await CalculateProjectionsAsync();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error initializing data in MainViewModel.");
        }
        finally {
            IsLoading = false;
            IsProjecting = false;
        }
    }

    private async Task LoadSnowballOptionsAsync() {
        try {
            if (SnowballOptions != null) {
                SnowballOptions.PropertyChanged -= OnSnowballOptionsPropertyChanged;
            }

            var json = await _budgetService.GetSettingAsync("SnowballStrategyOptions");

            if (!string.IsNullOrWhiteSpace(json)) {
                try {
                    var options = JsonConvert.DeserializeObject<SnowballStrategyOptions>(json);
                    if (options != null) {
                        SnowballOptions = options;
                    }
                }
                catch (Exception ex) {
                    Log.Error(ex, "Failed to deserialize SnowballOptions setting.");
                }
            }

            SnowballOptions ??= new SnowballStrategyOptions();

            SnowballOptions.PropertyChanged += OnSnowballOptionsPropertyChanged;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error loading snowball options.");
        }
    }

    private bool _useAutoSweep;

    public bool UseAutoSweep {
        get => _useAutoSweep;
        set {
            try {
                if (SetProperty(ref _useAutoSweep, value)) {
                    IsProjecting = true;
                    OnCalculateProjections();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting UseAutoSweep in MainViewModel.");
            }
        }
    }

    private int _yearsProjecting = 1;

    public int YearsProjecting {
        get => _yearsProjecting;
        set => SetProperty(ref _yearsProjecting, value);
    }

    private bool _isGatheringData;

    public bool IsGatheringData {
        get => _isGatheringData;
        set => SetProperty(ref _isGatheringData, value);
    }

    private bool _isLoading;

    public bool IsLoading {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private bool _isProjecting;

    public bool IsProjecting {
        get => _isProjecting;
        set => SetProperty(ref _isProjecting, value);
    }

    private bool _isSnowballProjecting;

    public bool IsSnowballProjecting {
        get => _isSnowballProjecting;
        set => SetProperty(ref _isSnowballProjecting, value);
    }

    private string _snowballAnalysisText = string.Empty;

    public string SnowballAnalysisText {
        get => _snowballAnalysisText;
        set => SetProperty(ref _snowballAnalysisText, value);
    }

    private DateTime? _snowballDebtFreeDate;

    public DateTime? SnowballDebtFreeDate {
        get => _snowballDebtFreeDate;
        set => SetProperty(ref _snowballDebtFreeDate, value);
    }

    private int _snowballMonthsSaved;

    public int SnowballMonthsSaved {
        get => _snowballMonthsSaved;
        set => SetProperty(ref _snowballMonthsSaved, value);
    }

    private decimal _snowballFinalNetWorth;

    public decimal SnowballFinalNetWorth {
        get => _snowballFinalNetWorth;
        set => SetProperty(ref _snowballFinalNetWorth, value);
    }

    private decimal _snowballNetWorthImprovement;

    public decimal SnowballNetWorthImprovement {
        get => _snowballNetWorthImprovement;
        set => SetProperty(ref _snowballNetWorthImprovement, value);
    }

    private decimal _snowballFinalDebt;

    public decimal SnowballFinalDebt {
        get => _snowballFinalDebt;
        set => SetProperty(ref _snowballFinalDebt, value);
    }

    private decimal _snowballDebtReductionVsStandard;

    public decimal SnowballDebtReductionVsStandard {
        get => _snowballDebtReductionVsStandard;
        set => SetProperty(ref _snowballDebtReductionVsStandard, value);
    }

    private bool _showSnowballAnalysis;

    public bool ShowSnowballAnalysis {
        get => _showSnowballAnalysis;
        set => SetProperty(ref _showSnowballAnalysis, value);
    }

    public RangeObservableCollection<Bill> Bills { get; } = new();

    public bool HasMultiplePaychecks => Paychecks.Count >= 2;

    public RangeObservableCollection<Paycheck> Paychecks { get; } = new();

    public RangeObservableCollection<Paycheck> PaychecksWithNone { get; } = new();

    private RangeObservableCollection<Account> _accounts = new();

    public RangeObservableCollection<Account> Accounts {
        get => _accounts;
        set {
            try {
                _accounts.CollectionChanged -= OnAccountsCollectionChanged;

                if (SetProperty(ref _accounts, value)) {
                    _accounts.CollectionChanged += OnAccountsCollectionChanged;
                    RefreshExcludableAccounts();
                }
                else {
                    _accounts.CollectionChanged += OnAccountsCollectionChanged;
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting Accounts collection in MainViewModel.");
            }
        }
    }

    public RangeObservableCollection<Account> VisibleAccounts { get; } = new();

    public AccountType[] AccountTypes => (AccountType[])Enum.GetValues(typeof(AccountType));

    public RangeObservableCollection<Account> ActiveAccountsWithNone { get; } = new();

    public RangeObservableCollection<Account> AccountsWithNone { get; } = new();

    public RangeObservableCollection<Bill> BillsWithNone { get; } = new();

    public RangeObservableCollection<BudgetBucket> BucketsWithNone { get; } = new();

    public RangeObservableCollection<SubCategory> SubCategories { get; } = new();

    private SubCategory? _selectedSubCategory;
    private SubCategory? _editingSubCategoryClone;
    private bool _isEditingSubCategory;
    private Category? _selectedCategory;
    private Category? _editingCategoryClone;
    private bool _isEditingCategory;

    public RangeObservableCollection<Category> Categories { get; }
        = new RangeObservableCollection<Category>();

    public RangeObservableCollection<Category> CategoriesWithNone { get; }
        = new RangeObservableCollection<Category>();

    public SubCategory? SelectedSubCategory {
        get => _selectedSubCategory;
        set {
            try {
                if (SetProperty(ref _selectedSubCategory, value)) {
                    if (_selectedCategory != null) IsEditingSubCategory = true;
                    // Add these lines to notify WPF controls bound to SubCategory commands:
                    OnPropertyChanged(nameof(CanEditSubCategory));
                    EditSubCategoryCommand.NotifyCanExecuteChanged();
                    //DeleteSubCategoryCommand.NotifyCanExecuteChanged();
                    SaveSubCategoryCommand.NotifyCanExecuteChanged();
                    CancelSubCategoryCommand.NotifyCanExecuteChanged();
                    DeleteSelectedSubCategoryCommand.NotifyCanExecuteChanged();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting SelectedSubCategory in MainViewModel.");
            }
        }
    }

    public SubCategory? EditingSubCategoryClone {
        get => _editingSubCategoryClone;
        set {
            if (_editingSubCategoryClone != null) {
                _editingSubCategoryClone.PropertyChanged -= EditingSubCategoryClone_PropertyChanged;
            }

            if (SetProperty(ref _editingSubCategoryClone, value)) {
                if (_editingSubCategoryClone != null) {
                    _editingSubCategoryClone.PropertyChanged += EditingSubCategoryClone_PropertyChanged;
                }

                SaveSubCategoryCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsEditingSubCategory {
        get => _isEditingSubCategory;
        set {
            if (SetProperty(ref _isEditingSubCategory, value)) {
                // EditSubCategoryCommand might still depend on whether an edit session is active,
                // but DeleteSubCategoryCommand is no longer tied to this state.
                EditSubCategoryCommand.NotifyCanExecuteChanged();
                SaveSubCategoryCommand.NotifyCanExecuteChanged();
                CancelSubCategoryCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsNotEditingSubCategory => !IsEditingSubCategory;
    public bool CanEditSubCategory => SelectedSubCategory != null;

    public IRelayCommand AddSubCategoryCommand { get; } = null!;
    public IRelayCommand EditSubCategoryCommand { get; } = null!;
    public IRelayCommand SaveSubCategoryCommand { get; } = null!;
    public IRelayCommand CancelSubCategoryCommand { get; } = null!;
    public IAsyncRelayCommand DeleteSubCategoryCommand { get; } = null!;

    public IAsyncRelayCommand DeleteSelectedSubCategoryCommand { get; } = null!;

    public Category? SelectedCategory {
        get => _selectedCategory;
        set {
            try {
                if (SetProperty(ref _selectedCategory, value)) {
                    OnPropertyChanged(nameof(CanEditCategory));
                    EditCategoryCommand.NotifyCanExecuteChanged();
                    DeleteSelectedCategoryCommand.NotifyCanExecuteChanged();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting SelectedCategory in MainViewModel.");
            }
        }
    }

    public Category? EditingCategoryClone {
        get => _editingCategoryClone;
        set => SetProperty(ref _editingCategoryClone, value);
    }

    public bool IsEditingCategory {
        get => _isEditingCategory;
        set {
            try {
                if (SetProperty(ref _isEditingCategory, value)) {
                    OnPropertyChanged(nameof(IsNotEditingCategory));
                    OnPropertyChanged(nameof(CanEditCategory));
                    AddCategoryCommand.NotifyCanExecuteChanged();
                    EditCategoryCommand.NotifyCanExecuteChanged();
                    CancelCategoryCommand.NotifyCanExecuteChanged();
                    SaveCategoryCommand.NotifyCanExecuteChanged();
                    DeleteCategoryCommand.NotifyCanExecuteChanged();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting IsEditingCategory in MainViewModel.");
            }
        }
    }

    public bool IsNotEditingCategory => !IsEditingCategory;
    public bool CanEditCategory => SelectedCategory != null;

    public IRelayCommand AddCategoryCommand { get; } = null!;
    public IRelayCommand EditCategoryCommand { get; } = null!;
    public IRelayCommand SaveCategoryCommand { get; } = null!;
    public IRelayCommand CancelCategoryCommand { get; } = null!;
    public IRelayCommand DeleteCategoryCommand { get; } = null!;

    public RangeObservableCollection<SubCategory> SubCategoriesWithNone { get; } = new();

    public RangeObservableCollection<ProjectionItem> Projections { get; } = new();

    public RangeObservableCollection<PeriodBill> CurrentPeriodBills {
        get => _currentPeriodBills;
        set {
            try {
                if (SetProperty(ref _currentPeriodBills, value)) {
                    UpdateWarningMetrics();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting CurrentPeriodBills in MainViewModel.");
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

    public RangeObservableCollection<PeriodBill> UnpaidPastDueBills { get; } = new();

    private void UpdateWarningMetrics() {
        try {
            var today = DateTime.Today;
            var upcomingLimit = today.AddDays(7);

            var pastDue = CurrentPeriodBills.Where(pb =>
                    !pb.HasActualAmount && pb.DueDate < today && pb.ActualAmount != 0 && pb.TransactionAmount == 0)
                .ToList();

            var upcoming = CurrentPeriodBills.Where(pb =>
                !pb.HasActualAmount && pb.DueDate >= today && pb.DueDate <= upcomingLimit && pb.ActualAmount != 0 &&
                pb.TransactionAmount == 0).ToList();

            PastDueCount = pastDue.Count;
            UpcomingCount = upcoming.Count;

            var temp = new List<PeriodBill>(pastDue.Count);
            foreach (var b in pastDue) {
                temp.Add(b);
            }

            UnpaidPastDueBills.Clear();
            UnpaidPastDueBills.AddRange(temp);

            OnPropertyChanged(nameof(ShowWarningWidget));
        }
        catch (Exception ex) {
            Log.Error(ex, "Error updating warning metrics in MainViewModel.");
        }
    }

    public bool ShowWarningWidget => PastDueCount > 0 || UpcomingCount > 0 || UpcomingStrategyTasks.Count > 0;

    #region Warning Envelope

    public int BudgetExceededCount {
        get => _budgetExceededCount;
        set => SetProperty(ref _budgetExceededCount, value);
    }

    public int EnvelopeNearingFullCount {
        get => _envelopeNearingFullCount;
        set => SetProperty(ref _envelopeNearingFullCount, value);
    }

    public RangeObservableCollection<PeriodBucket> BudgetBustedBuckets { get; } = new();

    private void UpdateBucketWarningMetrics() {
        try {
            var exceeded = CurrentPeriodBuckets.Where(pb =>
                    pb.HasActualAmount && pb.ActualAmount != 0 && pb.BudgetExceeded && pb.TransactionAmount != 0)
                .ToList();
            var nearingfull = CurrentPeriodBuckets.Where(pb =>
                pb.HasActualAmount && pb.ActualAmount != 0 && !pb.BudgetExceeded && pb.TransactionAmount != 0 &&
                Math.Abs((double)pb.TransactionAmount / (double)pb.ActualAmount) > .80).ToList();

            BudgetExceededCount = exceeded.Count;
            EnvelopeNearingFullCount = nearingfull.Count;
            if (nearingfull.Count > 0) {
                var myList = exceeded;
                myList.AddRange(nearingfull);

                var temp = new List<PeriodBucket>(myList.Count);
                foreach (var b in myList) {
                    temp.Add(b);
                }

                BudgetBustedBuckets.Clear();
                BudgetBustedBuckets.AddRange(temp);
            }
            else {
                var temp = new List<PeriodBucket>(exceeded.Count);
                foreach (var b in exceeded) {
                    temp.Add(b);
                }

                BudgetBustedBuckets.Clear();
                BudgetBustedBuckets.AddRange(temp);
            }

            OnPropertyChanged(nameof(ShowEnvelopeWarningWidget));
        }
        catch (Exception ex) {
            Log.Error(ex, "Error updating bucket warning metrics in MainViewModel.");
        }
    }

    public bool ShowEnvelopeWarningWidget => BudgetExceededCount > 0 || EnvelopeNearingFullCount > 0;

    #endregion

    public RangeObservableCollection<BudgetBucket> Buckets { get; } = new();

    public RangeObservableCollection<PeriodBucket> CurrentPeriodBuckets {
        get => _currentPeriodBuckets;
        set {
            try {
                if (SetProperty(ref _currentPeriodBuckets, value)) {
                    UpdateBucketWarningMetrics();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting CurrentPeriodBuckets in MainViewModel.");
            }
        }
    }

    public RangeObservableCollection<Transaction> CurrentPeriodTransactions { get; } = new();

    public string ToggleReconciliationText {
        get => _toggleReconciliationText;
        set => SetProperty(ref _toggleReconciliationText, value);
    }

    private CancellationTokenSource? _cts;

    private async void OnCalculateProjections() {
        IsProjecting = true;

        if (_cts != null) {
            _cts.Cancel();
            _cts.Dispose();
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try {
            await Application.Current.Dispatcher.InvokeAsync(() => { },
                System.Windows.Threading.DispatcherPriority.Render);

            await Task.Delay(300, token);

            await CalculateProjectionsAsync(token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) {
            Log.Error(ex, "Failed to calculate projections.");
        }
    }

    public bool ShowByMonth {
        get => _showByMonth;
        set {
            try {
                if (SetProperty(ref _showByMonth, value)) {
                    InitializePeriod();
                    OnShowByMonthChanged();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting ShowByMonth in MainViewModel.");
            }
        }
    }

    private async void OnShowByMonthChanged() {
        try {
            if (IsGatheringData)
                return;
            await LoadPeriodDataAsync();
        }
        catch (Exception ex) {
            Log.Error(ex, "Failed to load period data for month");
        }
    }

    public int SelectedPeriodPaycheckId {
        get => _selectedPeriodPaycheckId;
        set {
            try {
                if (SetProperty(ref _selectedPeriodPaycheckId, value)) {
                    SetCurrentPeriodDate(value);
                    IsProjecting = true;
                    OnCalculateProjections();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting SelectedPeriodPaycheckId in MainViewModel.");
            }
        }
    }

    public RangeObservableCollection<Paycheck> PeriodPaychecks { get; } = new();

    public ObservableCollection<ToastViewModel> Toasts {
        get => _toasts;
        set => SetProperty(ref _toasts, value);
    }

    public string PeriodDisplay {
        get {
            try {
                if (ShowByMonth) return _currentPeriodDate.ToString("MMMM yyyy");
                return $"Period: {_currentPeriodDate:d}";
            }
            catch (Exception ex) {
                Log.Error(ex, "Error formatting PeriodDisplay in MainViewModel.");

                return string.Empty;
            }
        }
    }

    public DateTime ProjectionEndDate {
        get => _projectionEndDate;
        set {
            try {
                if (SetProperty(ref _projectionEndDate, value)) {
                    IsProjecting = true;
                    OnCalculateProjections();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting ProjectionEndDate in MainViewModel.");
            }
        }
    }

    public DateTime? ProjectionStartDate {
        get => _projectionStartDate;
        set {
            try {
                if (SetProperty(ref _projectionStartDate, value)) {
                    IsProjecting = true;
                    OnCalculateProjections();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting ProjectionStartDate in MainViewModel.");
            }
        }
    }

    public int SelectedOuterTabIndex {
        get => _selectedOuterTabIndex;
        set {
            try {
                if (SetProperty(ref _selectedOuterTabIndex, value)) {
                    var match = NavigationItems.FirstOrDefault(x => x.TabIndex == value);
                    if (match != null && _selectedNavigationItem != match) {
                        _selectedNavigationItem = match;
                        OnPropertyChanged(nameof(SelectedNavigationItem));
                    }
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting SelectedOuterTabIndex in MainViewModel.");
            }
        }
    }

    public int SelectedInnerTabIndex {
        get => _selectedInnerTabIndex;
        set => SetProperty(ref _selectedInnerTabIndex, value);
    }

    public int SelectedProjectionTabIndex {
        get => _selectedProjectionTabIndex;
        set => SetProperty(ref _selectedProjectionTabIndex, value);
    }

    public DateTime CurrentPeriodDate {
        get => _currentPeriodDate;
        set {
            try {
                if (SetProperty(ref _currentPeriodDate, value)) {
                    OnPropertyChanged(nameof(PeriodDisplay));
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting CurrentPeriodDate in MainViewModel.");
            }
        }
    }

    private DateTime _nextPeriodDate;

    public DateTime NextPeriodDate {
        get => _nextPeriodDate;
        set => SetProperty(ref _nextPeriodDate, value);
    }

    public Bill? SelectedBill {
        get => _selectedBill;
        set {
            try {
                if (_selectedBill != value && IsEditingBill && EditingBillClone != null &&
                    EditingBillClone?.Id != value?.Id) {
                    CancelBill();
                }

                if (SetProperty(ref _selectedBill, value)) {
                    OnPropertyChanged(nameof(CanEditBill));
                    EditBillCommand.NotifyCanExecuteChanged();
                    DeleteSelectedBillCommand.NotifyCanExecuteChanged();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting SelectedBill in MainViewModel.");
            }
        }
    }

    public PeriodBill? SelectedPeriodBill {
        get => _selectedPeriodBill;
        set {
            try {
                if (_selectedPeriodBill != value && IsEditingPeriodBill && EditingPeriodBillClone != null &&
                    EditingPeriodBillClone?.Id != value?.Id) {
                    CancelPeriodBill();
                }

                if (SetProperty(ref _selectedPeriodBill, value)) {
                    OnPropertyChanged(nameof(CanEditPeriodBill));
                    EditPeriodBillCommand.NotifyCanExecuteChanged();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting SelectedPeriodBill in MainViewModel.");
            }
        }
    }

    public BudgetBucket? SelectedBucket {
        get => _selectedBucket;
        set {
            try {
                if (_selectedBucket != value && IsEditingBucket && EditingBucketClone != null &&
                    EditingBucketClone?.Id != value?.Id) {
                    CancelBucket();
                }

                if (SetProperty(ref _selectedBucket, value)) {
                    OnPropertyChanged(nameof(CanEditBucket));
                    EditBucketCommand.NotifyCanExecuteChanged();
                    DeleteSelectedBucketCommand.NotifyCanExecuteChanged();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting SelectedBucket in MainViewModel.");
            }
        }
    }

    public PeriodBucket? SelectedPeriodBucket {
        get => _selectedPeriodBucket;
        set {
            try {
                if (_selectedPeriodBucket != value && IsEditingPeriodBucket && EditingPeriodBucketClone != null &&
                    EditingPeriodBucketClone?.Id != value?.Id) {
                    CancelPeriodBucket();
                }

                if (SetProperty(ref _selectedPeriodBucket, value)) {
                    OnPropertyChanged(nameof(CanEditPeriodBucket));
                    EditPeriodBucketCommand.NotifyCanExecuteChanged();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting SelectedPeriodBucket in MainViewModel.");
            }
        }
    }


    public Account? SelectedAccount {
        get => _selectedAccount;
        set {
            try {
                if (_selectedAccount != value && IsEditingAccount && EditingAccountClone != null &&
                    EditingAccountClone?.Id != value?.Id) {
                    CancelAccount();
                }

                if (SetProperty(ref _selectedAccount, value)) {
                    OnPropertyChanged(nameof(CanEditAccount));
                    EditAccountCommand.NotifyCanExecuteChanged();
                    ImportAccountCommand.NotifyCanExecuteChanged();
                    ReconcileAccountCommand.NotifyCanExecuteChanged();
                    DeleteSelectedAccountCommand.NotifyCanExecuteChanged();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting SelectedAccount in MainViewModel.");
            }
        }
    }

    public Transaction? SelectedTransaction {
        get => _selectedTransaction;
        set {
            try {
                if (_selectedTransaction != value && IsEditingTransaction && EditingTransactionClone != null &&
                    EditingTransactionClone?.Id != value?.Id) {
                    CancelTransaction();
                }

                if (SetProperty(ref _selectedTransaction, value)) {
                    OnPropertyChanged(nameof(CanEditTransaction));
                    EditTransactionCommand.NotifyCanExecuteChanged();
                    DeleteSelectedTransactionCommand.NotifyCanExecuteChanged();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting SelectedTransaction in MainViewModel.");
            }
        }
    }

    public Paycheck? SelectedPaycheck {
        get => _selectedPaycheck;
        set {
            try {
                if (_selectedPaycheck != value && IsEditingPaycheck && EditingPaycheckClone != null &&
                    EditingPaycheckClone?.Id != value?.Id) {
                    CancelPaycheck();
                }

                if (SetProperty(ref _selectedPaycheck, value)) {
                    OnPropertyChanged(nameof(CanEditPaycheck));
                    EditPaycheckCommand.NotifyCanExecuteChanged();
                    DeleteSelectedPaycheckCommand.NotifyCanExecuteChanged();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting SelectedPaycheck in MainViewModel.");
            }
        }
    }

    public bool IsEditingBill {
        get => _isEditingBill;
        set {
            try {
                if (SetProperty(ref _isEditingBill, value)) {
                    OnPropertyChanged(nameof(IsNotEditingBill));
                    OnPropertyChanged(nameof(CanEditBill));
                    AddBillCommand.NotifyCanExecuteChanged();
                    EditBillCommand.NotifyCanExecuteChanged();
                    CancelBillCommand.NotifyCanExecuteChanged();
                    SaveBillCommand.NotifyCanExecuteChanged();
                    DeleteBillCommand.NotifyCanExecuteChanged();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting IsEditingBill in MainViewModel.");
            }
        }
    }

    public bool IsNotEditingBill => !IsEditingBill;
    public bool CanEditBill => SelectedBill != null;

    public bool IsEditingPaycheck {
        get => _isEditingPaycheck;
        set {
            try {
                if (SetProperty(ref _isEditingPaycheck, value)) {
                    OnPropertyChanged(nameof(IsNotEditingPaycheck));
                    OnPropertyChanged(nameof(CanEditPaycheck));
                    EditPaycheckCommand.NotifyCanExecuteChanged();
                    CancelPaycheckCommand.NotifyCanExecuteChanged();
                    SavePaycheckCommand.NotifyCanExecuteChanged();
                    DeletePaycheckCommand.NotifyCanExecuteChanged();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting IsEditingPaycheck in MainViewModel.");
            }
        }
    }

    public bool IsNotEditingPaycheck => !IsEditingPaycheck;

    public bool CanEditPaycheck => SelectedPaycheck != null;

    public bool IsEditingPeriodBucket {
        get => _isEditingPeriodBucket;
        set {
            try {
                if (SetProperty(ref _isEditingPeriodBucket, value)) {
                    OnPropertyChanged(nameof(IsNotEditingPeriodBucket));
                    OnPropertyChanged(nameof(CanEditPeriodBucket));
                    EditPeriodBucketCommand.NotifyCanExecuteChanged();
                    CancelPeriodBucketCommand.NotifyCanExecuteChanged();
                    SavePeriodBucketCommand.NotifyCanExecuteChanged();
                    DeletePeriodBucketCommand.NotifyCanExecuteChanged();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting IsEditingPeriodBucket in MainViewModel.");
            }
        }
    }

    public bool IsEditingBucket {
        get => _isEditingBucket;
        set {
            try {
                if (SetProperty(ref _isEditingBucket, value)) {
                    OnPropertyChanged(nameof(IsNotEditingBucket));
                    OnPropertyChanged(nameof(CanEditBucket));
                    AddBucketCommand.NotifyCanExecuteChanged();
                    EditBucketCommand.NotifyCanExecuteChanged();
                    CancelBucketCommand.NotifyCanExecuteChanged();
                    SaveBucketCommand.NotifyCanExecuteChanged();
                    DeleteBucketCommand.NotifyCanExecuteChanged();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting IsEditingBucket in MainViewModel.");
            }
        }
    }

    public bool IsNotEditingBucket => !IsEditingBucket;

    public bool CanEditBucket => SelectedBucket != null;

    public bool IsNotEditingPeriodBill => !IsEditingPeriodBill;

    public bool IsEditingPeriodBill {
        get => _isEditingPeriodBill;
        set {
            try {
                if (SetProperty(ref _isEditingPeriodBill, value)) {
                    OnPropertyChanged(nameof(IsNotEditingPeriodBill));
                    OnPropertyChanged(nameof(CanEditPeriodBill));
                    EditPeriodBillCommand.NotifyCanExecuteChanged();
                    CancelPeriodBillCommand.NotifyCanExecuteChanged();
                    SavePeriodBillCommand.NotifyCanExecuteChanged();
                    DeletePeriodBillCommand.NotifyCanExecuteChanged();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting IsEditingPeriodBill in MainViewModel.");
            }
        }
    }

    public bool CanEditPeriodBill => SelectedPeriodBill != null;

    public bool IsNotEditingPeriodBucket => !IsEditingPeriodBucket;

    public bool CanEditPeriodBucket => SelectedPeriodBucket != null;

    public bool IsEditingAccount {
        get => _isEditingAccount;
        set {
            try {
                if (SetProperty(ref _isEditingAccount, value)) {
                    OnPropertyChanged(nameof(IsNotEditingAccount));
                    OnPropertyChanged(nameof(CanEditAccount));
                    AddAccountCommand.NotifyCanExecuteChanged();
                    EditAccountCommand.NotifyCanExecuteChanged();
                    CancelAccountCommand.NotifyCanExecuteChanged();
                    SaveAccountCommand.NotifyCanExecuteChanged();
                    DeleteAccountCommand.NotifyCanExecuteChanged();
                    ImportAccountCommand.NotifyCanExecuteChanged();
                    ReconcileAccountCommand.NotifyCanExecuteChanged();
                    SetAccountAprRatesCommand.NotifyCanExecuteChanged();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting IsEditingAccount in MainViewModel.");
            }
        }
    }

    public bool IsNotEditingAccount => !IsEditingAccount;
    public bool CanEditAccount => SelectedAccount != null;

    public bool IsEditingTransaction {
        get => _isEditingTransaction;
        set {
            try {
                if (SetProperty(ref _isEditingTransaction, value)) {
                    OnPropertyChanged(nameof(IsNotEditingTransaction));
                    OnPropertyChanged(nameof(CanEditTransaction));
                    AddTransactionCommand.NotifyCanExecuteChanged();
                    EditTransactionCommand.NotifyCanExecuteChanged();
                    CancelTransactionCommand.NotifyCanExecuteChanged();
                    SaveTransactionCommand.NotifyCanExecuteChanged();
                    DeleteTransactionCommand.NotifyCanExecuteChanged();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting IsEditingTransaction in MainViewModel.");
            }
        }
    }

    public bool IsNotEditingTransaction => !IsEditingTransaction;
    public bool CanEditTransaction => SelectedTransaction != null;

    public IEnumerable<Frequency> BillFrequencies { get; } = new[] { Frequency.Monthly, Frequency.Yearly };

    public Bill? EditingBillClone {
        get => _editingBillClone;
        set {
            try {
                if (SetProperty(ref _editingBillClone, value)) {
                    SyncEditingBillOverrides(value);
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting EditingBillClone in MainViewModel.");
            }
        }
    }

    private void SyncEditingBillOverrides(Bill? bill) {
        try {
            EditingBillOverrides.Clear();
            if (bill?.Overrides != null) {
                foreach (var kvp in bill.Overrides) {
                    EditingBillOverrides.Add(new OverrideItem {
                        MonthKey = kvp.Key,
                        Amount = kvp.Value
                    });
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error syncing editing bill overrides.");
        }
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

    private bool _isEditingTransactionEnabled = true;

    public bool IsEditingTransactionEnabled {
        get => _isEditingTransactionEnabled;
        private set => SetProperty(ref _isEditingTransactionEnabled, value);
    }

    private bool _isEditingAccountEnabled;

    public bool IsEditingAccountEnabled {
        get => _isEditingAccountEnabled;
        private set => SetProperty(ref _isEditingAccountEnabled, value);
    }

    private bool _isEditingBillEnabled;

    public bool IsEditingBillEnabled {
        get => _isEditingBillEnabled;
        private set => SetProperty(ref _isEditingBillEnabled, value);
    }

    private bool _isEditingBucketEnabled;

    public bool IsEditingBucketEnabled {
        get => _isEditingBucketEnabled;
        private set => SetProperty(ref _isEditingBucketEnabled, value);
    }

    private bool _isEditingPaycheckEnabled;

    public bool IsEditingPaycheckEnabled {
        get => _isEditingPaycheckEnabled;
        private set => SetProperty(ref _isEditingPaycheckEnabled, value);
    }

    private bool _isEditingCategoryEnabled;

    public bool IsEditingCategoryEnabled {
        get => _isEditingCategoryEnabled;
        private set => SetProperty(ref _isEditingCategoryEnabled, value);
    }


    public RangeObservableCollection<Account> TransactionAccounts { get; } = new();

    public RangeObservableCollection<Account> TransactionToAccounts { get; } = new();

    public Paycheck? EditingPaycheckClone {
        get => _editingPaycheckClone;
        set => SetProperty(ref _editingPaycheckClone, value);
    }

    #endregion

    #region Commands

    public IRelayCommand ToggleFlyoutCommand { get; } = null!;
    public IRelayCommand AddBillCommand { get; } = null!;

    public IRelayCommand EditBillCommand { get; } = null!;
    public IAsyncRelayCommand SaveBillCommand { get; } = null!;

    public IRelayCommand CancelBillCommand { get; } = null!;

    public IAsyncRelayCommand DeleteBillCommand { get; } = null!;

    public IRelayCommand EditPeriodBillCommand { get; } = null!;

    public IAsyncRelayCommand SavePeriodBillCommand { get; } = null!;

    public IRelayCommand CancelPeriodBillCommand { get; } = null!;

    public IAsyncRelayCommand DeletePeriodBillCommand { get; } = null!;

    public IRelayCommand AddBucketCommand { get; } = null!;

    public IRelayCommand EditBucketCommand { get; } = null!;
    public IAsyncRelayCommand SaveBucketCommand { get; } = null!;

    public IRelayCommand CancelBucketCommand { get; } = null!;

    public IAsyncRelayCommand DeleteBucketCommand { get; } = null!;

    public IRelayCommand EditPeriodBucketCommand { get; } = null!;

    public IAsyncRelayCommand SavePeriodBucketCommand { get; } = null!;

    public IRelayCommand CancelPeriodBucketCommand { get; } = null!;

    public IAsyncRelayCommand DeletePeriodBucketCommand { get; } = null!;

    public IRelayCommand AddTransactionCommand { get; } = null!;

    public IRelayCommand EditTransactionCommand { get; } = null!;

    public IAsyncRelayCommand SaveTransactionCommand { get; } = null!;

    public IRelayCommand CancelTransactionCommand { get; } = null!;

    public IAsyncRelayCommand DeleteTransactionCommand { get; } = null!;

    public IRelayCommand AddPaycheckCommand { get; } = null!;

    public IRelayCommand EditPaycheckCommand { get; } = null!;
    public IAsyncRelayCommand SavePaycheckCommand { get; } = null!;

    public IRelayCommand CancelPaycheckCommand { get; } = null!;

    public IAsyncRelayCommand DeletePaycheckCommand { get; } = null!;

    public IRelayCommand AddAccountCommand { get; } = null!;

    public IRelayCommand EditAccountCommand { get; } = null!;

    public IAsyncRelayCommand ReconcileAccountCommand { get; } = null!;

    public IAsyncRelayCommand ImportAccountCommand { get; } = null!;

    public IAsyncRelayCommand SetAccountAprRatesCommand { get; } = null!;

    public IAsyncRelayCommand SaveAccountCommand { get; } = null!;

    public IRelayCommand CancelAccountCommand { get; } = null!;

    public IAsyncRelayCommand DeleteAccountCommand { get; } = null!;

    public IAsyncRelayCommand NextPeriodCommand { get; } = null!;

    public IAsyncRelayCommand PrevPeriodCommand { get; } = null!;

    public IRelayCommand ShowAmortizationCommand { get; } = null!;

    public IRelayCommand ShowAboutCommand { get; } = null!;

    public IRelayCommand SetThemeCommand { get; } = null!;

    public IRelayCommand ExitCommand { get; } = null!;

    public IRelayCommand BackupCommand { get; } = null!;

    public IRelayCommand SetOneYearCommand { get; } = null!;

    public IRelayCommand SetFiveYearCommand { get; } = null!;

    public IRelayCommand SetTenYearCommand { get; } = null!;

    public IRelayCommand SetThirtyYearCommand { get; } = null!;

    public IRelayCommand MapsToBillCommand { get; } = null!;

    public IRelayCommand MapsToBucketCommand { get; } = null!;

    public IRelayCommand ToggleBucketDescriptionCommand { get; } = null!;

    public IRelayCommand ToggleBillDescriptionCommand { get; } = null!;

    private void SetProjectionEndDate(int years) {
        try {
            YearsProjecting = years;
            ProjectionEndDate = DateTime.Now.AddYears(years);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error setting projection end date.");
        }
    }

    #endregion

    #region Snowball Overlay Support

    private bool _isManageExclusionsOpen;

    public bool IsManageExclusionsOpen {
        get => _isManageExclusionsOpen;
        set => SetProperty(ref _isManageExclusionsOpen, value);
    }

    public RangeObservableCollection<Account> ExcludableAccounts { get; } = new();

    private void OnAccountsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
        try {
            RefreshExcludableAccounts();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling OnAccountsCollectionChanged.");
        }
    }

    public void RefreshExcludableAccounts() {
        try {
            var filtered = (Accounts
                .Where(a => a.IsLiability || a.Type is AccountType.Brokerage
                    or AccountType.Investment
                    or AccountType.IRA
                    or AccountType.RothIRA)
                .Where(a => !a.IsArchived)).ToList();

            var temp = new List<Account>(filtered.Count);

            foreach (var account in filtered) {
                account.IsExcludedInSnowball = SnowballOptions.ExcludedAccountIds.Contains(account.Id);
                temp.Add(account);
            }

            ExcludableAccounts.Clear();
            ExcludableAccounts.AddRange(temp);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error refreshing excludable accounts.");
        }
    }

    public IRelayCommand OpenManageExcludedAccountsCommand { get; } = null!;
    public IRelayCommand CloseManageExcludedAccountsCommand { get; } = null!;
    public IRelayCommand ToggleAccountExclusionCommand { get; } = null!;

    private void OpenManageExcludedAccounts() {
        try {
            RefreshExcludableAccounts();
            IsManageExclusionsOpen = true;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error opening manage excluded accounts dialog.");
        }
    }

    private void CloseManageExcludedAccounts() {
        try {
            IsManageExclusionsOpen = false;
            OnPropertyChanged(nameof(SnowballOptions));
        }
        catch (Exception ex) {
            Log.Error(ex, "Error closing manage excluded accounts dialog.");
        }
    }

    private void ToggleAccountExclusion(int accountId) {
        try {
            if (SnowballOptions.ExcludedAccountIds.Contains(accountId)) {
                SnowballOptions.ExcludedAccountIds.Remove(accountId);
            }
            else {
                SnowballOptions.ExcludedAccountIds.Add(accountId);
            }

            var acc = ExcludableAccounts.FirstOrDefault(a => a.Id == accountId);
            if (acc != null) {
                acc.IsExcludedInSnowball = SnowballOptions.ExcludedAccountIds.Contains(accountId);
            }

            OnSnowballOptionsPropertyChanged(
                SnowballOptions,
                new PropertyChangedEventArgs(nameof(SnowballStrategyOptions.ExcludedAccountIds))
            );
        }
        catch (Exception ex) {
            Log.Error(ex, "Error toggling account exclusion.");
        }
    }

    #endregion

    private bool _isLoadingData;
    private bool _isLoadingAccountData;
    private bool _isLoadingBillData;
    private bool _isLoadingBucketData;
    private bool _isLoadingPaycheckData;
    private bool _isLoadingSubCategoryData;
    private bool _isLoadingCategoryData;

    #region Events

    private async void Item_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (_isLoadingData) return;
        try {
            switch (sender) {
                case Bill b:
                    await _budgetService.UpsertBillAsync(b);
                    break;
                case Paycheck p: {
                    await _budgetService.UpsertPaycheckAsync(p);
                    RefreshPaychecks();
                    if (p.Id == _selectedPeriodPaycheckId) {
                        OnPropertyChanged(nameof(SelectedPeriodPaycheckId));


                        await LoadPeriodDataAsync();
                    }

                    break;
                }
                case Account a:
                    await _budgetService.UpsertAccountAsync(a);
                    break;
                case BudgetBucket bb:
                    await _budgetService.UpsertBucketAsync(bb, null);
                    break;
            }

            RequestProjectionRecalculation();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in Item_PropertyChanged for {SenderType}.", sender?.GetType().Name);
        }
    }

    #endregion

    #region Bill CRUD

    #region Overrides

    public ObservableCollection<OverrideItem> EditingBillOverrides { get; } = new();

    private void SyncBillOverridesToClone() {
        try {
            if (EditingBillClone == null) return;

            EditingBillClone.Overrides = EditingBillOverrides
                .Where(x => !string.IsNullOrWhiteSpace(x.MonthKey))
                .ToDictionary(x => x.MonthKey.Trim(), x => x.Amount);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error syncing bill overrides to clone.");
        }
    }

    private void AddBillOverride() {
        try {
            var existingKeys = EditingBillOverrides.Select(x => x.MonthKey).ToHashSet();
            var defaultMonth = MonthOptions.FirstOrDefault(m => !existingKeys.Contains(m.Key)) ?? MonthOptions.First();

            EditingBillOverrides.Add(new OverrideItem {
                MonthKey = defaultMonth.Key,
                Amount = EditingBillClone?.ExpectedAmount ?? 0m
            });
        }
        catch (Exception ex) {
            Log.Error(ex, "Error adding bill override.");
        }
    }

    private void RemoveBillOverride(OverrideItem? item) {
        try {
            if (item != null) {
                EditingBillOverrides.Remove(item);
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error removing bill override.");
        }
    }

    #endregion

    private void AddAnotherBill() {
        if (EditingBillClone == null) return;

        // Validate current item before staging
        bool isValid = EditingBillClone.ValidateAllProperties();
        List<string> errors = EditingBillClone.GetValidationErrors();
        if (!isValid || errors.Any()) {
            // Force WPF to re-evaluate all bindings hanging off EditingTransactionClone
            OnPropertyChanged(nameof(EditingBillClone));

            MessageBox.Show(errors.FirstOrDefault() ?? "Please correct the highlighted errors before saving."
                , "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(EditingBillClone.Name)) {
            MessageBox.Show("Please enter a name for the bill.", "Validation Error", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Clean zero IDs
        if (EditingBillClone.AccountId == 0) EditingBillClone.AccountId = null;
        if (EditingBillClone.ToAccountId == 0) EditingBillClone.ToAccountId = null;
        if (EditingBillClone.BucketId == 0) EditingBillClone.BucketId = null;
        if (EditingBillClone.SubCategoryId == 0) EditingBillClone.SubCategoryId = null;

        // Stage a clone of the filled form
        StagedBills.Add(EditingBillClone.Clone());

        // Prepare a clean fresh instance for the next entry
        if (EditingBillClone != null) {
            EditingBillClone.PropertyChanged -= EditingBillClone_PropertyChanged;
        }

        var nextTrans = new Bill { Name = "", ExpectedAmount = 0, DueDay = 1, IsActive = true };

        EditingBillClone = nextTrans;
        EditingBillClone.PropertyChanged += EditingBillClone_PropertyChanged;

        OnPropertyChanged(nameof(TotalBillItemsToSaveCount));
        OnPropertyChanged(nameof(CanSaveBill));
        SaveBillCommand.NotifyCanExecuteChanged();
    }

    private void RemoveStagedBill(Bill? item) {
        if (item != null && StagedBills.Contains(item)) {
            StagedBills.Remove(item);

            OnPropertyChanged(nameof(TotalBillItemsToSaveCount));
            OnPropertyChanged(nameof(CanSaveBill));
            SaveBillCommand.NotifyCanExecuteChanged();
        }
    }

    private void ClearCurrentBillDraft() {
        if (EditingBillClone == null) return;

        // Reset fields on the current active edit form without closing overlay
        EditingBillClone.Name = string.Empty;
        EditingBillClone.ExpectedAmount = 0;
        EditingBillClone.BucketId = null;
        EditingBillClone.SubCategoryId = null;
    }

    private void AddBill() {
        try {
            if (EditingBillClone != null) {
                EditingBillClone.PropertyChanged -= EditingBillClone_PropertyChanged;
            }

            EditingBillClone = new Bill { Name = "", ExpectedAmount = 0, DueDay = 1, IsActive = true };

            EditingBillClone.PropertyChanged += EditingBillClone_PropertyChanged;

            SelectedBill = null;
            IsEditingBill = true;
            IsEditingBillEnabled = true;

            AddAnotherBillCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error initializing new bill.");
        }
    }

    private void EditBill() {
        try {
            CancelBill();
            if (SelectedBill == null) return;
            if (EditingBillClone != null) {
                EditingBillClone.PropertyChanged -= EditingBillClone_PropertyChanged;
            }

            EditingBillClone = new Bill {
                Id = SelectedBill.Id, Name = SelectedBill.Name, ExpectedAmount = SelectedBill.ExpectedAmount,
                Frequency = SelectedBill.Frequency, DueDay = SelectedBill.DueDay, AccountId = SelectedBill.AccountId,
                ToAccountId = SelectedBill.ToAccountId, NextDueDate = SelectedBill.NextDueDate,
                IsPrincipalOnly = SelectedBill.IsPrincipalOnly,
                Category = SelectedBill.Category, IsActive = SelectedBill.IsActive,
                IsArchived = SelectedBill.IsArchived,
                BucketId = SelectedBill.BucketId,
                SubCategoryId = SelectedBill.SubCategoryId,
                Overrides = new Dictionary<string, decimal>(SelectedBill.Overrides)
            };
            EditingBillClone.PropertyChanged += EditingBillClone_PropertyChanged;

            IsEditingBill = true;
            IsEditingBillEnabled = true;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error entering edit mode for bill.");
        }
    }

    public int TotalBillItemsToSaveCount {
        get {
            int stagedCount = StagedBills?.Count ?? 0;
            bool hasActiveDraft = EditingBillClone != null &&
                                  !string.IsNullOrWhiteSpace(EditingBillClone.Name);
            return stagedCount + (hasActiveDraft ? 1 : 0);
        }
    }

    public bool CanSaveBill => IsEditingBill &&
                               IsEditingBillEnabled &&
                               TotalBillItemsToSaveCount > 0;

    private async Task SaveBillAsync() {
        if (EditingBillClone == null && !StagedBills.Any()) return;

        try {
            // 1. Stage current draft if valid
            if (EditingBillClone != null) {
                bool isValid = EditingBillClone.ValidateAllProperties();

                List<string> errors = EditingBillClone.GetValidationErrors();
                if (!isValid || errors.Any()) {
                    // Force WPF to re-evaluate all bindings hanging off EditingBillClone
                    OnPropertyChanged(nameof(EditingBillClone));

                    MessageBox.Show(errors.FirstOrDefault() ?? "Please correct the highlighted errors before saving."
                        , "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (EditingBillClone.Frequency == Frequency.Monthly) {
                    if (EditingBillClone.DueDay < 1 || EditingBillClone.DueDay > 31) {
                        MessageBox.Show("Due Day must be between 1 and 31 for Monthly bills.", "Validation Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    EditingBillClone.NextDueDate = null;
                }
                else if (EditingBillClone.Frequency == Frequency.Yearly) {
                    if (EditingBillClone.NextDueDate == null) {
                        MessageBox.Show("Next Due Date is required for Yearly bills.", "Validation Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    EditingBillClone.DueDay = 0;
                }

                if (EditingBillClone.AccountId == 0) EditingBillClone.AccountId = null;
                if (EditingBillClone.ToAccountId == 0) EditingBillClone.ToAccountId = null;

                SyncBillOverridesToClone();
                StagedBills.Add(EditingBillClone);

                OnPropertyChanged(nameof(TotalBillItemsToSaveCount));
                OnPropertyChanged(nameof(CanSaveBill));
                SaveBillCommand.NotifyCanExecuteChanged();
            }

            if (!StagedBills.Any()) {
                MessageBox.Show("Please enter a name for the bill.", "Validation Error", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // 2. Persist all staged items
            foreach (var item in StagedBills) {
                if (SelectedBill != null && item.Id == SelectedBill.Id) {
                    UpdateBillFromClone(SelectedBill, item);
                    await _budgetService.UpsertBillAsync(SelectedBill);
                }
                else {
                    await _budgetService.UpsertBillAsync(item);
                }
            }

            int countSaved = StagedBills.Count;

            // 3. Reset editor state & clear staged collection
            if (EditingBillClone != null) {
                EditingBillClone.PropertyChanged -= EditingBillClone_PropertyChanged;
            }

            IsEditingBill = false;
            EditingBillClone = null;
            IsEditingBillEnabled = false;
            StagedBills.Clear();

            OnPropertyChanged(nameof(TotalBillItemsToSaveCount));
            OnPropertyChanged(nameof(CanSaveBill));
            SaveBillCommand.NotifyCanExecuteChanged();

            // 4. Refresh UI once
            await LoadBillDataAsync();
            await LoadPeriodDataAsync();
            RequestProjectionRecalculation();

            ShowSuccessToast($"Successfully saved {countSaved} bill{(countSaved > 1 ? "s" : "")}.");
        }
        catch (Exception ex) {
            Log.Error(ex, "Error saving batch bills.");
            MessageBox.Show("Failed to save bills. See log for details.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void UpdateBillFromClone(Bill target, Bill clone) {
        try {
            target.Name = clone.Name;
            target.ExpectedAmount = clone.ExpectedAmount;
            target.Frequency = clone.Frequency;
            target.DueDay = clone.DueDay;
            target.AccountId = (clone.AccountId == 0 || clone.AccountId == null) ? null : clone.AccountId;
            target.ToAccountId = (clone.ToAccountId == 0 || clone.ToAccountId == null) ? null : clone.ToAccountId;
            target.NextDueDate = clone.NextDueDate;
            target.Category = clone.Category;
            target.IsActive = clone.IsActive;
            target.IsPrincipalOnly = clone.IsPrincipalOnly;
            target.BucketId = clone.BucketId;
            target.SubCategoryId = clone.SubCategoryId;
            target.Overrides = new Dictionary<string, decimal>(clone.Overrides);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error updating bill from clone.");
        }
    }

    private void CancelBill() {
        try {
            if (EditingBillClone != null) {
                EditingBillClone.PropertyChanged -= EditingBillClone_PropertyChanged;
            }

            IsEditingBill = false;
            IsEditingBillEnabled = false;
            EditingBillClone = null;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error cancelling bill edit.");
        }
    }

    private async Task DeletePeriodBillAsync() {
        if (EditingPeriodBillClone == null) return;
        var messageBoxResult = MessageBox.Show(
            "Are you sure you want to delete this period's bill?",
            "Delete Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );

        if (messageBoxResult == MessageBoxResult.Yes) {
            try {
                await _budgetService.DeletePeriodBillAsync(EditingPeriodBillClone.Id);
                IsEditingPeriodBill = false;
                EditingPeriodBillClone = null;
                await LoadPeriodDataAsync();
                RequestProjectionRecalculation();
            }
            catch (Exception ex) {
                Log.Error(ex, "Error deleting period bill.");

                MessageBox.Show("Failed to delete period bill. See log for details.", "Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private void EditPeriodBill() {
        try {
            CancelPeriodBill();
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

    private async Task SavePeriodBillAsync() {
        if (EditingPeriodBillClone == null) return;
        try {
            if (SelectedPeriodBill != null) {
                UpdatePeriodBillFromClone(SelectedPeriodBill, EditingPeriodBillClone);
                await _budgetService.UpsertPeriodBillAsync(EditingPeriodBillClone);
            }
            else {
                await _budgetService.UpsertPeriodBillAsync(EditingPeriodBillClone);
            }

            var selectedPeriodBillId = SelectedPeriodBill?.Id;

            IsEditingPeriodBill = false;
            EditingPeriodBillClone = null;

            await LoadBillDataAsync();
            await LoadPeriodDataAsync();

            if (selectedPeriodBillId.HasValue) {
                SelectedPeriodBill = CurrentPeriodBills.FirstOrDefault(a => a.Id == selectedPeriodBillId);
            }

            RequestProjectionRecalculation();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error saving period bill.");

            MessageBox.Show("Failed to save period bill. See log for details.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
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
        try {
            target.Id = clone.Id;
            target.ActualAmount = clone.ActualAmount;
            target.DueDate = clone.DueDate;
            target.IsPaid = clone.IsPaid;
            target.TransactionAmount = clone.TransactionAmount;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error updating period bill from clone.");
        }
    }

    private async Task DeleteBillAsync() {
        if (EditingBillClone == null) return;
        var messageBoxResult = MessageBox.Show(
            "Are you sure you want to delete this bill?",
            "Delete Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );

        if (messageBoxResult == MessageBoxResult.Yes) {
            try {
                // Pre-delete safety backup
                _budgetService.CreateRollingBackup(BackupReason.PreDeleteBill);

                await _budgetService.DeleteBillAsync(EditingBillClone.Id);
                IsEditingBill = false;
                IsEditingBillEnabled = false;
                EditingBillClone = null;
                await LoadBillDataAsync();
                await LoadPeriodDataAsync();
                RequestProjectionRecalculation();
            }
            catch (Exception ex) {
                Log.Error(ex, "Error deleting bill.");

                MessageBox.Show("Failed to delete bill. See log for details.", "Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private async Task DeleteSelectedBillAsync() {
        if (SelectedBill == null) return;
        var messageBoxResult = MessageBox.Show(
            $"Are you sure you want to delete this bill?\r\n\r\n{SelectedBill!.Name}",
            "Delete Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );

        if (messageBoxResult == MessageBoxResult.Yes) {
            try {
                await _budgetService.DeleteBillAsync(SelectedBill.Id);
                IsEditingBill = false;
                IsEditingBillEnabled = false;
                SelectedBill = null;
                await LoadBillDataAsync();
                await LoadPeriodDataAsync();
                RequestProjectionRecalculation();
            }
            catch (Exception ex) {
                Log.Error(ex, "Error deleting bill.");

                MessageBox.Show("Failed to delete bill. See log for details.", "Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    #endregion

    #region Bucket CRUD

    #region Overrides

    public ObservableCollection<OverrideItem> EditingBucketOverrides { get; } = new();

    private void SyncBucketOverridesToClone() {
        try {
            if (EditingBucketClone == null) return;

            EditingBucketClone.Overrides = EditingBucketOverrides
                .Where(x => !string.IsNullOrWhiteSpace(x.MonthKey))
                .ToDictionary(x => x.MonthKey.Trim(), x => x.Amount);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error syncing bucket overrides to clone.");
        }
    }

    private void AddBucketOverride() {
        try {
            var existingKeys = EditingBucketOverrides.Select(x => x.MonthKey).ToHashSet();
            var defaultMonth = MonthOptions.FirstOrDefault(m => !existingKeys.Contains(m.Key)) ?? MonthOptions.First();

            EditingBucketOverrides.Add(new OverrideItem {
                MonthKey = defaultMonth.Key,
                Amount = EditingBucketClone?.ExpectedAmount ?? 0m
            });
        }
        catch (Exception ex) {
            Log.Error(ex, "Error adding bucket override.");
        }
    }

    private void RemoveBucketOverride(OverrideItem? item) {
        try {
            if (item != null) {
                EditingBucketOverrides.Remove(item);
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error removing bucket override.");
        }
    }

    #endregion

    private void AddAnotherBucket() {
        if (EditingBucketClone == null) return;

        // 1. Validate current item before staging
        bool isValid = EditingBucketClone.ValidateAllProperties();
        List<string> errors = EditingBucketClone.GetValidationErrors();
        if (!isValid || errors.Any()) {
            OnPropertyChanged(nameof(EditingBucketClone));
            MessageBox.Show(errors.FirstOrDefault() ?? "Please correct the highlighted errors before saving.",
                "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(EditingBucketClone.Name)) {
            MessageBox.Show("Please enter a name for the envelope.", "Validation Error", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Clean zero IDs
        if (EditingBucketClone.AccountId == 0) EditingBucketClone.AccountId = null;

        // 2. Stage a clone of the filled form
        StagedBuckets.Add(EditingBucketClone.Clone());

        // Unsubscribe from old clone events
        if (EditingBucketClone != null) {
            EditingBucketClone.PropertyChanged -= EditingBucketClone_PropertyChanged;
        }

        // 3. Prepare a clean fresh instance for the next entry
        var nextTrans = new BudgetBucket {
            Name = "",
            Type = BucketType.Standard,
            ExpectedAmount = 0,
            TargetBalance = 0,
            CurrentBalance = 0,
            TargetFrequency = TargetFrequencyType.PaycheckFrequency,
            TargetAmount = 0,
            NextDueDate = null
        };

        EditingBucketClone = nextTrans;
        EditingBucketClone.PropertyChanged += EditingBucketClone_PropertyChanged;

        // 4. Reset Paycheck Allocations & Subcategories for the new draft
        EditableAllocations.Clear();
        var defaultAllocations = Paychecks
            .Where(p => p.EndDate == null || p.EndDate > DateTime.Now)
            .Select(p => new BucketPaycheckAllocation {
                PaycheckId = p.Id,
                PaycheckName = p.Name,
                AllocationValue = 0m,
                IsActive = true,
                CreatedDate = DateTime.Today,
                SortOrder = 0
            })
            .ToList();

        EditableAllocations.ReplaceRange(defaultAllocations);
        PopulateEditableSubCategories(bucketId: null);

        // 5. Notify UI Commands
        OnPropertyChanged(nameof(TotalBucketItemsToSaveCount));
        OnPropertyChanged(nameof(CanSaveBucket));
        SaveBucketCommand.NotifyCanExecuteChanged();
    }

    private void RemoveStagedBucket(BudgetBucket? item) {
        if (item != null && StagedBuckets.Contains(item)) {
            StagedBuckets.Remove(item);

            OnPropertyChanged(nameof(TotalBucketItemsToSaveCount));
            OnPropertyChanged(nameof(CanSaveBucket));
            SaveBucketCommand.NotifyCanExecuteChanged();
        }
    }

    private void ClearCurrentBucketDraft() {
        if (EditingBucketClone == null) return;

        // Reset fields on the current active edit form without closing overlay
        EditingBucketClone.Name = string.Empty;
        EditingBucketClone.ExpectedAmount = 0;

        // Reset allocations to default $0 rows for active paychecks
        EditableAllocations.Clear();
        var defaultAllocations = Paychecks
            .Where(p => p.EndDate == null || p.EndDate > DateTime.Now)
            .Select(p => new BucketPaycheckAllocation {
                PaycheckId = p.Id,
                PaycheckName = p.Name,
                AllocationValue = 0m,
                IsActive = true,
                CreatedDate = DateTime.Today,
                SortOrder = 0
            })
            .ToList();

        EditableAllocations.ReplaceRange(defaultAllocations);

        // Reset selected subcategories
        PopulateEditableSubCategories(bucketId: null);
    }

    private async void AddBucket() {
        try {
            if (EditingBucketClone != null) {
                EditingBucketClone.PropertyChanged -= EditingBucketClone_PropertyChanged;
            }

            EditingBucketClone = new BudgetBucket {
                Name = "",
                Type = BucketType.Standard,
                ExpectedAmount = 0,
                TargetBalance = 0,
                CurrentBalance = 0,
                TargetFrequency = TargetFrequencyType.PaycheckFrequency,
                TargetAmount = 0,
                NextDueDate = null
            };

            EditingBucketClone.PropertyChanged += EditingBucketClone_PropertyChanged;

            EditableAllocations.Clear();
            // Populate default $0 rows for all active paychecks
            var defaultAllocations = Paychecks
                .Where(p => p.EndDate == null || p.EndDate > DateTime.Now)
                .Select(p => new BucketPaycheckAllocation {
                    PaycheckId = p.Id,
                    PaycheckName = p.Name,
                    AllocationValue = 0m,
                    IsActive = true,
                    CreatedDate = DateTime.Today,
                    SortOrder = 0
                })
                .ToList();

            EditableAllocations.ReplaceRange(defaultAllocations);

            SelectedBucket = null;
            PopulateEditableSubCategories(bucketId: null);
            IsEditingBucket = true;
            IsEditingBucketEnabled = true;
            AddAnotherBucketCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error initializing new bucket.");
        }
    }

    private async void EditBucket() {
        try {
            CancelBucket();
            if (SelectedBucket == null) return;
            if (EditingBucketClone != null) {
                EditingBucketClone.PropertyChanged -= EditingBucketClone_PropertyChanged;
            }

            EditingBucketClone = new BudgetBucket {
                Id = SelectedBucket.Id,
                Name = SelectedBucket.Name,
                Type = SelectedBucket.Type,
                ExpectedAmount = SelectedBucket.ExpectedAmount,
                TargetBalance = SelectedBucket.TargetBalance,
                CurrentBalance = SelectedBucket.CurrentBalance,
                InitialBalance = SelectedBucket.InitialBalance,
                AccountId = SelectedBucket.AccountId,
                IsArchived = SelectedBucket.IsArchived,
                TargetFrequency = SelectedBucket.TargetFrequency,
                TargetAmount = SelectedBucket.TargetAmount,
                NextDueDate = SelectedBucket.NextDueDate
            };

            var allocations = await _budgetService.GetAllocationsForBucketAsync(SelectedBucket.Id);
            EditableAllocations.ReplaceRange(allocations);
            PopulateEditableSubCategories(SelectedBucket.Id);

            EditingBucketClone.PropertyChanged += EditingBucketClone_PropertyChanged;

            IsEditingBucket = true;
            IsEditingBucketEnabled = true;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error entering edit mode for bucket.");
        }
    }

    private void PopulateEditableSubCategories(int? bucketId) {
        try {
            var items = new List<SelectableSubCategory>();

            foreach (var subCat in SubCategories) {
                var isCurrentlyAssignedToThis = EditingBucketClone != null
                                                && subCat.DefaultBucketId == EditingBucketClone.Id;

                string? currentBucketName = null;
                if (subCat.DefaultBucketId.HasValue) {
                    currentBucketName = Buckets
                        .FirstOrDefault(b => b.Id == subCat.DefaultBucketId.Value)?.Name;
                }

                var item = new SelectableSubCategory {
                    Id = subCat.Id,
                    Name = subCat.Name,
                    CategoryName = subCat.CategoryName ?? "",
                    CurrentBucketId = subCat.DefaultBucketId,
                    CurrentBucketName = currentBucketName,
                    EditingBucketId = EditingBucketClone?.Id ?? 0,
                    IsSelected = isCurrentlyAssignedToThis
                };

                items.Add(item);
            }

            EditableSubCategories.ReplaceRange(items);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error populating editable subcategories.");
        }
    }

    public int TotalBucketItemsToSaveCount {
        get {
            int stagedCount = StagedBuckets?.Count ?? 0;
            bool hasActiveDraft = EditingBucketClone != null &&
                                  !string.IsNullOrWhiteSpace(EditingBucketClone.Name);
            return stagedCount + (hasActiveDraft ? 1 : 0);
        }
    }

    public bool CanSaveBucket => IsEditingBucket &&
                                 IsEditingBucketEnabled &&
                                 TotalBucketItemsToSaveCount > 0;

    private async Task SaveBucketAsync() {
        if (EditingBucketClone == null && !StagedBuckets.Any()) return;

        try {
            // Strip out placeholder, unassigned, or zero-ID paycheck allocations
            var validAllocations = EditableAllocations
                .Where(a => a.PaycheckId > 0 && a.AllocationValue >= 0)
                .ToList();
            if (Paychecks.Count < 2) {
                validAllocations.Clear();
            }

            // 1. Stage current draft if valid
            if (EditingBucketClone != null && !string.IsNullOrWhiteSpace(EditingBucketClone.Name)) {
                bool isValid = EditingBucketClone.ValidateAllProperties();

                List<string> errors = EditingBucketClone.GetValidationErrors();
                if (!isValid || errors.Any()) {
                    OnPropertyChanged(nameof(EditingBucketClone));
                    MessageBox.Show(errors.FirstOrDefault() ?? "Please correct the highlighted errors before saving.",
                        "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (EditingBucketClone.AccountId == 0) EditingBucketClone.AccountId = null;

                NormalizeBucketTypeRules(EditingBucketClone);
                SyncBucketOverridesToClone();
                StagedBuckets.Add(EditingBucketClone);

                OnPropertyChanged(nameof(TotalBucketItemsToSaveCount));
                OnPropertyChanged(nameof(CanSaveBucket));
                SaveBucketCommand.NotifyCanExecuteChanged();
            }

            if (!StagedBuckets.Any()) {
                MessageBox.Show("Please enter a name for the envelope.", "Validation Error", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var selectedSubCategoryIds = EditableSubCategories
                .Where(x => x.IsSelected)
                .Select(x => x.Id)
                .ToList();

            // 2. Persist all staged items
            foreach (var item in StagedBuckets) {
                int savedBucketId;
                if (SelectedBucket != null && item.Id == SelectedBucket.Id) {
                    UpdateBucketFromClone(SelectedBucket, item);
                    SelectedBucket.InitialBalance = SelectedBucket.CurrentBalance;
                    await _budgetService.UpsertBucketAsync(SelectedBucket, selectedSubCategoryIds);
                    savedBucketId = SelectedBucket.Id;
                }
                else {
                    await _budgetService.UpsertBucketAsync(item, selectedSubCategoryIds);
                    savedBucketId = item.Id;
                }

                if (item.Type != BucketType.UpfrontFloor) {
                    // Pass cleaned allocations without 0-ID paycheck placeholders
                    await _budgetService.SaveBucketPaycheckAllocationsAsync(savedBucketId, item.Type, validAllocations);
                }

                await _budgetService.RecalculateBucketBalanceAsync(savedBucketId);
            }

            int countSaved = StagedBuckets.Count;

            // 3. Reset editor state & clear staged collection
            if (EditingBucketClone != null) {
                EditingBucketClone.PropertyChanged -= EditingBucketClone_PropertyChanged;
            }

            IsEditingBucket = false;
            IsEditingBucketEnabled = false;
            EditingBucketClone = null;
            EditableAllocations.Clear();
            StagedBuckets.Clear();

            OnPropertyChanged(nameof(TotalBucketItemsToSaveCount));
            OnPropertyChanged(nameof(CanSaveBucket));
            SaveBucketCommand.NotifyCanExecuteChanged();

            // 4. Refresh UI once
            await LoadBucketDataAsync();
            await LoadPeriodDataAsync();
            RequestProjectionRecalculation();

            ShowSuccessToast($"Successfully saved {countSaved} envelope{(countSaved > 1 ? "s" : "")}.");
        }
        catch (Exception ex) {
            Log.Error(ex, "Error saving batch buckets.");
            MessageBox.Show("Failed to save envelopes. See log for details.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void NormalizeBucketTypeRules(BudgetBucket bucket) {
        try {
            switch (bucket.Type) {
                case BucketType.UpfrontFloor:
                    bucket.ExpectedAmount = 0;
                    bucket.CurrentBalance = 0;
                    bucket.TargetFrequency = null;
                    bucket.NextDueDate = null;
                    EditableAllocations.Clear();
                    break;

                case BucketType.Standard:
                    bucket.TargetBalance = 0;
                    bucket.CurrentBalance = 0;
                    bucket.TargetFrequency ??= TargetFrequencyType.PaycheckFrequency;
                    break;

                case BucketType.AccumulatingDrawdown:
                    if (bucket.TargetBalance < 0) bucket.TargetBalance = 0;
                    if (bucket.Id <= 0) bucket.InitialBalance = bucket.CurrentBalance;
                    bucket.TargetFrequency ??= TargetFrequencyType.PaycheckFrequency;
                    break;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error normalizing bucket type rules.");
        }
    }

    private void UpdateBucketFromClone(BudgetBucket target, BudgetBucket clone) {
        try {
            target.Name = clone.Name;
            target.Type = clone.Type;
            target.ExpectedAmount = clone.ExpectedAmount;
            target.TargetBalance = clone.TargetBalance;
            target.CurrentBalance = clone.CurrentBalance;
            target.AccountId = clone.AccountId;
            target.TargetFrequency = clone.TargetFrequency;
            target.TargetAmount = clone.TargetAmount;
            target.NextDueDate = clone.NextDueDate;
            target.Overrides = clone.Overrides;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error updating bucket from clone.");
        }
    }

    private void CancelBucket() {
        try {
            if (EditingBucketClone != null) {
                EditingBucketClone.PropertyChanged -= EditingBucketClone_PropertyChanged;
            }

            IsEditingBucket = false;
            IsEditingBucketEnabled = false;
            EditingBucketClone = null;
            EditableAllocations.Clear();
            EditableSubCategories.Clear();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error cancelling bucket edit.");
        }
    }

    private async Task DeleteBucketAsync() {
        if (EditingBucketClone == null) return;
        var messageBoxResult = MessageBox.Show(
            "Are you sure you want to delete this bucket?",
            "Delete Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );

        if (messageBoxResult == MessageBoxResult.Yes) {
            try {
                // Pre-delete safety backup
                _budgetService.CreateRollingBackup(BackupReason.PreDeleteBucket);

                await _budgetService.DeleteBucketAsync(EditingBucketClone.Id);
                IsEditingBucket = false;
                IsEditingBucketEnabled = false;
                EditingBucketClone = null;
                EditableAllocations.Clear();

                await LoadBucketDataAsync();
                await LoadPeriodDataAsync();
                await LoadSubCategoryDataAsync();
                await LoadCategoryDataAsync();
                RequestProjectionRecalculation();
            }
            catch (Exception ex) {
                Log.Error(ex, "Error deleting bucket.");

                MessageBox.Show("Failed to delete bucket. See log for details.", "Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private async Task DeleteSelectedBucketAsync() {
        if (SelectedBucket == null) return;
        var messageBoxResult = MessageBox.Show(
            $"Are you sure you want to delete this envelope?\r\n\r\n{SelectedBucket!.Name}",
            "Delete Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );

        if (messageBoxResult == MessageBoxResult.Yes) {
            try {
                await _budgetService.DeleteBucketAsync(SelectedBucket.Id);
                IsEditingBucket = false;
                IsEditingBucketEnabled = false;
                SelectedBucket = null;
                EditableAllocations.Clear();

                await LoadBucketDataAsync();
                await LoadPeriodDataAsync();
                await LoadSubCategoryDataAsync();
                await LoadCategoryDataAsync();
                RequestProjectionRecalculation();
            }
            catch (Exception ex) {
                Log.Error(ex, "Error deleting bucket.");

                MessageBox.Show("Failed to delete bucket. See log for details.", "Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private void EditPeriodBucket() {
        try {
            CancelPeriodBucket();
            if (SelectedPeriodBucket == null) return;
            EditingPeriodBucketClone = new PeriodBucket {
                Id = SelectedPeriodBucket.Id,
                BucketName = SelectedPeriodBucket.BucketName,
                ActualAmount = SelectedPeriodBucket.ActualAmount,
                BucketId = SelectedPeriodBucket.BucketId,
                FitId = SelectedPeriodBucket.FitId,
                PeriodDate = SelectedPeriodBucket.PeriodDate,
                IsPaid = SelectedPeriodBucket.IsPaid,
                BucketType = SelectedPeriodBucket.BucketType
            };
            IsEditingPeriodBucket = true;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error entering edit mode for period bucket.");
        }
    }

    private async Task SavePeriodBucketAsync() {
        if (EditingPeriodBucketClone == null) return;

        try {
            if (SelectedPeriodBucket != null) {
                UpdatePeriodBucketFromClone(SelectedPeriodBucket, EditingPeriodBucketClone);
                await _budgetService.UpsertPeriodBucketAsync(EditingPeriodBucketClone);
            }
            else {
                await _budgetService.UpsertPeriodBucketAsync(EditingPeriodBucketClone);
            }

            var selectedPeriodBucketId = SelectedPeriodBucket?.Id;

            await _budgetService.RecalculateBucketBalanceAsync(EditingPeriodBucketClone.BucketId);

            IsEditingPeriodBucket = false;
            EditingPeriodBucketClone = null;

            await LoadBucketDataAsync();
            await LoadPeriodDataAsync();

            if (selectedPeriodBucketId.HasValue) {
                SelectedPeriodBucket = CurrentPeriodBuckets.FirstOrDefault(a => a.Id == selectedPeriodBucketId);
            }

            RequestProjectionRecalculation();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error saving period bucket.");

            MessageBox.Show("Failed to save period bucket. See log for details.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void UpdatePeriodBucketFromClone(PeriodBucket target, PeriodBucket clone) {
        try {
            target.Id = clone.Id;
            target.BucketName = clone.BucketName;
            target.ActualAmount = clone.ActualAmount;
            target.BucketId = clone.BucketId;
            target.FitId = clone.FitId;
            target.PeriodDate = clone.PeriodDate;
            target.IsPaid = clone.IsPaid;
            target.BucketType = clone.BucketType;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error updating period bucket from clone.");
        }
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

    private async Task DeletePeriodBucketAsync() {
        if (EditingPeriodBucketClone == null || EditingPeriodBucketClone.Id == 0) return;
        var messageBoxResult = MessageBox.Show(
            "Are you sure you want to delete this period's bucket?\r\n\r\nIt will use the budgetted amount for the bucket instead. Save a $0 amount if you do not want to budget for this bucket for this period.",
            "Delete Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );

        if (messageBoxResult == MessageBoxResult.Yes) {
            try {
                var bucketId = EditingPeriodBucketClone.BucketId;
                await _budgetService.DeletePeriodBucketAsync(EditingPeriodBucketClone.Id);
                await _budgetService.RecalculateBucketBalanceAsync(bucketId);
                IsEditingPeriodBucket = false;
                EditingPeriodBucketClone = null;
                await LoadBucketDataAsync();
                await LoadPeriodDataAsync();
                RequestProjectionRecalculation();
            }
            catch (Exception ex) {
                Log.Error(ex, "Error deleting period bucket.");

                MessageBox.Show("Failed to delete period bucket. See log for details.", "Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    #endregion

    #region Category CRUD

    private void AddAnotherCategory() {
        bool stagedAny = false;

        // Stage Category draft
        if (EditingCategoryClone != null && !string.IsNullOrWhiteSpace(EditingCategoryClone.Name)) {
            bool isValid = EditingCategoryClone.ValidateAllProperties();
            List<string> errors = EditingCategoryClone.GetValidationErrors();
            if (!isValid || errors.Any()) {
                OnPropertyChanged(nameof(EditingCategoryClone));
                MessageBox.Show(errors.FirstOrDefault() ?? "Please correct category validation errors.",
                    "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StagedCategories.Add(EditingCategoryClone.Clone());
            stagedAny = true;
        }

        // Stage Subcategory draft
        if (EditingSubCategoryClone != null && !string.IsNullOrWhiteSpace(EditingSubCategoryClone.Name)) {
            bool isValid = EditingSubCategoryClone.ValidateAllProperties();
            List<string> errors = EditingSubCategoryClone.GetValidationErrors();
            if (!isValid || errors.Any()) {
                OnPropertyChanged(nameof(EditingSubCategoryClone));
                MessageBox.Show(errors.FirstOrDefault() ?? "Please correct subcategory validation errors.",
                    "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StageSubCategoryDraft();
            stagedAny = true;
        }

        if (!stagedAny) return;

        // Refresh dropdown choices & reset drafts
        if (EditingCategoryClone != null) EditingCategoryClone.PropertyChanged -= EditingCategoryClone_PropertyChanged;
        if (EditingSubCategoryClone != null)
            EditingSubCategoryClone.PropertyChanged -= EditingSubCategoryClone_PropertyChanged;

        EditingCategoryClone = new Category { Name = "", SortOrder = 0 };
        EditingSubCategoryClone = new SubCategory { CategoryId = 0, Name = "", SortOrder = 0 };

        EditingCategoryClone.PropertyChanged += EditingCategoryClone_PropertyChanged;
        EditingSubCategoryClone.PropertyChanged += EditingSubCategoryClone_PropertyChanged;

        OnPropertyChanged(nameof(AvailableParentCategories));
        OnPropertyChanged(nameof(TotalCategoryItemsToSaveCount));
        OnPropertyChanged(nameof(CanSaveCategory));
        OnPropertyChanged(nameof(HasStagedCategoryItems));
        SaveCategoryCommand.NotifyCanExecuteChanged();
        AddAnotherCategoryCommand.NotifyCanExecuteChanged();
    }

    private void StageSubCategoryDraft() {
        if (EditingSubCategoryClone == null || string.IsNullOrWhiteSpace(EditingSubCategoryClone.Name)) return;

        // Resolve Category Name for UI display
        string parentName = string.Empty;

        if (EditingSubCategoryClone.CategoryId > 0) {
            parentName = CategoriesWithNone.FirstOrDefault(c => c.Id == EditingSubCategoryClone.CategoryId)?.Name ??
                         string.Empty;
        }
        else if (EditingCategoryClone != null && !string.IsNullOrWhiteSpace(EditingCategoryClone.Name)) {
            parentName = EditingCategoryClone.Name;
        }

        EditingSubCategoryClone.CategoryName = parentName;

        if (EditingSubCategoryClone.DefaultBucketId == 0) {
            EditingSubCategoryClone.DefaultBucketId = null;
        }

        StagedSubCategories.Add(EditingSubCategoryClone.Clone());
    }

    private void RemoveStagedCategory(Category? item) {
        if (item != null && StagedCategories.Contains(item)) {
            if (item.Id == 0) {
                //remove related subcategories
                foreach (var subCategory in StagedSubCategories.Where(s => s.CategoryId == item.Id)) {
                    StagedSubCategories.Remove(subCategory);
                }
            }

            StagedCategories.Remove(item);

            OnPropertyChanged(nameof(TotalCategoryItemsToSaveCount));
            OnPropertyChanged(nameof(CanSaveCategory));
            SaveCategoryCommand.NotifyCanExecuteChanged();
        }
    }

    private void ClearCurrentCategoryDraft() {
        if (EditingCategoryClone != null) {
            EditingCategoryClone.Name = string.Empty;
            EditingCategoryClone.Subcategories.Clear();
        }

        if (EditingSubCategoryClone != null) {
            EditingSubCategoryClone.Name = string.Empty;
            EditingSubCategoryClone.DefaultBucketId = null;
        }

        OnPropertyChanged(nameof(TotalCategoryItemsToSaveCount));
        OnPropertyChanged(nameof(CanSaveCategory));
        SaveCategoryCommand.NotifyCanExecuteChanged();
        AddAnotherCategoryCommand.NotifyCanExecuteChanged();
    }

    private void AddCategory() {
        try {
            CancelCategory();

            // Initialize Category Clone
            EditingCategoryClone = new Category { Name = "", SortOrder = 0 };
            EditingCategoryClone.PropertyChanged += EditingCategoryClone_PropertyChanged;

            // Initialize SubCategory Clone for the new overlay
            EditingSubCategoryClone = new SubCategory { CategoryId = 0, Name = "", SortOrder = 0 };

            // In AddCategory() and EditCategory():
            if (EditingSubCategoryClone != null) {
                EditingSubCategoryClone.PropertyChanged += EditingSubCategoryClone_PropertyChanged;
            }

            SelectedCategory = null;
            IsEditingCategory = true;
            IsEditingCategoryEnabled = true;

            // Force ComboBox binding to refresh immediately
            OnPropertyChanged(nameof(AvailableParentCategories));
        }
        catch (Exception ex) {
            Log.Error(ex, "Error initializing new category.");
        }
    }

    private void EditCategory() {
        try {
            CancelCategory();
            if (SelectedCategory == null) return;

            EditingCategoryClone = new Category {
                Id = SelectedCategory.Id,
                Name = SelectedCategory.Name,
                HexColor = SelectedCategory.HexColor,
                SortOrder = SelectedCategory.SortOrder,
                IsArchived = SelectedCategory.IsArchived
            };
            EditingCategoryClone.PropertyChanged += EditingCategoryClone_PropertyChanged;

            // Initialize a clean draft subcategory assigned to this category
            // instead of picking only the first matching subcategory
            EditingSubCategoryClone = new SubCategory {
                CategoryId = SelectedCategory.Id,
                Name = "",
                SortOrder = 0
            };

            if (EditingSubCategoryClone != null) {
                EditingSubCategoryClone.PropertyChanged += EditingSubCategoryClone_PropertyChanged;
            }

            IsEditingCategory = true;
            IsEditingCategoryEnabled = true;

            OnPropertyChanged(nameof(AvailableParentCategories));
            OnPropertyChanged(nameof(TotalCategoryItemsToSaveCount));
            OnPropertyChanged(nameof(CanSaveCategory));
            SaveCategoryCommand.NotifyCanExecuteChanged();
            AddAnotherCategoryCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error entering edit mode for category.");
        }
    }

    // private async Task SaveCategoryAsync() {
    //     if (EditingCategoryClone == null) return;
    //
    //     try {
    //         List<string> errors = EditingCategoryClone.GetValidationErrors();
    //         if (errors.Any()) {
    //             MessageBox.Show(errors.First(), "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
    //             return;
    //         }
    //         
    //         if (SelectedCategory != null) {
    //             UpdateCategoryFromClone(SelectedCategory, EditingCategoryClone);
    //             await _budgetService.UpsertCategoryAsync(SelectedCategory);
    //         }
    //         else {
    //             await _budgetService.UpsertCategoryAsync(EditingCategoryClone);
    //         }
    //
    //         var selectedId = SelectedCategory?.Id ?? EditingCategoryClone.Id;
    //
    //         IsEditingCategory = false;
    //         EditingCategoryClone = null;
    //
    //         await LoadCategoryDataAsync();
    //         await LoadSubCategoryDataAsync();
    //
    //         if (selectedId > 0) {
    //             SelectedCategory = Categories.FirstOrDefault(c => c.Id == selectedId);
    //         }
    //
    //         RequestProjectionRecalculation();
    //     }
    //     catch (Exception ex) {
    //         Log.Error(ex, "Error saving category.");
    //
    //         MessageBox.Show("Failed to save category. See log for details.", "Error", MessageBoxButton.OK,
    //             MessageBoxImage.Error);
    //     }
    // }

    public int TotalCategoryItemsToSaveCount {
        get {
            int stagedCount = (StagedCategories?.Count ?? 0) + (StagedSubCategories?.Count ?? 0);
            bool hasActiveCategoryDraft =
                EditingCategoryClone != null && !string.IsNullOrWhiteSpace(EditingCategoryClone.Name);
            bool hasActiveSubCategoryDraft = EditingSubCategoryClone != null &&
                                             !string.IsNullOrWhiteSpace(EditingSubCategoryClone.Name);

            return stagedCount + (hasActiveCategoryDraft ? 1 : 0) + (hasActiveSubCategoryDraft ? 1 : 0);
        }
    }

    public bool CanSaveCategory => IsEditingCategory &&
                                   IsEditingCategoryEnabled &&
                                   TotalCategoryItemsToSaveCount > 0;

    private void EditingSubCategoryClone_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        try {
            if (e.PropertyName == nameof(SubCategory.Name)) {
                OnPropertyChanged(nameof(TotalCategoryItemsToSaveCount));
                OnPropertyChanged(nameof(CanSaveCategory));
                SaveCategoryCommand.NotifyCanExecuteChanged();
                AddAnotherCategoryCommand.NotifyCanExecuteChanged();
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in EditingSubCategoryClone_PropertyChanged.");
        }
    }

    private async Task SaveCategoryAsync() {
        bool hasCategoryDraft = EditingCategoryClone != null && !string.IsNullOrWhiteSpace(EditingCategoryClone.Name);
        bool hasSubCategoryDraft =
            EditingSubCategoryClone != null && !string.IsNullOrWhiteSpace(EditingSubCategoryClone.Name);

        if (!hasCategoryDraft && !hasSubCategoryDraft && !StagedCategories.Any() && !StagedSubCategories.Any()) {
            MessageBox.Show("Please enter a name for either the category or the subcategory before saving.",
                "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try {
            // 1. Validate Category draft ONLY if populated
            if (hasCategoryDraft) {
                bool isValid = EditingCategoryClone!.ValidateAllProperties();
                List<string> errors = EditingCategoryClone.GetValidationErrors();

                if (!isValid || errors.Any()) {
                    OnPropertyChanged(nameof(EditingCategoryClone));
                    MessageBox.Show(errors.FirstOrDefault() ?? "Please correct the category errors before saving.",
                        "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                StagedCategories.Add(EditingCategoryClone);
            }

            // 2. Validate SubCategory draft ONLY if populated
            if (hasSubCategoryDraft) {
                bool isValid = EditingSubCategoryClone!.ValidateAllProperties();
                List<string> errors = EditingSubCategoryClone.GetValidationErrors();

                if (!isValid || errors.Any()) {
                    OnPropertyChanged(nameof(EditingSubCategoryClone));
                    MessageBox.Show(errors.FirstOrDefault() ?? "Please correct the subcategory errors before saving.",
                        "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (EditingSubCategoryClone.DefaultBucketId == 0)
                    EditingSubCategoryClone.DefaultBucketId = null;

                StagedSubCategories.Add(EditingSubCategoryClone);
            }

            // Dictionary to track Category Name -> New Auto-Generated ID mapping
            var categoryNameToIdMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // 3. Save All Categories First
            foreach (var item in StagedCategories) {
                int categoryId;
                if (SelectedCategory != null && item.Id == SelectedCategory.Id) {
                    UpdateCategoryFromClone(SelectedCategory, item);
                    await _budgetService.UpsertCategoryAsync(SelectedCategory);
                    categoryId = SelectedCategory.Id;
                }
                else {
                    // Returns newly generated database ID
                    await _budgetService.UpsertCategoryAsync(item);
                    categoryId = item.Id;
                }

                if (!string.IsNullOrWhiteSpace(item.Name) && categoryId > 0) {
                    categoryNameToIdMap[item.Name.Trim()] = categoryId;
                }
            }

            // 4. Resolve Parent Category IDs for Subcategories by Matching Name or SelectedCategory
            foreach (var subItem in StagedSubCategories) {
                // If CategoryId is not set, attempt name matching against new categories
                if (subItem.CategoryId == 0) {
                    // Check if a parent category name was stored on the subcategory draft/clone
                    if (!string.IsNullOrWhiteSpace(subItem.CategoryName) &&
                        categoryNameToIdMap.TryGetValue(subItem.CategoryName.Trim(), out int matchedId)) {
                        subItem.CategoryId = matchedId;
                    }
                    else if (categoryNameToIdMap.Count == 1) {
                        // If only one category was created in this batch, fall back to linking to it automatically
                        subItem.CategoryId = categoryNameToIdMap.Values.First();
                    }
                    else if (SelectedCategory != null && SelectedCategory.Id > 0) {
                        subItem.CategoryId = SelectedCategory.Id;
                    }
                }

                if (subItem.CategoryId == 0) {
                    MessageBox.Show($"Please select a valid Parent Category for subcategory '{subItem.Name}'.",
                        "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (subItem.DefaultBucketId == 0)
                    subItem.DefaultBucketId = null;

                await _budgetService.UpsertSubCategoryAsync(subItem);
            }

            int countSaved = StagedCategories.Count + StagedSubCategories.Count;

            // 5. Reset Overlay State & Refresh
            CancelCategory();

            await LoadCategoryDataAsync();
            await LoadSubCategoryDataAsync();

            ShowSuccessToast($"Successfully saved {countSaved} item{(countSaved > 1 ? "s" : "")}.");
        }
        catch (Exception ex) {
            Log.Error(ex, "Error saving categories/subcategories.");
            MessageBox.Show("Failed to save changes. See log for details.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void UpdateCategoryFromClone(Category target, Category clone) {
        try {
            target.Name = clone.Name;
            target.HexColor = clone.HexColor;
            target.SortOrder = clone.SortOrder;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error updating category from clone.");
        }
    }

    private void CancelCategory() {
        try {
            if (EditingCategoryClone != null)
                EditingCategoryClone.PropertyChanged -= EditingCategoryClone_PropertyChanged;
            if (EditingSubCategoryClone != null)
                EditingSubCategoryClone.PropertyChanged -= EditingSubCategoryClone_PropertyChanged;

            IsEditingCategory = false;
            IsEditingCategoryEnabled = false;
            EditingCategoryClone = null;
            EditingSubCategoryClone = null;
            StagedCategories.Clear();
            StagedSubCategories.Clear();

            OnPropertyChanged(nameof(HasStagedCategoryItems));
        }
        catch (Exception ex) {
            Log.Error(ex, "Error cancelling category edit.");
        }
    }

    private async Task DeleteCategoryAsync() {
        if (EditingCategoryClone == null) return;

        try {
            bool inUse = await _budgetService.IsCategoryInUseAsync(EditingCategoryClone.Id);
            if (inUse) {
                MessageBox.Show(
                    "This category currently has subcategories assigned to it. Delete or reassign those subcategories first.",
                    "Cannot Delete Category", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show("Are you sure you want to delete this category?",
                "Delete Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes) {
                // Pre-delete safety backup
                _budgetService.CreateRollingBackup(BackupReason.PreDeleteCategory);

                await _budgetService.DeleteCategoryAsync(EditingCategoryClone.Id);
                IsEditingCategory = false;
                IsEditingCategoryEnabled = false;
                EditingCategoryClone = null;
                await LoadCategoryDataAsync();
                RequestProjectionRecalculation();
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error deleting category.");

            MessageBox.Show("Failed to delete category.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task DeleteSelectedCategoryAsync() {
        if (SelectedCategory == null) return;

        try {
            bool inUse = await _budgetService.IsCategoryInUseAsync(SelectedCategory.Id);
            if (inUse) {
                MessageBox.Show(
                    "This category currently has subcategories assigned to it. Delete or reassign those subcategories first.",
                    "Cannot Delete Category", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Are you sure you want to delete this category?\r\n\r\n{SelectedCategory.Name}",
                "Delete Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes) {
                await _budgetService.DeleteCategoryAsync(SelectedCategory.Id);
                IsEditingCategory = false;
                IsEditingCategoryEnabled = false;
                SelectedCategory = null;
                await LoadCategoryDataAsync();
                RequestProjectionRecalculation();
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error deleting category.");

            MessageBox.Show("Failed to delete category.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region Subcategory CRUD

    private void AddSubCategory() {
        try {
            CancelSubCategory();
            EditingSubCategoryClone = new SubCategory {
                CategoryId = Categories.FirstOrDefault()?.Id ?? 0,
                Name = "",
                SortOrder = 0
            };
            SelectedSubCategory = null;
            IsEditingSubCategory = true;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error initializing new subcategory.");
        }
    }

    private void EditSubCategory() {
        try {
            CancelSubCategory();
            if (SelectedSubCategory == null) return;

            EditingSubCategoryClone = new SubCategory {
                Id = SelectedSubCategory.Id,
                CategoryId = SelectedSubCategory.CategoryId,
                Name = SelectedSubCategory.Name,
                DefaultBucketId = SelectedSubCategory.DefaultBucketId,
                SortOrder = SelectedSubCategory.SortOrder,
                IsArchived = SelectedSubCategory.IsArchived
            };

            IsEditingSubCategory = true;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error entering edit mode for subcategory.");
        }
    }

    // public int TotalSubCategoryItemsToSaveCount {
    //     get {
    //         int stagedCount = StagedSubCategories?.Count ?? 0;
    //         bool hasActiveDraft = EditingSubCategoryClone != null && 
    //                               !string.IsNullOrWhiteSpace(EditingSubCategoryClone.Name);
    //         return stagedCount + (hasActiveDraft ? 1 : 0);
    //     }
    // }
    //
    // public bool CanSaveSubCategory => IsEditingSubCategory && 
    //                                   IsEditingSubCategoryEnabled && 
    //                                   TotalSubCategoryItemsToSaveCount > 0;

    private async Task SaveSubCategoryAsync() {
        if (EditingSubCategoryClone == null) return;

        try {
            bool isValid = EditingSubCategoryClone.ValidateAllProperties();

            List<string> errors = EditingSubCategoryClone.GetValidationErrors();
            if (!isValid || errors.Any()) {
                // Force WPF to re-evaluate all bindings hanging off EditingSubCategoryClone
                OnPropertyChanged(nameof(EditingSubCategoryClone));

                MessageBox.Show(errors.FirstOrDefault() ?? "Please correct the highlighted errors before saving."
                    , "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (EditingSubCategoryClone.DefaultBucketId == 0)
                EditingSubCategoryClone.DefaultBucketId = null;

            if (SelectedSubCategory != null) {
                UpdateSubCategoryFromClone(SelectedSubCategory, EditingSubCategoryClone);
                await _budgetService.UpsertSubCategoryAsync(SelectedSubCategory);
            }
            else {
                await _budgetService.UpsertSubCategoryAsync(EditingSubCategoryClone);
            }

            var selectedId = SelectedSubCategory?.Id ?? EditingSubCategoryClone.Id;

            IsEditingSubCategory = false;
            EditingSubCategoryClone = null;

            await LoadCategoryDataAsync();
            await LoadSubCategoryDataAsync();

            if (selectedId > 0) {
                SelectedSubCategory = SubCategories.FirstOrDefault(s => s.Id == selectedId);
            }

            //RequestProjectionRecalculation();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error saving subcategory.");

            MessageBox.Show("Failed to save subcategory. See log for details.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void RemoveStagedSubCategory(SubCategory? item) {
        if (item != null && StagedSubCategories.Contains(item)) {
            StagedSubCategories.Remove(item);
            OnPropertyChanged(nameof(TotalCategoryItemsToSaveCount));
            OnPropertyChanged(nameof(CanSaveCategory));
            OnPropertyChanged(nameof(HasStagedCategoryItems));
            SaveCategoryCommand.NotifyCanExecuteChanged();
        }
    }

    private void UpdateSubCategoryFromClone(SubCategory target, SubCategory clone) {
        try {
            target.CategoryId = clone.CategoryId;
            target.Name = clone.Name;
            target.DefaultBucketId = clone.DefaultBucketId == 0 ? null : clone.DefaultBucketId;
            target.SortOrder = clone.SortOrder;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error updating subcategory from clone.");
        }
    }

    private void CancelSubCategory() {
        try {
            IsEditingSubCategory = false;
            EditingSubCategoryClone = null;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error cancelling subcategory edit.");
        }
    }

    private async Task DeleteSubCategoryAsync() {
        if (EditingSubCategoryClone == null) return;

        try {
            bool inUse = await _budgetService.IsSubCategoryInUseAsync(EditingSubCategoryClone.Id);
            if (inUse) {
                MessageBox.Show(
                    "This subcategory is currently assigned to existing transactions and cannot be deleted.",
                    "Cannot Delete", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show("Are you sure you want to delete this subcategory?",
                "Delete Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes) {
                // Pre-delete safety backup
                _budgetService.CreateRollingBackup(BackupReason.PreDeleteSubCategory);

                await _budgetService.DeleteSubCategoryAsync(EditingSubCategoryClone.Id);
                IsEditingSubCategory = false;
                EditingSubCategoryClone = null;
                await LoadCategoryDataAsync();
                await LoadSubCategoryDataAsync();
                RequestProjectionRecalculation();
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error deleting subcategory.");

            MessageBox.Show("Failed to delete subcategory.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task DeleteSelectedSubCategoryAsync() {
        if (SelectedSubCategory == null) return;

        try {
            bool inUse = await _budgetService.IsSubCategoryInUseAsync(SelectedSubCategory.Id);
            if (inUse) {
                MessageBox.Show(
                    "This subcategory is currently assigned to existing transactions and cannot be deleted.",
                    "Cannot Delete", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Are you sure you want to delete this subcategory {SelectedSubCategory.Name}?",
                "Delete Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes) {
                await _budgetService.DeleteSubCategoryAsync(SelectedSubCategory.Id);
                SelectedSubCategory = null;
                IsEditingSubCategory = false;
                EditingSubCategoryClone = null;
                await LoadCategoryDataAsync();
                await LoadSubCategoryDataAsync();
                RequestProjectionRecalculation();
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error deleting subcategory.");

            MessageBox.Show("Failed to delete subcategory.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region Transaction CRUD

    private void AddSplitDetailLeg() {
        if (EditingTransactionClone == null) return;

        var leg = new TransactionDetail {
            TransactionId = EditingTransactionClone.TransactionId,
            Description = EditingTransactionClone.Description,
            Amount = Math.Max(0m, UnassignedSplitAmount),
            AccountId = EditingTransactionClone.AccountId,
            TransactionDate = EditingTransactionClone.TransactionDate
        };

        EditingSplitDetails.Add(leg);
        OnPropertyChanged(nameof(UnassignedSplitAmount));
    }

    private void AddAnotherTransaction() {
        if (EditingTransactionClone == null) return;

        // Validate current item before staging
        bool isValid = EditingTransactionClone.ValidateAllProperties();
        List<string> errors = EditingTransactionClone.GetValidationErrors();
        if (!isValid || errors.Any()) {
            OnPropertyChanged(nameof(EditingTransactionClone));
            MessageBox.Show(errors.FirstOrDefault() ?? "Please correct the highlighted errors before saving."
                , "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(EditingTransactionClone.Description)) {
            MessageBox.Show("Please enter a description for the transaction.", "Validation Error", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Clean zero IDs
        if (EditingTransactionClone.AccountId == 0) EditingTransactionClone.AccountId = null;
        if (EditingTransactionClone.ToAccountId == 0) EditingTransactionClone.ToAccountId = null;
        if (EditingTransactionClone.BillId == 0) EditingTransactionClone.BillId = null;
        if (EditingTransactionClone.BucketId == 0) EditingTransactionClone.BucketId = null;
        if (EditingTransactionClone.SubCategoryId == 0) EditingTransactionClone.SubCategoryId = null;

        // Handle Split Details before staging
        if (IsSplitTransaction) {
            if (Math.Abs(UnassignedSplitAmount) > 0.01m) {
                MessageBox.Show(
                    $"The total allocated split amount does not match the transaction total. Remaining unassigned: {UnassignedSplitAmount:C}",
                    "Split Mismatch", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            EditingTransactionClone.Details.Clear();
            foreach (var leg in EditingSplitDetails) {
                EditingTransactionClone.Details.Add(leg);
            }
        }

        // Stage a clone of the filled form
        StagedTransactions.Add(EditingTransactionClone.Clone());

        // Prepare a clean fresh instance for the next entry
        if (EditingTransactionClone != null) {
            EditingTransactionClone.PropertyChanged -= EditingTransactionClone_PropertyChanged;
        }

        var nextTrans = new Transaction {
            Description = "",
            Memo = "",
            Amount = 0,
            TransactionDate = EditingTransactionClone?.TransactionDate ?? DateTime.Today,
            ToFitId = Guid.NewGuid().ToString(),
            FromFitId = Guid.NewGuid().ToString()
        };

        RefreshTransactionEditState(nextTrans);
        EditingTransactionClone = nextTrans;
        EditingTransactionClone.PropertyChanged += EditingTransactionClone_PropertyChanged;

        // RESET SPLIT CHECKBOX AND CATEGORIES FOR NEXT ITEM
        IsSplitTransaction = false;
        EditingSplitDetails.Clear();

        OnPropertyChanged(nameof(TotalTransactionItemsToSaveCount));
        OnPropertyChanged(nameof(CanSaveTransaction));
        SaveTransactionCommand.NotifyCanExecuteChanged();
    }

    private void RemoveStagedTransaction(Transaction? item) {
        if (item != null && StagedTransactions.Contains(item)) {
            StagedTransactions.Remove(item);

            OnPropertyChanged(nameof(TotalTransactionItemsToSaveCount));
            OnPropertyChanged(nameof(CanSaveTransaction));
            SaveTransactionCommand.NotifyCanExecuteChanged();
        }
    }

    private void ClearCurrentTransactionDraft() {
        if (EditingTransactionClone == null) return;

        // Reset fields on the current active edit form without closing overlay
        EditingTransactionClone.Description = string.Empty;
        EditingTransactionClone.Memo = string.Empty;
        EditingTransactionClone.Amount = 0;
        EditingTransactionClone.BillId = null;
        EditingTransactionClone.BucketId = null;
        EditingTransactionClone.SubCategoryId = null;

        // Reset Split Transaction State
        IsSplitTransaction = false;
        EditingSplitDetails.Clear();
    }

    private void AddTransaction() {
        try {
            if (EditingTransactionClone != null) {
                EditingTransactionClone.PropertyChanged -= EditingTransactionClone_PropertyChanged;
            }

            var editTrans = new Transaction {
                Description = "", Memo = "", Amount = 0, TransactionDate = DateTime.Today,
                ToFitId = Guid.NewGuid().ToString(), FromFitId = Guid.NewGuid().ToString()
            };
            IsEditingTransactionEnabled = true;
            RefreshTransactionEditState(editTrans);

            EditingTransactionClone = editTrans;
            EditingTransactionClone.PropertyChanged += EditingTransactionClone_PropertyChanged;

            SelectedTransaction = null;
            IsEditingTransaction = true;

            AddAnotherTransactionCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error initializing new transaction.");
        }
    }

    private void EditTransaction() {
        try {
            CancelTransaction();
            if (SelectedTransaction == null) return;
            if (EditingTransactionClone != null) {
                EditingTransactionClone.PropertyChanged -= EditingTransactionClone_PropertyChanged;
            }

            var editTrans = SelectedTransaction.Clone();

            RefreshTransactionEditState(editTrans);

            EditingTransactionClone = editTrans;
            EditingTransactionClone.PropertyChanged += EditingTransactionClone_PropertyChanged;

            // Load existing split details if present
            EditingSplitDetails.Clear();
            if (editTrans.Details.Count(x => !x.IsInterestOnly) > 2) {
                foreach (var leg in editTrans.Details) {
                    EditingSplitDetails.Add(leg);
                }

                IsSplitTransaction = true;
            }
            else {
                IsSplitTransaction = false;
            }

            IsEditingTransaction = true;

            OnPropertyChanged(nameof(TotalTransactionItemsToSaveCount));
            OnPropertyChanged(nameof(CanSaveTransaction));
            SaveTransactionCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error entering edit mode for transaction.");
        }
    }

    private string? _originalFromFitId;
    private string? _originalToFitId;

    private void RefreshTransactionEditState(Transaction? source) {
        try {
            if (source == null) return;

            var fromAccount = Accounts.FirstOrDefault(a => a.Id == source.AccountId);
            var toAccount = Accounts.FirstOrDefault(a => a.Id == source.ToAccountId);

            bool fromArchived = fromAccount?.IsArchived ?? false;
            bool toArchived = toAccount?.IsArchived ?? false;

            IsEditingTransactionEnabled = !fromArchived && !toArchived;

            if (fromAccount == null && source.AccountId != null &&
                source.AccountId != 0) {
                IsEditingTransactionEnabled = false;
            }

            if (toAccount == null && source.ToAccountId != null &&
                source.ToAccountId != 0) {
                IsEditingTransactionEnabled = false;
            }

            var filteredAccounts = IsEditingTransactionEnabled
                ? Accounts.Where(a => !a.IsArchived || a.Id == source.AccountId).ToList()
                : Accounts.ToList();

            if (source.AccountId != null && source.AccountId != 0 &&
                filteredAccounts.All(a => a.Id != source.AccountId)) {
                var missingAccount = fromAccount;
                if (missingAccount != null) {
                    filteredAccounts.Add(missingAccount);
                }
            }

            var accountsWithNone = new List<Account> { new Account { Id = 0, Name = "(None)" } };
            accountsWithNone.AddRange(filteredAccounts.OrderBy(a => a.IsArchived).ThenBy(a => a.Name));

            TransactionAccounts.Clear();
            TransactionAccounts.AddRange(accountsWithNone);

            var filteredToAccounts = IsEditingTransactionEnabled
                ? Accounts.Where(a => !a.IsArchived || a.Id == source.ToAccountId).ToList()
                : Accounts.ToList();

            if (source.ToAccountId != null && source.ToAccountId != 0 &&
                filteredToAccounts.All(a => a.Id != source.ToAccountId)) {
                var missingAccount = toAccount;
                if (missingAccount != null) {
                    filteredToAccounts.Add(missingAccount);
                }
            }

            var toAccountsWithNone = new List<Account> { new Account { Id = 0, Name = "(None)" } };
            toAccountsWithNone.AddRange(filteredToAccounts.OrderBy(a => a.IsArchived).ThenBy(a => a.Name));

            TransactionToAccounts.Clear();
            TransactionToAccounts.AddRange(toAccountsWithNone);

            // Snapshot original reconciliation IDs and FitIds for undo/revert functionality
            _originalFromAccountReconciledId = source.FromAccountReconciliationId;
            _originalToAccountReconciledId = source.ToAccountReconciliationId;
            _originalFromFitId = source.FromFitId;
            _originalToFitId = source.ToFitId;

            // Populate the sub-panel items collection
            EditingTransactionStatusItems.Clear();

            if (source.AccountId.HasValue && source.AccountId.Value > 0) {
                var fromAcc = Accounts.FirstOrDefault(a => a.Id == source.AccountId.Value);
                if (fromAcc != null) {
                    EditingTransactionStatusItems.Add(new TransactionStatusItemViewModel {
                        AccountId = fromAcc.Id,
                        AccountName = $"From: {fromAcc.Name}",
                        Side = TransactionSide.From,
                        CurrentStatus = source.FromAccountReconciliationId.HasValue
                            ? ReconciliationStatus.Reconciled
                            : ((source.FromAccountIsCleared ?? false)
                                ? ReconciliationStatus.Cleared
                                : ReconciliationStatus.Uncleared),
                        StatusDetailsText = source.FromAccountReconciliationId.HasValue
                            ? $"Reconciled ID: {source.FromAccountReconciliationId.Value}"
                            : "Not Reconciled",
                        StatusChangedCallback = HandleTransactionStatusChanged
                    });
                }
            }

            if (source.ToAccountId.HasValue && source.ToAccountId.Value > 0) {
                var toAcc = Accounts.FirstOrDefault(a => a.Id == source.ToAccountId.Value);
                if (toAcc != null) {
                    EditingTransactionStatusItems.Add(new TransactionStatusItemViewModel {
                        AccountId = toAcc.Id,
                        AccountName = $"To: {toAcc.Name}",
                        Side = TransactionSide.To,
                        CurrentStatus = source.ToAccountReconciliationId.HasValue
                            ? ReconciliationStatus.Reconciled
                            : ((source.ToAccountIsCleared ?? false)
                                ? ReconciliationStatus.Cleared
                                : ReconciliationStatus.Uncleared),
                        StatusDetailsText = source.ToAccountReconciliationId.HasValue
                            ? $"Reconciled ID: {source.ToAccountReconciliationId.Value}"
                            : "Not Reconciled",
                        StatusChangedCallback = HandleTransactionStatusChanged
                    });
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error refreshing transaction edit state.");
        }
    }

    private void HandleTransactionStatusChanged(TransactionStatusItemViewModel item, ReconciliationStatus newStatus) {
        if (EditingTransactionClone == null) return;

        if (item.Side == TransactionSide.From) {
            switch (newStatus) {
                case ReconciliationStatus.Uncleared:
                    EditingTransactionClone.FromAccountReconciliationId = null;
                    EditingTransactionClone.FromAccountIsCleared = false;
                    // Only assign a new FitId if we haven't already generated one, or generate one provisionally 
                    // but allow it to revert if changed back.
                    if (string.IsNullOrEmpty(EditingTransactionClone.FromFitId) ||
                        EditingTransactionClone.FromFitId == _originalFromFitId) {
                        EditingTransactionClone.FromFitId = Guid.NewGuid().ToString();
                    }

                    break;
                case ReconciliationStatus.Cleared:
                    EditingTransactionClone.FromAccountReconciliationId = null;
                    EditingTransactionClone.FromAccountIsCleared = true;
                    // Revert to original FitId if turning it back to cleared/reconciled
                    EditingTransactionClone.FromFitId = _originalFromFitId ?? "";
                    break;
                case ReconciliationStatus.Reconciled:
                    EditingTransactionClone.FromAccountIsCleared = true;
                    EditingTransactionClone.FromFitId = _originalFromFitId ?? "";
                    break;
            }
        }
        else if (item.Side == TransactionSide.To) {
            switch (newStatus) {
                case ReconciliationStatus.Uncleared:
                    EditingTransactionClone.ToAccountReconciliationId = null;
                    EditingTransactionClone.ToAccountIsCleared = false;
                    if (string.IsNullOrEmpty(EditingTransactionClone.ToFitId) ||
                        EditingTransactionClone.ToFitId == _originalToFitId) {
                        EditingTransactionClone.ToFitId = Guid.NewGuid().ToString();
                    }

                    break;
                case ReconciliationStatus.Cleared:
                    EditingTransactionClone.ToAccountReconciliationId = null;
                    EditingTransactionClone.ToAccountIsCleared = true;
                    EditingTransactionClone.ToFitId = _originalToFitId ?? "";
                    break;
                case ReconciliationStatus.Reconciled:
                    EditingTransactionClone.ToAccountIsCleared = true;
                    EditingTransactionClone.ToFitId = _originalToFitId ?? "";
                    break;
            }
        }
    }

    public int TotalTransactionItemsToSaveCount {
        get {
            int stagedCount = StagedTransactions?.Count ?? 0;
            bool hasActiveDraft = EditingTransactionClone != null &&
                                  !string.IsNullOrWhiteSpace(EditingTransactionClone.Description);
            return stagedCount + (hasActiveDraft ? 1 : 0);
        }
    }

    public bool CanSaveTransaction => IsEditingTransaction &&
                                      IsEditingTransactionEnabled &&
                                      TotalTransactionItemsToSaveCount > 0;

    private async Task SaveTransactionAsync() {
        if (EditingTransactionClone == null && !StagedTransactions.Any()) return;

        try {
            // 1. Stage the currently visible item if it has valid input
            if (EditingTransactionClone != null && !string.IsNullOrWhiteSpace(EditingTransactionClone.Description)) {
                if (EditingTransactionClone.AccountId == 0) EditingTransactionClone.AccountId = null;
                if (EditingTransactionClone.ToAccountId == 0) EditingTransactionClone.ToAccountId = null;
                if (EditingTransactionClone.BillId == 0) EditingTransactionClone.BillId = null;
                if (EditingTransactionClone.BucketId == 0) EditingTransactionClone.BucketId = null;
                if (EditingTransactionClone.SubCategoryId == 0) EditingTransactionClone.SubCategoryId = null;

                bool isValid = EditingTransactionClone.ValidateAllProperties();
                List<string> errors = EditingTransactionClone.GetValidationErrors();
                if (!isValid || errors.Any()) {
                    // Force WPF to re-evaluate all bindings hanging off EditingTransactionClone
                    OnPropertyChanged(nameof(EditingTransactionClone));

                    MessageBox.Show(errors.FirstOrDefault() ?? "Please correct the highlighted errors before saving."
                        , "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                StagedTransactions.Add(EditingTransactionClone);

                OnPropertyChanged(nameof(TotalTransactionItemsToSaveCount));
                OnPropertyChanged(nameof(CanSaveTransaction));
                SaveTransactionCommand.NotifyCanExecuteChanged();
            }

            if (!StagedTransactions.Any()) {
                MessageBox.Show("Please enter a description for the transaction.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (IsSplitTransaction) {
                if (Math.Abs(UnassignedSplitAmount) > 0.01m) {
                    MessageBox.Show(
                        $"The total allocated split amount does not match the transaction total. Remaining unassigned: {UnassignedSplitAmount:C}",
                        "Split Mismatch", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                EditingTransactionClone!.Details.Clear();
                foreach (var leg in EditingSplitDetails) {
                    EditingTransactionClone.Details.Add(leg);
                }
            }

            // 2. Persist all staged items to database without triggering projections intermediate
            foreach (var item in StagedTransactions) {
                if (SelectedTransaction != null && item.TransactionId == SelectedTransaction.TransactionId) {
                    UpdateTransactionFromClone(SelectedTransaction, item);
                    await _budgetService.UpsertTransactionAsync(SelectedTransaction);
                }
                else {
                    await _budgetService.UpsertTransactionAsync(item);
                }
            }

            int countSaved = StagedTransactions.Count;

            // 3. Reset editor state and clear staging list
            if (EditingTransactionClone != null) {
                EditingTransactionClone.PropertyChanged -= EditingTransactionClone_PropertyChanged;
            }

            IsEditingTransactionEnabled = false;
            IsEditingTransaction = false;
            EditingTransactionClone = null;
            StagedTransactions.Clear();

            OnPropertyChanged(nameof(TotalTransactionItemsToSaveCount));
            OnPropertyChanged(nameof(CanSaveTransaction));
            SaveTransactionCommand.NotifyCanExecuteChanged();

            // 4. Single refresh pass for UI & Projections
            await LoadPeriodDataAsync();
            RequestProjectionRecalculation();

            ShowSuccessToast($"Successfully saved {countSaved} transaction{(countSaved > 1 ? "s" : "")}.");
        }
        catch (Exception ex) {
            Log.Error(ex, "Error saving batch transactions.");
            MessageBox.Show("Failed to save transactions. See log for details.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void UpdateTransactionFromClone(Transaction target, Transaction clone) {
        try {
            target.TransactionId = clone.TransactionId;
            target.ToFitId = clone.ToFitId;
            target.FromFitId = clone.FromFitId;
            target.Description = clone.Description;
            target.Memo = clone.Memo;
            target.Amount = clone.Amount;
            target.TransactionDate = clone.TransactionDate;
            target.AccountId = clone.AccountId == 0 ? null : clone.AccountId;
            target.ToAccountId = clone.ToAccountId == 0 ? null : clone.ToAccountId;
            target.BucketId = clone.BucketId == 0 ? null : clone.BucketId;
            target.SubCategoryId = clone.SubCategoryId == 0 ? null : clone.SubCategoryId;
            target.BillId = clone.BillId == 0 ? null : clone.BillId;
            target.IsPrincipalOnly = clone.IsPrincipalOnly;
            target.IsInterestOnly = clone.IsInterestOnly;
            target.IsRebalance = clone.IsRebalance;
            target.PaycheckId = clone.PaycheckId;
            target.PaycheckOccurrenceDate = clone.PaycheckOccurrenceDate;
            target.FromAccountReconciliationId = clone.FromAccountReconciliationId;
            target.ToAccountReconciliationId = clone.ToAccountReconciliationId;
            target.FromAccountIsCleared = clone.FromAccountIsCleared;
            target.ToAccountIsCleared = clone.ToAccountIsCleared;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error updating transaction from clone.");
        }
    }

    // private void CancelTransaction() {
    //     try {
    //         if (SelectedTransaction != null && SelectedTransaction.TransactionId == Guid.Empty) {
    //             CurrentPeriodTransactions.Remove(SelectedTransaction);
    //         }
    //
    //         IsEditingTransaction = false;
    //         EditingTransactionClone = null;
    //     }
    //     catch (Exception ex) {
    //         Log.Error(ex, "Error cancelling transaction edit.");
    //     }
    // }

    private void CancelTransaction() {
        try {
            if (EditingTransactionClone != null) {
                EditingTransactionClone.PropertyChanged -= EditingTransactionClone_PropertyChanged;
            }

            if (SelectedTransaction != null && SelectedTransaction.TransactionId == Guid.Empty) {
                CurrentPeriodTransactions.Remove(SelectedTransaction);
            }

            StagedTransactions.Clear();

            // Reset Split Transaction State
            IsSplitTransaction = false;
            EditingSplitDetails.Clear();

            IsEditingTransaction = false;
            EditingTransactionClone = null;
            IsEditingTransactionEnabled = false;

            OnPropertyChanged(nameof(TotalTransactionItemsToSaveCount));
            OnPropertyChanged(nameof(CanSaveTransaction));
            SaveTransactionCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error cancelling transaction edit.");
        }
    }

    private async Task DeleteTransactionAsync() {
        if (EditingTransactionClone == null) return;
        var messageBoxResult = MessageBox.Show(
            "Are you sure you want to delete this transaction?",
            "Delete Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );

        if (messageBoxResult == MessageBoxResult.Yes) {
            try {
                await _budgetService.DeleteTransactionAsync(EditingTransactionClone.TransactionId);
                IsEditingTransaction = false;
                IsEditingTransactionEnabled = false;
                EditingTransactionClone = null;
                await LoadPeriodDataAsync();
                RequestProjectionRecalculation();
            }
            catch (Exception ex) {
                Log.Error(ex, "Error deleting transaction.");

                MessageBox.Show("Failed to delete transaction. See log for details.", "Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }


    private async Task DeleteSelectedTransactionAsync() {
        if (SelectedTransaction == null || SelectedTransaction.TransactionId == Guid.Empty) return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete this transaction?\r\n\r\n{SelectedTransaction!.Description} {SelectedTransaction.Amount:C} {SelectedTransaction.TransactionDate:MM/dd/yyyy} ",
            "Delete Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );

        if (result == MessageBoxResult.Yes) {
            try {
                await _budgetService.DeleteTransactionAsync(SelectedTransaction.TransactionId);
                SelectedTransaction = null;

                await LoadPeriodDataAsync();
                RequestProjectionRecalculation();

                ShowSuccessToast("Transaction deleted.");
            }
            catch (Exception ex) {
                Log.Error(ex, "Error deleting selected transaction.");
                MessageBox.Show("Failed to delete transaction.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    #endregion

    #region Paycheck CRUD

    private void AddAnotherPaycheck() {
        if (EditingPaycheckClone == null) return;

        // Validate current item before staging
        bool isValid = EditingPaycheckClone.ValidateAllProperties();
        List<string> errors = EditingPaycheckClone.GetValidationErrors();
        if (!isValid || errors.Any()) {
            // Force WPF to re-evaluate all bindings hanging off EditingTransactionClone
            OnPropertyChanged(nameof(EditingPaycheckClone));

            MessageBox.Show(errors.FirstOrDefault() ?? "Please correct the highlighted errors before saving."
                , "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(EditingPaycheckClone.Name)) {
            MessageBox.Show("Please enter a name for the paycheck.", "Validation Error", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Clean zero IDs
        if (EditingPaycheckClone.AccountId == 0) EditingPaycheckClone.AccountId = null;

        // Stage a clone of the filled form
        StagedPaychecks.Add(EditingPaycheckClone.Clone());

        // Prepare a clean fresh instance for the next entry
        if (EditingPaycheckClone != null) {
            EditingPaycheckClone.PropertyChanged -= EditingPaycheckClone_PropertyChanged;
        }

        var nextTrans = new Paycheck {
            Name = "", ExpectedAmount = 0, StartDate = DateTime.Today, Frequency = Frequency.BiWeekly
        };

        EditingPaycheckClone = nextTrans;

        EditingPaycheckClone.PropertyChanged += EditingPaycheckClone_PropertyChanged;

        OnPropertyChanged(nameof(TotalPaycheckItemsToSaveCount));
        OnPropertyChanged(nameof(CanSavePaycheck));
        SavePaycheckCommand.NotifyCanExecuteChanged();
    }

    private void RemoveStagedPaycheck(Paycheck? item) {
        if (item != null && StagedPaychecks.Contains(item)) {
            StagedPaychecks.Remove(item);

            OnPropertyChanged(nameof(TotalPaycheckItemsToSaveCount));
            OnPropertyChanged(nameof(CanSavePaycheck));
            SavePaycheckCommand.NotifyCanExecuteChanged();
        }
    }

    private void ClearCurrentPaycheckDraft() {
        if (EditingPaycheckClone == null) return;

        // Reset fields on the current active edit form without closing overlay
        EditingPaycheckClone.Name = string.Empty;
        EditingPaycheckClone.ExpectedAmount = 0;
        EditingPaycheckClone.StartDate = DateTime.Today;
        EditingPaycheckClone.Frequency = Frequency.BiWeekly;
    }

    private void AddPaycheck() {
        try {
            if (EditingPaycheckClone != null) {
                EditingPaycheckClone.PropertyChanged -= EditingPaycheckClone_PropertyChanged;
            }

            EditingPaycheckClone = new Paycheck {
                Name = "", ExpectedAmount = 0, StartDate = DateTime.Today, Frequency = Frequency.BiWeekly
            };

            EditingPaycheckClone.PropertyChanged += EditingPaycheckClone_PropertyChanged;

            SelectedPaycheck = null;
            IsEditingPaycheck = true;
            IsEditingPaycheckEnabled = true;
            AddAnotherPaycheckCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error initializing new paycheck.");
        }
    }

    private void EditPaycheck() {
        try {
            CancelPaycheck();
            if (SelectedPaycheck == null) return;
            if (EditingPaycheckClone != null) {
                EditingPaycheckClone.PropertyChanged -= EditingPaycheckClone_PropertyChanged;
            }

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

            EditingPaycheckClone.PropertyChanged += EditingPaycheckClone_PropertyChanged;

            IsEditingPaycheck = true;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error entering edit mode for paycheck.");
        }
    }

    public int TotalPaycheckItemsToSaveCount {
        get {
            int stagedCount = StagedPaychecks?.Count ?? 0;
            bool hasActiveDraft = EditingPaycheckClone != null &&
                                  !string.IsNullOrWhiteSpace(EditingPaycheckClone.Name);
            return stagedCount + (hasActiveDraft ? 1 : 0);
        }
    }

    public bool CanSavePaycheck => IsEditingPaycheck &&
                                   IsEditingPaycheckEnabled &&
                                   TotalPaycheckItemsToSaveCount > 0;

    private async Task SavePaycheckAsync() {
        if (EditingPaycheckClone == null && !StagedPaychecks.Any()) return;

        try {
            // 1. Stage current draft if valid
            if (EditingPaycheckClone != null && !string.IsNullOrWhiteSpace(EditingPaycheckClone.Name)) {
                bool isValid = EditingPaycheckClone.ValidateAllProperties();

                List<string> errors = EditingPaycheckClone.GetValidationErrors();
                if (!isValid || errors.Any()) {
                    // Force WPF to re-evaluate all bindings hanging off EditingPaycheckClone
                    OnPropertyChanged(nameof(EditingPaycheckClone));

                    MessageBox.Show(errors.FirstOrDefault() ?? "Please correct the highlighted errors before saving."
                        , "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (EditingPaycheckClone.AccountId == 0) EditingPaycheckClone.AccountId = null;
                StagedPaychecks.Add(EditingPaycheckClone);

                OnPropertyChanged(nameof(TotalPaycheckItemsToSaveCount));
                OnPropertyChanged(nameof(CanSavePaycheck));
                SavePaycheckCommand.NotifyCanExecuteChanged();
            }

            if (!StagedPaychecks.Any()) {
                MessageBox.Show("Please enter a name for the paycheck.", "Validation Error", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // 2. Persist all staged items
            foreach (var item in StagedPaychecks) {
                if (SelectedPaycheck != null && item.Id == SelectedPaycheck.Id) {
                    UpdatePaycheckFromClone(SelectedPaycheck, item);
                    await _budgetService.UpsertPaycheckAsync(SelectedPaycheck);
                }
                else {
                    await _budgetService.UpsertPaycheckAsync(item);
                }
            }

            int countSaved = StagedPaychecks.Count;

            // 3. Reset editor state & clear staged collection
            if (EditingPaycheckClone != null) {
                EditingPaycheckClone.PropertyChanged -= EditingPaycheckClone_PropertyChanged;
            }

            IsEditingPaycheck = false;
            IsEditingPaycheckEnabled = false;
            EditingPaycheckClone = null;
            StagedPaychecks.Clear();

            OnPropertyChanged(nameof(TotalPaycheckItemsToSaveCount));
            OnPropertyChanged(nameof(CanSavePaycheck));
            OnPropertyChanged(nameof(HasMultiplePaychecks));
            SavePaycheckCommand.NotifyCanExecuteChanged();

            // 4. Refresh UI once
            await LoadPaycheckDataAsync();
            await LoadPeriodDataAsync();
            RefreshPaychecks();
            LoadPaychecks();
            RequestProjectionRecalculation();

            ShowSuccessToast($"Successfully saved {countSaved} paycheck{(countSaved > 1 ? "s" : "")}.");
        }
        catch (Exception ex) {
            Log.Error(ex, "Error saving batch paychecks.");
            MessageBox.Show("Failed to save paychecks. See log for details.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void UpdatePaycheckFromClone(Paycheck target, Paycheck clone) {
        try {
            target.Name = clone.Name;
            target.ExpectedAmount = clone.ExpectedAmount;
            target.Frequency = clone.Frequency;
            target.StartDate = clone.StartDate;
            target.EndDate = clone.EndDate;
            target.AccountId = clone.AccountId;
            target.IsBalanced = clone.IsBalanced;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error updating paycheck from clone.");
        }
    }

    private void CancelPaycheck() {
        try {
            if (EditingPaycheckClone != null) {
                EditingPaycheckClone.PropertyChanged -= EditingPaycheckClone_PropertyChanged;
            }

            IsEditingPaycheck = false;
            IsEditingPaycheckEnabled = false;
            EditingPaycheckClone = null;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error cancelling paycheck edit.");
        }
    }

    private async Task DeletePaycheckAsync() {
        if (EditingPaycheckClone == null) return;
        var messageBoxResult = MessageBox.Show(
            "Are you sure you want to delete this paycheck?",
            "Delete Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );

        if (messageBoxResult == MessageBoxResult.Yes) {
            try {
                // Pre-delete safety backup
                _budgetService.CreateRollingBackup(BackupReason.PreDeletePaycheck);

                await _budgetService.DeletePaycheckAsync(EditingPaycheckClone.Id);
                IsEditingPaycheck = false;
                IsEditingPaycheckEnabled = false;
                EditingPaycheckClone = null;
                await LoadPaycheckDataAsync();
                await LoadPeriodDataAsync();
                RefreshPaychecks();
                RequestProjectionRecalculation();
            }
            catch (Exception ex) {
                Log.Error(ex, "Error deleting paycheck.");

                MessageBox.Show("Failed to delete paycheck. See log for details.", "Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private async Task DeleteSelectedPaycheckAsync() {
        if (SelectedPaycheck == null) return;
        var messageBoxResult = MessageBox.Show(
            $"Are you sure you want to delete this paycheck?\r\n\r\n{SelectedPaycheck.Name}",
            "Delete Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );

        if (messageBoxResult == MessageBoxResult.Yes) {
            try {
                await _budgetService.DeletePaycheckAsync(SelectedPaycheck.Id);
                IsEditingPaycheck = false;
                IsEditingPaycheckEnabled = false;
                SelectedPaycheck = null;
                await LoadPaycheckDataAsync();
                await LoadPeriodDataAsync();
                RefreshPaychecks();
                RequestProjectionRecalculation();
            }
            catch (Exception ex) {
                Log.Error(ex, "Error deleting paycheck.");

                MessageBox.Show("Failed to delete paycheck. See log for details.", "Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    #endregion

    #region Account CRUD

    private void AddAnotherAccount() {
        if (EditingAccountClone == null) return;

        // Validate current item before staging
        bool isValid = EditingAccountClone.ValidateAllProperties();
        List<string> errors = EditingAccountClone.GetValidationErrors();
        if (!isValid || errors.Any()) {
            // Force WPF to re-evaluate all bindings hanging off EditingTransactionClone
            OnPropertyChanged(nameof(EditingAccountClone));

            MessageBox.Show(errors.FirstOrDefault() ?? "Please correct the highlighted errors before saving."
                , "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(EditingAccountClone.Name)) {
            MessageBox.Show("Please enter a name for the account.", "Validation Error", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Clean zero IDs

        // Stage a clone of the filled form
        StagedAccounts.Add(EditingAccountClone.Clone());

        // Prepare a clean fresh instance for the next entry
        if (EditingAccountClone != null) {
            EditingAccountClone.PropertyChanged -= EditingAccountClone_PropertyChanged;
        }

        var nextTrans = new Account {
            Name = "",
            Type = AccountType.Checking,
            Balance = 0,
            BalanceAsOf = DateTime.Today,
            IncludeInTotal = true,
            MortgageDetails = new MortgageDetails(),
            CreditCardDetails = new CreditCardDetails(),
            HexColor = "#FF808080"
        };

        EditingAccountClone = nextTrans;

        EditingAccountClone.PropertyChanged += EditingAccountClone_PropertyChanged;

        OnPropertyChanged(nameof(TotalAccountItemsToSaveCount));
        OnPropertyChanged(nameof(CanSaveAccount));
        SaveAccountCommand.NotifyCanExecuteChanged();
    }

    private void RemoveStagedAccount(Account? item) {
        if (item != null && StagedAccounts.Contains(item)) {
            StagedAccounts.Remove(item);

            OnPropertyChanged(nameof(TotalAccountItemsToSaveCount));
            OnPropertyChanged(nameof(CanSaveAccount));
            SaveAccountCommand.NotifyCanExecuteChanged();
        }
    }

    private void ClearCurrentAccountDraft() {
        if (EditingAccountClone == null) return;

        // Reset fields on the current active edit form without closing overlay
        EditingAccountClone.Name = string.Empty;
        EditingAccountClone.IncludeInTotal = true;
        EditingAccountClone.Balance = 0;
        EditingAccountClone.BalanceAsOf = DateTime.Today;
        EditingAccountClone.Type = AccountType.Checking;
        EditingAccountClone.MortgageDetails = new MortgageDetails();
        EditingAccountClone.CreditCardDetails = new CreditCardDetails();
        EditingAccountClone.HexColor = "#FF808080";
    }

    private void AddAccount() {
        try {
            if (EditingAccountClone != null) {
                EditingAccountClone.PropertyChanged -= EditingAccountClone_PropertyChanged;
            }

            EditingAccountClone = new Account {
                Name = "",
                Type = AccountType.Checking,
                Balance = 0,
                BalanceAsOf = DateTime.Today,
                IncludeInTotal = true,
                MortgageDetails = new MortgageDetails(),
                CreditCardDetails = new CreditCardDetails(),
                HexColor = "#FF808080"
            };

            EditingAccountClone.PropertyChanged += EditingAccountClone_PropertyChanged;

            SelectedAccount = null;
            IsEditingAccount = true;
            IsEditingAccountEnabled = true;
            AddAnotherAccountCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error initializing new account.");
        }
    }

    private void EditAccount() {
        try {
            CancelAccount();
            if (SelectedAccount == null) return;
            if (EditingAccountClone != null) {
                EditingAccountClone.PropertyChanged -= EditingAccountClone_PropertyChanged;
            }

            EditingAccountClone = new Account {
                Id = SelectedAccount.Id,
                Name = SelectedAccount.Name,
                BankName = SelectedAccount.BankName,
                Balance = SelectedAccount.Balance,
                BalanceAsOf = SelectedAccount.BalanceAsOf,
                AnnualGrowthRate = SelectedAccount.AnnualGrowthRate,
                IncludeInTotal = SelectedAccount.IncludeInTotal,
                Type = SelectedAccount.Type,
                HexColor = SelectedAccount.HexColor,
                IsPrimary = SelectedAccount.IsPrimary,
                IsArchived = SelectedAccount.IsArchived
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

            if (SelectedAccount.AccountAprHistory != null) {
                EditingAccountClone.AccountAprHistory =
                    JsonConvert.DeserializeObject<List<AccountAprHistory>>(
                        JsonConvert.SerializeObject(SelectedAccount.AccountAprHistory));
            }
            else {
                EditingAccountClone.AccountAprHistory = new();
            }

            EditingAccountClone.PropertyChanged += EditingAccountClone_PropertyChanged;

            IsEditingAccount = true;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error entering edit mode for account.");
        }
    }

    // private async Task SaveAccountAsync() {
    //     if (EditingAccountClone != null) {
    //         try {
    //             List<string> errors = EditingAccountClone.GetValidationErrors();
    //             if (errors.Any()) {
    //                 MessageBox.Show(errors.First(), "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
    //                 return;
    //             }
    //             
    //             if (EditingAccountClone.Type == AccountType.CreditCard &&
    //                 (EditingAccountClone.AccountAprHistory == null ||
    //                  EditingAccountClone.AccountAprHistory.Count ==
    //                  0)) {
    //                 MessageBox.Show(
    //                     "Before you can save this credit card, you need to set up your interest rates.",
    //                     "Incomplete Setup",
    //                     MessageBoxButton.OK,
    //                     MessageBoxImage.Warning
    //                 );
    //                 SetAccountAprRatesCommand.Execute(EditingAccountClone);
    //                 return;
    //             }
    //
    //             var selectedAccountId = SelectedAccount?.Id;
    //             if (SelectedAccount != null) {
    //                 UpdateAccountFromClone(SelectedAccount, EditingAccountClone);
    //                 await _budgetService.UpsertAccountAsync(SelectedAccount);
    //             }
    //             else {
    //                 EditingAccountClone.Id = await _budgetService.UpsertAccountAsync(EditingAccountClone);
    //
    //                 var openingBalance = new Transaction() {
    //                     AccountId = EditingAccountClone.IsLiability ? EditingAccountClone.Id : null,
    //                     ToAccountId = EditingAccountClone.IsLiability
    //                         ? null
    //                         : EditingAccountClone.Id,
    //                     AccountName = EditingAccountClone.IsLiability
    //                         ? EditingAccountClone.Name
    //                         : null,
    //                     ToAccountName = EditingAccountClone.IsLiability
    //                         ? null
    //                         : EditingAccountClone.Name,
    //                     Amount = EditingAccountClone.Balance,
    //                     TransactionDate = EditingAccountClone.BalanceAsOf,
    //                     TransactionId = Guid.NewGuid(),
    //                     ToFitId = Guid.NewGuid().ToString(),
    //                     FromFitId = Guid.NewGuid().ToString(),
    //                     Description = Constants.OpeningBalance,
    //                     Memo = Constants.OpeningBalance
    //                 };
    //
    //                 if (openingBalance.Amount != 0) {
    //                     try {
    //                         await _budgetService.UpsertTransactionAsync(openingBalance);
    //                     }
    //                     catch (Exception ex) {
    //                         Log.Error(ex, "Error upserting transaction in PropertyChanged.");
    //                     }
    //
    //                     List<Transaction> transactions = new List<Transaction>();
    //                     if (openingBalance.AccountId.HasValue) {
    //                         transactions.AddRange(
    //                             await _budgetService.GetAccountTransactionsAsync(openingBalance.AccountId.Value));
    //                     }
    //
    //                     if (openingBalance.ToAccountId.HasValue) {
    //                         transactions.AddRange(
    //                             await _budgetService.GetAccountTransactionsAsync(openingBalance.ToAccountId.Value));
    //                     }
    //
    //                     string json = JsonConvert.SerializeObject(transactions.ToList());
    //                     var reconciliationTransactions =
    //                         JsonConvert.DeserializeObject<List<TransactionViewModel>>(json);
    //                     if (reconciliationTransactions != null) {
    //                         if (openingBalance.AccountId.HasValue) {
    //                             await _reconciliationService.ReconcileAccountAsync(
    //                                 openingBalance.AccountId.Value,
    //                                 reconciliationTransactions,
    //                                 openingBalance.Amount,
    //                                 openingBalance.TransactionDate);
    //                         }
    //
    //                         if (openingBalance.ToAccountId.HasValue) {
    //                             await _reconciliationService.ReconcileAccountAsync(
    //                                 openingBalance.ToAccountId.Value,
    //                                 reconciliationTransactions,
    //                                 openingBalance.Amount,
    //                                 openingBalance.TransactionDate);
    //                         }
    //                     }
    //                 }
    //             }
    //
    //             IsEditingAccount = false;
    //             EditingAccountClone = null;
    //
    //             await LoadAccountDataAsync();
    //
    //             if (selectedAccountId.HasValue) {
    //                 SelectedAccount = VisibleAccounts.FirstOrDefault(a => a.Id == selectedAccountId);
    //             }
    //
    //             await LoadPeriodDataAsync();
    //
    //             RequestProjectionRecalculation();
    //         }
    //         catch (Exception ex) {
    //             Log.Error(ex, "Error saving account.");
    //
    //             MessageBox.Show("Failed to save account. See log for details.", "Error", MessageBoxButton.OK,
    //                 MessageBoxImage.Error);
    //         }
    //     }
    // }


    public int TotalAccountItemsToSaveCount {
        get {
            int stagedCount = StagedAccounts?.Count ?? 0;
            bool hasActiveDraft = EditingAccountClone != null &&
                                  !string.IsNullOrWhiteSpace(EditingAccountClone.Name);
            return stagedCount + (hasActiveDraft ? 1 : 0);
        }
    }

    public bool CanSaveAccount => IsEditingAccount &&
                                  IsEditingAccountEnabled &&
                                  TotalAccountItemsToSaveCount > 0;

    private async Task SaveAccountAsync() {
        if (EditingAccountClone == null && !StagedAccounts.Any()) return;

        try {
            // 1. Stage current draft if valid
            if (EditingAccountClone != null && !string.IsNullOrWhiteSpace(EditingAccountClone.Name)) {
                // Run DataAnnotations validation on the object to highlight red fields in WPF
                bool isValid = EditingAccountClone.ValidateAllProperties();

                List<string> errors = EditingAccountClone.GetValidationErrors();
                if (!isValid || errors.Any()) {
                    // Force WPF to re-evaluate all bindings hanging off EditingAccountClone
                    OnPropertyChanged(nameof(EditingAccountClone));

                    MessageBox.Show(errors.FirstOrDefault() ?? "Please correct the highlighted errors before saving."
                        , "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (EditingAccountClone.Type == AccountType.CreditCard &&
                    (EditingAccountClone.AccountAprHistory == null ||
                     EditingAccountClone.AccountAprHistory.Count == 0)) {
                    MessageBox.Show("Before you can save this credit card, you need to set up your interest rates.",
                        "Incomplete Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
                    SetAccountAprRatesCommand.Execute(EditingAccountClone);
                    return;
                }

                StagedAccounts.Add(EditingAccountClone);

                OnPropertyChanged(nameof(TotalAccountItemsToSaveCount));
                OnPropertyChanged(nameof(CanSaveAccount));
                SaveAccountCommand.NotifyCanExecuteChanged();
            }

            if (!StagedAccounts.Any()) {
                MessageBox.Show("Please enter a name for the account.", "Validation Error", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // 2. Persist all staged items
            foreach (var item in StagedAccounts) {
                if (SelectedAccount != null && item.Id == SelectedAccount.Id) {
                    UpdateAccountFromClone(SelectedAccount, item);
                    await _budgetService.UpsertAccountAsync(SelectedAccount);
                }
                else {
                    item.Id = await _budgetService.UpsertAccountAsync(item);

                    var openingBalance = new Transaction() {
                        AccountId = item.IsLiability ? item.Id : null,
                        ToAccountId = item.IsLiability ? null : item.Id,
                        AccountName = item.IsLiability ? item.Name : null,
                        ToAccountName = item.IsLiability ? null : item.Name,
                        Amount = item.Balance,
                        TransactionDate = item.BalanceAsOf,
                        TransactionId = Guid.NewGuid(),
                        ToFitId = Guid.NewGuid().ToString(),
                        FromFitId = Guid.NewGuid().ToString(),
                        Description = Constants.OpeningBalance,
                        Memo = Constants.OpeningBalance
                    };

                    if (openingBalance.Amount != 0) {
                        await _budgetService.UpsertTransactionAsync(openingBalance);

                        List<Transaction> transactions = new();
                        if (openingBalance.AccountId.HasValue) {
                            transactions.AddRange(
                                await _budgetService.GetAccountTransactionsAsync(openingBalance.AccountId.Value));
                        }

                        if (openingBalance.ToAccountId.HasValue) {
                            transactions.AddRange(
                                await _budgetService.GetAccountTransactionsAsync(openingBalance.ToAccountId.Value));
                        }

                        string json = JsonConvert.SerializeObject(transactions.ToList());
                        var reconciliationTransactions =
                            JsonConvert.DeserializeObject<List<TransactionViewModel>>(json);
                        if (reconciliationTransactions != null) {
                            if (openingBalance.AccountId.HasValue) {
                                await _reconciliationService.ReconcileAccountAsync(
                                    openingBalance.AccountId.Value, reconciliationTransactions, openingBalance.Amount,
                                    openingBalance.TransactionDate);
                            }

                            if (openingBalance.ToAccountId.HasValue) {
                                await _reconciliationService.ReconcileAccountAsync(
                                    openingBalance.ToAccountId.Value, reconciliationTransactions, openingBalance.Amount,
                                    openingBalance.TransactionDate);
                            }
                        }
                    }
                }
            }

            int countSaved = StagedAccounts.Count;

            // 3. Reset editor state & clear staged collection
            if (EditingAccountClone != null) {
                EditingAccountClone.PropertyChanged -= EditingAccountClone_PropertyChanged;
            }

            IsEditingAccount = false;
            IsEditingAccountEnabled = false;
            EditingAccountClone = null;
            StagedAccounts.Clear();

            OnPropertyChanged(nameof(TotalAccountItemsToSaveCount));
            OnPropertyChanged(nameof(CanSaveAccount));
            SaveAccountCommand.NotifyCanExecuteChanged();

            // 4. Refresh UI once
            await LoadAccountDataAsync();
            await LoadPeriodDataAsync();
            RequestProjectionRecalculation();

            ShowSuccessToast($"Successfully saved {countSaved} account{(countSaved > 1 ? "s" : "")}.");
        }
        catch (Exception ex) {
            Log.Error(ex, "Error saving batch accounts.");
            MessageBox.Show("Failed to save accounts. See log for details.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void UpdateAccountFromClone(Account target, Account clone) {
        try {
            target.Name = clone.Name;
            target.BankName = clone.BankName;
            target.Balance = clone.Balance;
            target.BalanceAsOf = clone.BalanceAsOf;
            target.AnnualGrowthRate = clone.AnnualGrowthRate;
            target.IncludeInTotal = clone.IncludeInTotal;
            target.Type = clone.Type;
            target.HexColor = clone.HexColor;
            target.IsPrimary = clone.IsPrimary;

            if ((clone.IsLoanAccount) && clone.MortgageDetails != null) {
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

                if (clone.AccountAprHistory != null) {
                    target.AccountAprHistory =
                        JsonConvert.DeserializeObject<List<AccountAprHistory>>(
                            JsonConvert.SerializeObject(clone.AccountAprHistory));
                }
                else {
                    target.AccountAprHistory = null;
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error updating account from clone.");
        }
    }

    private void CancelAccount() {
        try {
            if (EditingAccountClone != null) {
                EditingAccountClone.PropertyChanged -= EditingAccountClone_PropertyChanged;
            }

            IsEditingAccount = false;
            IsEditingAccountEnabled = false;
            EditingAccountClone = null;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error cancelling account edit.");
        }
    }

    private async Task DeleteAccountAsync() {
        if (EditingAccountClone == null) return;

        try {
            var affectedPaychecks = Paychecks.Where(x =>
                x.AccountId == EditingAccountClone.Id && (x.EndDate == null || x.EndDate > DateTime.Now)).ToList();
            var affectedBills = Bills.Where(x =>
                (x.AccountId == EditingAccountClone.Id || x.ToAccountId == EditingAccountClone.Id) && x.IsActive &&
                !x.IsArchived).ToList();
            var affectedBuckets = Buckets.Where(x => x.AccountId == EditingAccountClone.Id && !x.IsArchived).ToList();

            if (affectedPaychecks.Any() || affectedBills.Any() || affectedBuckets.Any()) {
                var availableAccounts = Accounts.Where(a => a.Id != EditingAccountClone.Id && !a.IsArchived).ToList();
                var vm = new ReassignAccountDependenciesViewModel(affectedPaychecks, affectedBills, affectedBuckets,
                    availableAccounts, EditingAccountClone.Id!);
                var dialog = new ReassignAccountDependenciesDialog(vm) {
                    Owner = Application.Current.MainWindow
                };

                if (dialog.ShowDialog() == true) {
                    foreach (var pItem in vm.Paychecks) {
                        pItem.Item.AccountId = pItem.TargetAccountId;
                        await _budgetService.UpsertPaycheckAsync(pItem.Item);
                    }

                    foreach (var bItem in vm.Bills) {
                        bItem.Bill.AccountId = bItem.TargetAccountId;
                        bItem.Bill.ToAccountId = bItem.TargetToAccountId;
                        await _budgetService.UpsertBillAsync(bItem.Bill);
                    }

                    foreach (var bItem in vm.Buckets) {
                        bItem.Item.AccountId = bItem.TargetAccountId;
                        await _budgetService.UpsertBucketAsync(bItem.Item, null);
                    }
                }
                else {
                    return;
                }
            }

            var messageBoxResult = MessageBox.Show(
                "Are you sure you want to delete this account?",
                "Delete Confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (messageBoxResult == MessageBoxResult.Yes) {
                // Pre-delete safety backup
                _budgetService.CreateRollingBackup(BackupReason.PreDeleteAccount);

                await _budgetService.DeleteAccountAsync(EditingAccountClone.Id);
                IsEditingAccount = false;
                IsEditingAccountEnabled = false;
                EditingAccountClone = null;
                await LoadAccountDataAsync();
                await LoadPeriodDataAsync();
                RequestProjectionRecalculation();
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error deleting account.");

            MessageBox.Show("Failed to delete account. See log for details.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task DeleteSelectedAccountAsync() {
        if (SelectedAccount == null) return;

        try {
            var affectedPaychecks = Paychecks.Where(x =>
                x.AccountId == SelectedAccount.Id && (x.EndDate == null || x.EndDate > DateTime.Now)).ToList();
            var affectedBills = Bills.Where(x =>
                (x.AccountId == SelectedAccount.Id || x.ToAccountId == SelectedAccount.Id) && x.IsActive &&
                !x.IsArchived).ToList();
            var affectedBuckets = Buckets.Where(x => x.AccountId == SelectedAccount.Id && !x.IsArchived).ToList();

            if (affectedPaychecks.Any() || affectedBills.Any() || affectedBuckets.Any()) {
                var availableAccounts = Accounts.Where(a => a.Id != SelectedAccount.Id && !a.IsArchived).ToList();
                var vm = new ReassignAccountDependenciesViewModel(affectedPaychecks, affectedBills, affectedBuckets,
                    availableAccounts, SelectedAccount.Id!);
                var dialog = new ReassignAccountDependenciesDialog(vm) {
                    Owner = Application.Current.MainWindow
                };

                if (dialog.ShowDialog() == true) {
                    foreach (var pItem in vm.Paychecks) {
                        pItem.Item.AccountId = pItem.TargetAccountId;
                        await _budgetService.UpsertPaycheckAsync(pItem.Item);
                    }

                    foreach (var bItem in vm.Bills) {
                        bItem.Bill.AccountId = bItem.TargetAccountId;
                        bItem.Bill.ToAccountId = bItem.TargetToAccountId;
                        await _budgetService.UpsertBillAsync(bItem.Bill);
                    }

                    foreach (var bItem in vm.Buckets) {
                        bItem.Item.AccountId = bItem.TargetAccountId;
                        await _budgetService.UpsertBucketAsync(bItem.Item, null);
                    }
                }
                else {
                    return;
                }
            }

            var messageBoxResult = MessageBox.Show(
                $"Are you sure you want to delete this account?\r\n\r\n{SelectedAccount.Name}",
                "Delete Confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (messageBoxResult == MessageBoxResult.Yes) {
                await _budgetService.DeleteAccountAsync(SelectedAccount.Id);
                IsEditingAccount = false;
                IsEditingAccountEnabled = false;
                SelectedAccount = null;
                await LoadAccountDataAsync();
                await LoadPeriodDataAsync();
                RequestProjectionRecalculation();
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error deleting account.");

            MessageBox.Show("Failed to delete account. See log for details.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    #endregion

    #region Helpers

    public string StrategyTakeawayPrimary {
        get {
            try {
                bool netWorthImproved = SnowballNetWorthImprovement > 1.00m;
                bool netWorthWorse = SnowballNetWorthImprovement < -1.00m;
                bool reducedDebt = SnowballDebtReductionVsStandard > 1.00m;
                bool increasedDebt = SnowballDebtReductionVsStandard < -1.00m;

                string primaryAnalysis;

                if (reducedDebt && netWorthImproved) {
                    primaryAnalysis =
                        $"Clear Win: You eliminate {Math.Abs(SnowballDebtReductionVsStandard):C0} in debt while growing your net worth by an extra {Math.Abs(SnowballNetWorthImprovement):C0}.";
                }
                else if (increasedDebt && netWorthImproved) {
                    primaryAnalysis =
                        $"Wealth Growth Focus: Investing extra cash boosts your net worth by {Math.Abs(SnowballNetWorthImprovement):C0}, but leaves {Math.Abs(SnowballDebtReductionVsStandard):C0} more debt balance than the standard plan.";
                }
                else if (reducedDebt && netWorthWorse) {
                    primaryAnalysis =
                        $"Risk Reduction Focus: Pays off {Math.Abs(SnowballDebtReductionVsStandard):C0} more debt for peace of mind, though net worth ends up {Math.Abs(SnowballNetWorthImprovement):C0} lower than investing.";
                }
                else if (increasedDebt && netWorthWorse) {
                    primaryAnalysis =
                        $"Suboptimal Strategy: This configuration increases your debt by {Math.Abs(SnowballDebtReductionVsStandard):C0} and lowers your final net worth by {Math.Abs(SnowballNetWorthImprovement):C0}.";
                }
                else if (netWorthImproved) {
                    primaryAnalysis =
                        $"Net Worth Boost: Your net worth increases by {Math.Abs(SnowballNetWorthImprovement):C0} with no change to your debt payoff trajectory.";
                }
                else if (reducedDebt) {
                    primaryAnalysis =
                        $"Debt Payoff Boost: You eliminate {Math.Abs(SnowballDebtReductionVsStandard):C0} more debt with no overall impact on your final net worth.";
                }
                else {
                    primaryAnalysis = "Strategy matches your standard baseline plan.";
                }

                return $"{primaryAnalysis}";
            }
            catch (Exception ex) {
                Log.Error(ex, "Error getting StrategyTakeawayPrimary.");

                return string.Empty;
            }
        }
    }

    public string StrategyTakeawayDilemma {
        get {
            string dilemmaExplanation =
                "Choosing between paying down debt or investing involves a trade-off: debt paydown offers a guaranteed return and lowers monthly liabilities, while investing aims for higher long-term wealth growth, with no guarantee—at the cost and potential worry of carrying debt longer.";

            return $"{dilemmaExplanation}";
        }
    }

    private async Task CalculateProjectionsAsync(CancellationToken cancellationToken = default) {
        try {
            IsProjecting = true;
            IsSnowballProjecting = SnowballOptions?.EnableSnowball == true;
            SnowballAnalysisText = "Analyzing strategy...";

            var showReconciled = true;
            var currentPeriodDate = CurrentPeriodDate;
            var projectionStartDate = ProjectionStartDate;
            var projectionEndDate = ProjectionEndDate;
            var useAutoSweep = UseAutoSweep;
            //var allocation = EditableAllocations;

            var snowballOptions = SnowballOptions;
            bool isSnowballEnabled = snowballOptions?.EnableSnowball == true;

            var (resultList, snowballList, negativeAccounts) = await Task.Run(async () => {
                cancellationToken.ThrowIfCancellationRequested();

                var paychecks = await _budgetService.GetAllPaychecksAsync();
                var bills = await _budgetService.GetAllBillsAsync();
                var buckets = await _budgetService.GetAllBucketsAsync();
                var periodBills = await _budgetService.GetAllPeriodBillsAsync();
                var periodBuckets = await _budgetService.GetAllPeriodBucketsAsync();
                var allocations = (await _budgetService.GetAllAllocationsAsync()).ToList();
                cancellationToken.ThrowIfCancellationRequested();

                List<AccountReconciliation>? reconciliations = null;

                var start = currentPeriodDate == DateTime.MinValue ? DateTime.Today : currentPeriodDate;
                if (projectionStartDate.HasValue) start = projectionStartDate.Value;

                var accounts = (await _budgetService.GetAllAccountsAsOfAsync(start.AddDays(-1), true)).ToList();
                var end = projectionEndDate;
                if (end < start) end = start.AddYears(1);
                //
                // var rawPaycheckTransactions = await _budgetService.GetAllPaycheckTransactionsAsync();
                // var rawBillTransactions = await _budgetService.GetBillTransactionsAsync();
                // var rawBucketTransactions = await _budgetService.GetBucketTransactionsAsync();
                var transactions =
                    (await _budgetService.GetAllTransactionsAsync(start.AddDays(-90), end.AddDays(365))).ToList();

                var rawPaycheckTransactions = transactions.Where(t => t.PaycheckId != null).ToList();
                var rawBillTransactions = transactions.Where(t => t.BillId != null).ToList();
                var rawBucketTransactions = transactions.Where(t => t.BucketId != null).ToList();

                cancellationToken.ThrowIfCancellationRequested();

                var paycheckTransactions = rawPaycheckTransactions
                    .Select(x => new Transaction {
                        Id = x.Id,
                        PaycheckId = x.PaycheckId,
                        PaycheckOccurrenceDate = x.PaycheckOccurrenceDate,
                        TransactionDate = (x.PaycheckOccurrenceDate != null &&
                                           x.TransactionDate != x.PaycheckOccurrenceDate)
                            ? x.PaycheckOccurrenceDate.Value
                            : x.TransactionDate,
                        Amount = x.Amount,
                        AccountId = x.AccountId
                    }).ToList();

                var allTransactions = transactions.Select(x => {
                    var copy = x.CloneReflection();

                    if (copy.PaycheckId != null && copy.PaycheckOccurrenceDate != null &&
                        copy.TransactionDate != copy.PaycheckOccurrenceDate) {
                        copy.TransactionDate = copy.PaycheckOccurrenceDate.Value;
                    }

                    return copy;
                }).ToList();

                var results = _projectionEngine.CalculateProjections(
                    paycheckTransactions,
                    rawBillTransactions.ToList(),
                    rawBucketTransactions.ToList(),
                    allTransactions,
                    start, end, accounts, paychecks.ToList(), bills.ToList(), buckets.ToList(),
                    allocations,
                    periodBills.ToList(), periodBuckets.ToList(), transactions.ToList(), reconciliations?.ToList(),
                    showReconciled, true, useAutoSweep, null);

                cancellationToken.ThrowIfCancellationRequested();

                var list = results.ToList();
                List<ProjectionItem> snowballList;

                if (isSnowballEnabled) {
                    var snowballResults = _projectionEngine.CalculateProjections(
                        paycheckTransactions,
                        rawBillTransactions.ToList(),
                        rawBucketTransactions.ToList(),
                        allTransactions,
                        start, end, accounts, paychecks.ToList(), bills.ToList(), buckets.ToList(),
                        allocations,
                        periodBills.ToList(), periodBuckets.ToList(), transactions.ToList(), reconciliations?.ToList(),
                        showReconciled, true, useAutoSweep, snowballOptions);

                    snowballList = snowballResults.ToList();
                }
                else {
                    snowballList = new List<ProjectionItem>();
                }

                var breachedAccounts = new HashSet<string>();
                foreach (var item in list) {
                    if (item.IsBelowFloor) {
                        var targetAcc = accounts.FirstOrDefault(a => a.Id == (item.FromAccountId ?? item.ToAccountId));
                        if (targetAcc != null) {
                            breachedAccounts.Add(targetAcc.Name);
                        }
                    }

                    foreach (var acc in accounts) {
                        if (acc.Type is not (AccountType.Checking or AccountType.Savings)) continue;

                        if (item.AccountBalances.TryGetValue(acc.Name, out decimal balance) && balance < 0) {
                            breachedAccounts.Add(acc.Name);
                        }
                    }
                }

                return (list, snowballList, breachedAccounts);
            }, cancellationToken);

            if (cancellationToken.IsCancellationRequested) return;

            Projections.Clear();
            Projections.AddRange(resultList);

            SnowballProjections.Clear();
            if (isSnowballEnabled) {
                SnowballProjections.AddRange(snowballList);
                UpdateSnowballAnalysis(resultList, snowballList);
            }
            else {
                ShowSnowballAnalysis = false;
            }

            OnPropertyChanged(nameof(TotalLiquidCash));
            OnPropertyChanged(nameof(EnvelopeFloorRequirements));
            OnPropertyChanged(nameof(AccumulatingDrawdownReserves));
            OnPropertyChanged(nameof(UpcomingBillsRequirements));
            OnPropertyChanged(nameof(UnspentStandardEnvelopeRequirements));
            OnPropertyChanged(nameof(TotalRequiredReserves));
            OnPropertyChanged(nameof(UnallocatedSurplusCash));
            OnPropertyChanged(nameof(AvailableSurplusPool));
            OnPropertyChanged(nameof(TotalActiveSweepAmount));
            OnPropertyChanged(nameof(RetainedCheckingBuffer));
            OnPropertyChanged(nameof(RecommendedDebtAllocation));
            OnPropertyChanged(nameof(RecommendedInvestmentAllocation));

            OnPropertyChanged(nameof(LowestProjectedCheckingBalance));
            OnPropertyChanged(nameof(ReadinessStatus));
            OnPropertyChanged(nameof(ReadinessStatusTitle));
            OnPropertyChanged(nameof(ReadinessSuggestionMessage));
            OnPropertyChanged(nameof(ReadinessStatusHeaderBrush));
            OnPropertyChanged(nameof(ReadinessStatusBackgroundBrush));
            OnPropertyChanged(nameof(ReadinessStatusBorderBrush));
            OnPropertyChanged(nameof(StrategyDisplayName));

            OnPropertyChanged(nameof(IsMinimumBalanceCushionBreached));
            OnPropertyChanged(nameof(HasOverspentEnvelopes));
            OnPropertyChanged(nameof(IsUnpaidBillsAlert));
            OnPropertyChanged(nameof(IsTotalCommittedFundsBreached));

            RefreshUpcomingStrategyTasks();
            if (negativeAccounts.Any()) {
                string message =
                    $"Warning: The following accounts breach their balance floor in the projection: {string.Join(", ", negativeAccounts)}";
                ShowWarningToast(message);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) {
            Log.Error(ex, "Error calculating projections.");

            ShowWarningToast("Failed to calculate projections. Check logs.");
        }
        finally {
            if (!cancellationToken.IsCancellationRequested) {
                IsProjecting = false;
                IsSnowballProjecting = false;
            }
        }
    }

    public void ShowToast(string message) {
        try {
            Application.Current.Dispatcher.Invoke(() => {
                if (Toasts.Any(t => t.Message == message)) return;

                var toast = new ToastViewModel(message,
                    t => { Application.Current.Dispatcher.Invoke(() => Toasts.Remove(t)); });
                Toasts.Add(toast);
            });
        }
        catch (Exception ex) {
            Log.Error(ex, "Error showing toast.");
        }
    }

    public void ShowSuccessToast(string message) {
        try {
            Application.Current.Dispatcher.Invoke(() => {
                if (Toasts.Any(t => t.Message == message)) return;

                var toast = new ToastViewModel(message,
                    t => { Application.Current.Dispatcher.Invoke(() => Toasts.Remove(t)); }, ToastType.Success);
                Toasts.Add(toast);
            });
        }
        catch (Exception ex) {
            Log.Error(ex, "Error showing success toast.");
        }
    }

    public void ShowWarningToast(string message) {
        try {
            Application.Current.Dispatcher.Invoke(() => {
                if (Toasts.Any(t => t.Message == message)) return;

                var toast = new ToastViewModel(message,
                    t => { Application.Current.Dispatcher.Invoke(() => Toasts.Remove(t)); }, ToastType.Warning);
                Toasts.Add(toast);
            });
        }
        catch (Exception ex) {
            Log.Error(ex, "Error showing warning toast.");
        }
    }

    public List<PeriodBill> GetProjectedBillsForPeriod(DateTime periodStart) {
        try {
            var periodEnd = periodStart.AddDays(14);
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

    private async Task LoadDataAsync() {
        Log.Information("Loading all budget data.");
        _isLoadingData = true;
        try {
            await Task.Yield();

            await LoadAccountDataAsync();
            await Task.Yield();

            await LoadBillDataAsync();
            await Task.Yield();

            await LoadPaycheckDataAsync();
            await Task.Yield();

            await LoadBucketDataAsync();
            await Task.Yield();

            await LoadCategoryDataAsync();
            await Task.Yield();

            await LoadSubCategoryDataAsync();

            OnPropertyChanged(nameof(Accounts));

            Log.Information("Budget data loaded successfully. Accounts: {AccountCount}, Bills: {BillCount}",
                Accounts.Count, Bills.Count);
        }
        catch (Exception ex) {
            Log.Error(ex, "Failed to load budget data.");

            MessageBox.Show("Failed to load budget data. See log for details.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally {
            _isLoadingData = false;
        }
    }

    private async Task LoadAccountDataAsync() {
        Log.Information("Loading account data.");
        _isLoadingAccountData = true;
        try {
            var accounts = (await _budgetService.GetAllAccountsAsync(true)).ToList();
            if (accounts.All(a => !(a.Name == "Household Cash" && a.Type == AccountType.Cash))) {
                Log.Information("Household Cash account not found. Creating default.");
                var cashAccount = new Account {
                    Name = "Household Cash",
                    Type = AccountType.Cash,
                    Balance = 0,
                    IncludeInTotal = true
                };
                await _budgetService.UpsertAccountAsync(cashAccount);
                accounts = (await _budgetService.GetAllAccountsAsync(true)).ToList();
            }

            var accountBalances = (await _budgetService.GetAllAccountsAsOfAsync(DateTime.Now, true)).ToList();
            accounts = accounts.OrderBy(b => b.Name).ToList();
            foreach (var a in accounts) {
                a.Balance = accountBalances.SingleOrDefault(b => b.Id == a.Id)?.Balance ?? 0;
            }

            foreach (var a in Accounts) {
                a.PropertyChanged -= Item_PropertyChanged;
            }

            Accounts.Clear();
            VisibleAccounts.Clear();

            var visibleList = new List<Account>(accounts.Count);

            foreach (var a in accounts) {
                a.PropertyChanged += Item_PropertyChanged;
                if (!a.IsArchived) {
                    visibleList.Add(a);
                }
            }

            Accounts.AddRange(accounts);
            VisibleAccounts.AddRange(visibleList);

            RefreshExcludableAccounts();

            AccountsWithNone.Clear();

            var accountsWithNoneList = new List<Account>(accounts.Count + 1) {
                new Account { Id = 0, Name = "(None)" }
            };
            accountsWithNoneList.AddRange(accounts);
            AccountsWithNone.AddRange(accountsWithNoneList);

            ActiveAccountsWithNone.Clear();

            var filtered = accounts.Where(a => !a.IsArchived).ToList();
            var activeAccountsWithNoneList = new List<Account>(filtered.Count + 1) {
                new Account { Id = 0, Name = "(None)" }
            };
            activeAccountsWithNoneList.AddRange(filtered);
            ActiveAccountsWithNone.AddRange(activeAccountsWithNoneList);

            if (Accounts.Any(x => x.Type == AccountType.Checking && x.IsPrimary) &&
                Accounts.Any(x => x.Type == AccountType.CreditCard)) {
                UseAutoSweep = true;
                OnPropertyChanged(nameof(UseAutoSweep));
            }

            OnPropertyChanged(nameof(TotalLiquidCash));
            OnPropertyChanged(nameof(EnvelopeFloorRequirements));
            OnPropertyChanged(nameof(AccumulatingDrawdownReserves));
            OnPropertyChanged(nameof(UpcomingBillsRequirements));
            OnPropertyChanged(nameof(UnspentStandardEnvelopeRequirements));
            OnPropertyChanged(nameof(TotalRequiredReserves));
            OnPropertyChanged(nameof(UnallocatedSurplusCash));
            OnPropertyChanged(nameof(AvailableSurplusPool));
            OnPropertyChanged(nameof(TotalActiveSweepAmount));
            OnPropertyChanged(nameof(RetainedCheckingBuffer));
            OnPropertyChanged(nameof(RecommendedDebtAllocation));
            OnPropertyChanged(nameof(RecommendedInvestmentAllocation));
            OnPropertyChanged(nameof(IsMinimumBalanceCushionBreached));
            OnPropertyChanged(nameof(HasOverspentEnvelopes));
            OnPropertyChanged(nameof(IsUnpaidBillsAlert));
            OnPropertyChanged(nameof(IsTotalCommittedFundsBreached));
            Log.Information("Account data loaded successfully. Accounts: {AccountCount}",
                Accounts.Count);
        }
        catch (Exception ex) {
            Log.Error(ex, "Failed to load account data.");

            MessageBox.Show("Failed to load account data. See log for details.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally {
            _isLoadingAccountData = false;
        }
    }

    private ICollectionView _filteredBillsView = null!;
    public ICollectionView FilteredBillsView => _filteredBillsView;

    private async Task LoadBillDataAsync() {
        Log.Information("Loading bill data.");
        _isLoadingBillData = true;
        try {
            foreach (var item in Bills) {
                item.PropertyChanged -= Item_PropertyChanged;
            }

            foreach (var item in BillsWithNone) {
                item.PropertyChanged -= Item_PropertyChanged;
            }

            Bills.Clear();
            BillsWithNone.Clear();

            var billsList = (await _budgetService.GetAllBillsAsync(true))
                .OrderBy(b => b.DueDay)
                .ThenBy(b => b.Name)
                .ToList();

            foreach (var b in billsList) {
                b.PropertyChanged += Item_PropertyChanged;
            }

            var unarchivedBills = billsList.Where(b => !b.IsArchived).ToList();
            var billsWithNoneList = new List<Bill>(unarchivedBills.Count + 1) {
                new Bill { Id = 0, Name = "(None)" }
            };

            billsWithNoneList.AddRange(unarchivedBills);

            Bills.AddRange(billsList);
            BillsWithNone.AddRange(billsWithNoneList);

            Log.Information("Bill data loaded successfully. Bills: {BillCount}", Bills.Count);
        }
        catch (Exception ex) {
            Log.Error(ex, "Failed to load bill data.");

            MessageBox.Show("Failed to load bill data. See log for details.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally {
            _isLoadingBillData = false;
        }
    }

    private bool FilterBillItem(object item) {
        try {
            if (item is not Bill bill) return false;

            if (bill.Id == 0) return true;

            string searchText = EditingTransactionClone?.Description?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(searchText)) return true;

            //return bill.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            return bill.Name.StartsWith(searchText, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error filtering bill item.");

            return false;
        }
    }

    private async Task LoadBucketDataAsync() {
        Log.Information("Loading all bucket data.");
        _isLoadingBucketData = true;
        try {
            foreach (var item in Buckets) {
                item.PropertyChanged -= Item_PropertyChanged;
            }

            foreach (var item in BucketsWithNone) {
                item.PropertyChanged -= Item_PropertyChanged;
            }

            Buckets.Clear();
            BucketsWithNone.Clear();

            var bucketsList = (await _budgetService.GetAllBucketsAsync(true))
                .OrderBy(b => b.Name)
                .ToList();

            foreach (var b in bucketsList) {
                b.PropertyChanged += Item_PropertyChanged;
            }

            var unarchivedBuckets = bucketsList.Where(b => !b.IsArchived).ToList();
            var bucketsWithNoneList = new List<BudgetBucket>(unarchivedBuckets.Count + 1) {
                new BudgetBucket { Id = 0, Name = "(None)" }
            };
            bucketsWithNoneList.AddRange(unarchivedBuckets);

            Buckets.AddRange(bucketsList);
            BucketsWithNone.AddRange(bucketsWithNoneList);

            Log.Information("Bucket data loaded successfully. Buckets: {BucketCount}", Buckets.Count);
        }
        catch (Exception ex) {
            Log.Error(ex, "Failed to load bucket data.");

            MessageBox.Show("Failed to load bucket data. See log for details.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally {
            _isLoadingBucketData = false;
        }
    }

    private async Task LoadSubCategoryDataAsync() {
        Log.Information("Loading all sub category data.");
        _isLoadingSubCategoryData = true;
        try {
            foreach (var item in SubCategories) {
                item.PropertyChanged -= Item_PropertyChanged;
            }

            foreach (var item in SubCategoriesWithNone) {
                item.PropertyChanged -= Item_PropertyChanged;
            }

            SubCategories.Clear();
            SubCategoriesWithNone.Clear();

            var subCategoriesList = (await _budgetService.GetAllSubCategoriesAsync(true))
                .OrderBy(b => b.CategoryName)
                .ThenBy(b => b.SortOrder)
                .ThenBy(b => b.Name)
                .ToList();

            foreach (var b in subCategoriesList) {
                b.PropertyChanged += Item_PropertyChanged;
            }

            var unarchivedSubCategories = subCategoriesList.Where(b => !b.IsArchived).ToList();
            var subCategoriesWithNoneList = new List<SubCategory>(unarchivedSubCategories.Count + 1) {
                new SubCategory { Id = 0, Name = "(None)" }
            };
            subCategoriesWithNoneList.AddRange(unarchivedSubCategories);

            SubCategories.AddRange(subCategoriesList);
            SubCategoriesWithNone.AddRange(subCategoriesWithNoneList);

            Log.Information("Sub Category data loaded successfully. SubCategories: {SubCategoryCount}",
                SubCategories.Count);
        }
        catch (Exception ex) {
            Log.Error(ex, "Failed to load sub category data.");

            MessageBox.Show("Failed to load sub category data. See log for details.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally {
            _isLoadingSubCategoryData = false;
        }
    }

    private async Task LoadCategoryDataAsync() {
        Log.Information("Loading all category data.");
        _isLoadingCategoryData = true;
        try {
            foreach (var item in Categories) {
                item.PropertyChanged -= Item_PropertyChanged;
            }

            foreach (var item in CategoriesWithNone) {
                item.PropertyChanged -= Item_PropertyChanged;
            }

            Categories.Clear();

            var categoriesList = (await _budgetService.GetAllCategoriesAsync(true))
                .OrderBy(b => b.Name)
                .ToList();

            foreach (var b in categoriesList) {
                b.PropertyChanged += Item_PropertyChanged;
            }

            var unarchivedCategories = categoriesList.Where(b => !b.IsArchived).ToList();
            var categoriesWithNoneList = new List<Category>(unarchivedCategories.Count + 1) {
                new Category { Id = 0, Name = "(None)" }
            };
            categoriesWithNoneList.AddRange(unarchivedCategories);

            Categories.AddRange(categoriesList);
            CategoriesWithNone.AddRange(categoriesWithNoneList);

            Log.Information("Category data loaded successfully. Categories: {CategoryCount}",
                Categories.Count);
        }
        catch (Exception ex) {
            Log.Error(ex, "Failed to load category data.");

            MessageBox.Show("Failed to load category data. See log for details.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally {
            _isLoadingCategoryData = false;
        }
    }

    private async Task LoadPaycheckDataAsync() {
        Log.Information("Loading Paycheck data.");
        _isLoadingPaycheckData = true;
        try {
            foreach (var item in Paychecks) {
                item.PropertyChanged -= Item_PropertyChanged;
            }

            foreach (var item in PaychecksWithNone) {
                item.PropertyChanged -= Item_PropertyChanged;
            }

            Paychecks.Clear();
            PaychecksWithNone.Clear();

            var paychecksList = (await _budgetService.GetAllPaychecksAsync())
                .OrderBy(b => b.Name)
                .ToList();

            foreach (var b in paychecksList) {
                b.PropertyChanged += Item_PropertyChanged;
            }

            var paychecksWithNoneList = new List<Paycheck>(paychecksList.Count + 1) {
                new Paycheck { Id = 0, Name = "(None)" }
            };
            paychecksWithNoneList.AddRange(paychecksList);

            Paychecks.AddRange(paychecksList);
            OnPropertyChanged(nameof(HasMultiplePaychecks));
            PaychecksWithNone.AddRange(paychecksWithNoneList);

            Log.Information("Paycheck data loaded successfully. Paychecks: {PaycheckCount}", Paychecks.Count);
        }
        catch (Exception ex) {
            Log.Error(ex, "Failed to load Paycheck data.");

            MessageBox.Show("Failed to load Paycheck data. See log for details.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally {
            _isLoadingPaycheckData = false;
        }
    }

    private void LoadPaychecks() {
        try {
            var allPaychecks = Paychecks.ToList();
            if (allPaychecks.Count == 0) {
                CurrentPeriodDate = DateTime.Today;
                NextPeriodDate = GetNextPeriodDate(CurrentPeriodDate);
                return;
            }

            var temp = new List<Paycheck>(allPaychecks.Count);
            foreach (var b in allPaychecks) {
                temp.Add(b);
            }

            PeriodPaychecks.Clear();
            PeriodPaychecks.AddRange(temp);

            SetCurrentPeriodDate();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error loading paychecks into period view.");
        }
    }

    private async Task LoadPeriodDataAsync() {
        try {
            await LoadPeriodBillsAsync();
            await LoadPeriodBucketsAsync();
            await LoadPeriodTransactionsAsync();
            await ApplyTransactionAmounts();
            UpdateWarningMetrics();
            UpdateBucketWarningMetrics();

            OnPropertyChanged(nameof(TotalLiquidCash));
            OnPropertyChanged(nameof(EnvelopeFloorRequirements));
            OnPropertyChanged(nameof(AccumulatingDrawdownReserves));
            OnPropertyChanged(nameof(UpcomingBillsRequirements));
            OnPropertyChanged(nameof(UnspentStandardEnvelopeRequirements));
            OnPropertyChanged(nameof(TotalRequiredReserves));
            OnPropertyChanged(nameof(UnallocatedSurplusCash));
            OnPropertyChanged(nameof(AvailableSurplusPool));
            OnPropertyChanged(nameof(TotalActiveSweepAmount));
            OnPropertyChanged(nameof(RetainedCheckingBuffer));
            OnPropertyChanged(nameof(RecommendedDebtAllocation));
            OnPropertyChanged(nameof(RecommendedInvestmentAllocation));
            OnPropertyChanged(nameof(IsMinimumBalanceCushionBreached));
            OnPropertyChanged(nameof(HasOverspentEnvelopes));
            OnPropertyChanged(nameof(IsUnpaidBillsAlert));
            OnPropertyChanged(nameof(IsTotalCommittedFundsBreached));
        }
        catch (Exception ex) {
            Log.Error(ex, "Error loading period data.");
        }
    }

    private async Task ApplyTransactionAmounts() {
        try {
            // Expand lookup window backwards by 7 days to catch bills paid in the prior paycheck period
            var thresholdStartDate = CurrentPeriodDate.AddDays(-7);

            // Fetch paid bill transactions within the extended window
            var paidBillsInRange =
                (await _budgetService.GetBillsPaidInRange(thresholdStartDate, NextPeriodDate)).ToList();

            if (CurrentPeriodBills.Count != 0) {
                foreach (var pb in CurrentPeriodBills) {
                    if (pb.TransactionAmount == 0) {
                        // 1. Check current period transactions
                        decimal currentAmount = CurrentPeriodTransactions
                            .Where(t => t.BillId == pb.BillId)
                            .Sum(t => t.Amount);

                        // 2. Fall back to threshold-range if paid early/late across period boundaries
                        if (currentAmount == 0) {
                            currentAmount = paidBillsInRange
                                .Where(x => x.billId == pb.BillId)
                                .Sum(x => x.amount);
                        }

                        pb.TransactionAmount = currentAmount;
                    }

                    if (pb.TransactionAmount != 0) {
                        pb.IsPaid = true;
                    }
                }
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

    private async Task LoadPeriodBillsAsync() {
        try {
            var pBills = (await _budgetService.GetPeriodBillsAsync(CurrentPeriodDate)).ToList();
            var projectedBillsForPeriod = GetProjectedBillsForPeriod(CurrentPeriodDate);

            var mergedBills = new List<PeriodBill>();

            foreach (var pb in projectedBillsForPeriod) {
                // Find existing saved period bill record matching this underlying BillId
                var existing = pBills.FirstOrDefault(p => p.BillId == pb.BillId);

                if (existing != null) {
                    // Use the persisted historical snapshot amount and status
                    pb.Id = existing.Id;
                    pb.ActualAmount = existing.ActualAmount;
                    pb.IsPaid = existing.IsPaid;
                    pb.TransactionAmount = existing.TransactionAmount;
                    pb.FitId = existing.FitId;
                }

                mergedBills.Add(pb);
            }

            // Include any orphan period bills saved specifically for this period that weren't in projections
            foreach (var p in pBills) {
                if (!mergedBills.Any(m => m.BillId == p.BillId)) {
                    mergedBills.Add(p);
                }
            }

            var sortedBills = mergedBills.OrderBy(pb => pb.DueDate).ToList();

            CurrentPeriodBills.Clear();
            CurrentPeriodBills.AddRange(sortedBills);

            UpdateWarningMetrics();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error loading period bills.");
        }
    }
    
    // private async Task LoadPeriodBillsAsync() {
    //     try {
    //         var pBills = (await _budgetService.GetPeriodBillsAsync(CurrentPeriodDate)).ToList();
    //         pBills = pBills.OrderBy(pb => pb.DueDate).ToList();
    //         var projectedBillsForPeriod = GetProjectedBillsForPeriod(CurrentPeriodDate);
    //
    //         foreach (var pb in projectedBillsForPeriod) {
    //             var periodBill = pBills.FirstOrDefault(existing =>
    //                 existing.BillId == pb.BillId && existing.PeriodDate.Date == pb.PeriodDate.Date);
    //             if (periodBill != null) {
    //                 pb.IsPaid = periodBill.IsPaid;
    //                 pb.TransactionAmount = periodBill.ActualAmount;
    //                 pb.ActualAmount = periodBill.ActualAmount;
    //                 pb.Id = periodBill.Id;
    //             }
    //         }
    //
    //         projectedBillsForPeriod = projectedBillsForPeriod.OrderBy(pb => pb.DueDate).ToList();
    //
    //         CurrentPeriodBills.Clear();
    //         CurrentPeriodBills.AddRange(projectedBillsForPeriod);
    //
    //         UpdateWarningMetrics();
    //     }
    //     catch (Exception ex) {
    //         Log.Error(ex, "Error loading period bills.");
    //     }
    // }

    private async Task LoadPeriodBucketsAsync() {
    try {
        var pBuckets = (await _budgetService.GetPeriodBucketsIncludingMonthlyAsync(CurrentPeriodDate)).ToList();
        var finalBuckets = new List<PeriodBucket>();

        foreach (var bucket in Buckets) {
            List<BucketPaycheckAllocation> allocations = new();
            if (bucket.Type != BucketType.UpfrontFloor) {
                allocations = (await _budgetService.GetAllocationsForBucketAsync(bucket.Id)).ToList();
            }

            bool isLinkedToCurrentPaycheck = allocations.Any(a => a.PaycheckId == SelectedPeriodPaycheckId);
            bool isStandaloneOrMonthly = !allocations.Any() || ShowByMonth;

            if (isStandaloneOrMonthly || isLinkedToCurrentPaycheck) {
                // 1. Check if there is an exact match for this period date or bucket ID
                var existing = pBuckets
                    .Where(pb => pb.BucketId == bucket.Id)
                    .OrderByDescending(pb => pb.PeriodDate.Date == CurrentPeriodDate.Date) // Prefer exact match
                    .FirstOrDefault();

                if (existing != null) {
                    finalBuckets.Add(existing);
                }
                else {
                    // 2. Fall back to creating a new transient period bucket with default expected amount
                    var pb = new PeriodBucket {
                        BucketId = bucket.Id,
                        BucketName = bucket.Name,
                        PeriodDate = !allocations.Any()
                            ? new DateTime(CurrentPeriodDate.Year, CurrentPeriodDate.Month, 1)
                            : CurrentPeriodDate,
                        ActualAmount = bucket.ExpectedAmount,
                        IsPaid = false,
                        FitId = Guid.NewGuid(),
                        BucketType = bucket.Type
                    };
                    finalBuckets.Add(pb);
                }
            }
        }

        CurrentPeriodBuckets.Clear();
        CurrentPeriodBuckets.AddRange(finalBuckets.OrderBy(b => b.BucketName));
    }
    catch (Exception ex) {
        Log.Error(ex, "Error loading period buckets.");
    }
}
    
    // private async Task LoadPeriodBucketsAsync() {
    //     try {
    //         //, NextPeriodDate.AddDays(-1)
    //         var pBuckets = (await _budgetService.GetPeriodBucketsIncludingMonthlyAsync(CurrentPeriodDate)).ToList();
    //
    //         foreach (var bucket in Buckets) {
    //             List<BucketPaycheckAllocation> allocations = new();
    //             if (bucket.Type != BucketType.UpfrontFloor) {
    //                 allocations = (await _budgetService.GetAllocationsForBucketAsync(bucket.Id)).ToList();
    //             }
    //
    //             bool isLinkedToCurrentPaycheck = allocations.Any(a => a.PaycheckId == SelectedPeriodPaycheckId);
    //             bool isStandaloneOrMonthly = !allocations.Any() || ShowByMonth;
    //
    //             if (isStandaloneOrMonthly || isLinkedToCurrentPaycheck) {
    //                 if (pBuckets.All(existing => existing.BucketId != bucket.Id)) {
    //                     var pb = new PeriodBucket {
    //                         BucketId = bucket.Id,
    //                         BucketName = bucket.Name,
    //                         PeriodDate = !allocations.Any()
    //                             ? new DateTime(CurrentPeriodDate.Year, CurrentPeriodDate.Month, 1)
    //                             : CurrentPeriodDate,
    //                         ActualAmount = bucket.ExpectedAmount,
    //                         IsPaid = false,
    //                         FitId = Guid.NewGuid(),
    //                         BucketType = bucket.Type
    //                     };
    //                     pBuckets.Add(pb);
    //                 }
    //             }
    //         }
    //
    //         CurrentPeriodBuckets.Clear();
    //         CurrentPeriodBuckets.AddRange(pBuckets);
    //     }
    //     catch (Exception ex) {
    //         Log.Error(ex, "Error loading period buckets.");
    //     }
    // }

    private DateTime GetNextPeriodDate(DateTime currentPeriodStart) {
        try {
            if (ShowByMonth) {
                return currentPeriodStart.AddMonths(1);
            }

            var allPaycheckDates = new List<DateTime>();
            var end = currentPeriodStart.AddYears(1);
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
            var nextDate = sortedDates.FirstOrDefault(d => d > currentPeriodStart);

            return nextDate == DateTime.MinValue ? currentPeriodStart.AddDays(14) : nextDate;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error getting next period date.");

            return currentPeriodStart.AddDays(14);
        }
    }

    private async Task LoadPeriodTransactionsAsync() {
        try {
            var transactions = (await _budgetService.GetTransactionsAsync(CurrentPeriodDate, NextPeriodDate)).ToList();
            transactions = transactions.OrderBy(pb => pb.TransactionDate).ToList();

            var temp = new List<Transaction>(transactions.Count);
            foreach (var b in transactions) {
                temp.Add(b);
            }

            CurrentPeriodTransactions.Clear();
            CurrentPeriodTransactions.AddRange(temp);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error loading period transactions.");
        }
    }

    private void InitializePeriod() {
        try {
            if (ShowByMonth) {
                CurrentPeriodDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                NextPeriodDate = GetNextPeriodDate(CurrentPeriodDate);
                return;
            }

            LoadPaychecks();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error initializing period.");
        }
    }

    private void InitializeNavigationMenu() {
        try {
            NavigationItems.Clear();

            NavigationItems.Add(new NavigationItemViewModel {
                Title = "Dashboard",
                IconKind = "Speedometer",
                TabIndex = 0
            });
            NavigationItems.Add(new NavigationItemViewModel {
                Title = "Accounts",
                IconKind = "Bank",
                TabIndex = 1
            });
            NavigationItems.Add(new NavigationItemViewModel {
                Title = "Bills",
                IconKind = "Receipt",
                TabIndex = 2
            });
            NavigationItems.Add(new NavigationItemViewModel {
                Title = "Envelopes",
                IconKind = "FolderStar",
                TabIndex = 3
            });
            NavigationItems.Add(new NavigationItemViewModel {
                Title = "Transactions",
                IconKind = "FormatListBulleted",
                TabIndex = 4
            });
            NavigationItems.Add(new NavigationItemViewModel {
                Title = "Projections",
                IconKind = "ChartLine",
                TabIndex = 5
            });
            NavigationItems.Add(new NavigationItemViewModel {
                Title = "Settings",
                IconKind = "Cog",
                TabIndex = 6
            });

            SelectedNavigationItem = NavigationItems.FirstOrDefault();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error initializing navigation menu.");
        }
    }

    private async Task NavigatePeriodAsync(int direction) {
        try {
            if (ShowByMonth) {
                CurrentPeriodDate = CurrentPeriodDate.AddMonths(direction);
                NextPeriodDate = GetNextPeriodDate(CurrentPeriodDate);
                await LoadPeriodDataAsync();
                return;
            }

            var oldestTransaction = await _budgetService.GetOldestTransactionAsync();

            var allPaycheckDates = new List<DateTime>();
            var end = DateTime.Today.AddYears(1);
            if (oldestTransaction.HasValue) {
                allPaycheckDates.Add(oldestTransaction.Value);
            }

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

            NextPeriodDate = GetNextPeriodDate(CurrentPeriodDate);
            await LoadPeriodDataAsync();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error navigating period.");
        }
    }

    private async Task ReconcileAccountAsync() {
        if (SelectedAccount == null) return;
        try {
            var window = new ReconciliationWindow(SelectedAccount, _budgetService) {
                Owner = Application.Current.MainWindow
            };
            window.ShowDialog();

            var selectedAccountId = SelectedAccount.Id;

            await LoadAccountDataAsync();

            SelectedAccount = VisibleAccounts.FirstOrDefault(a => a.Id == selectedAccountId);

            await LoadPeriodDataAsync();

            RequestProjectionRecalculation();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error showing reconciliation window.");

            MessageBox.Show("Failed to open reconciliation window. See log for details.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task ImportAccountAsync() {
        if (SelectedAccount == null) return;
        try {
            var window = new ImportReconciliationWindow(SelectedAccount, _budgetService) {
                Owner = Application.Current.MainWindow
            };
            window.ShowDialog();

            var selectedAccountId = SelectedAccount.Id;

            await LoadAccountDataAsync();

            SelectedAccount = VisibleAccounts.FirstOrDefault(a => a.Id == selectedAccountId);

            await LoadPeriodDataAsync();

            RequestProjectionRecalculation();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error showing import window.");

            MessageBox.Show("Failed to open import window. See log for details.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task SetAccountAprRatesAsync() {
        if (EditingAccountClone is not { Type: AccountType.CreditCard }) return;
        try {
            EditingAccountClone.AccountAprHistory ??= [];
            var window = new AccountAprHistoryWindow(EditingAccountClone, _budgetService) {
                Owner = Application.Current.MainWindow
            };
            window.ShowDialog();
            RequestProjectionRecalculation();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error showing APR history window.");

            MessageBox.Show("Failed to open interest rate window. See log for details.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }


    private void RefreshPaychecks() {
        try {
            var allPaychecks = Paychecks.ToList();
            if (allPaychecks.Count == 0) {
                CurrentPeriodDate = DateTime.Today;
                NextPeriodDate = GetNextPeriodDate(CurrentPeriodDate);
                return;
            }

            var temp = new List<Paycheck>(allPaychecks.Count);
            foreach (var b in allPaychecks) {
                temp.Add(b);
            }

            PeriodPaychecks.Clear();
            PeriodPaychecks.AddRange(temp);
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
                NextPeriodDate = GetNextPeriodDate(CurrentPeriodDate);
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

            NextPeriodDate = GetNextPeriodDate(CurrentPeriodDate);
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

    private void ExportTransactions() {
        try {
            var viewModel = new ExportTransactionsViewModel(_budgetService);
            var dialog = new ExportTransactionsDialog(viewModel) {
                Owner = Application.Current.MainWindow
            };
            dialog.ShowDialog();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error exporting transactions.");
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
            MessageBox.Show($"Database backup saved successfully to {file}", "Success", MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error during database backup.");

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

    private void UpdateSnowballAnalysis(List<ProjectionItem> standard, List<ProjectionItem> snowball) {
        try {
            if (standard == null || snowball == null || !standard.Any() || !snowball.Any()) {
                ShowSnowballAnalysis = false;
                return;
            }

            var lastStd = standard.Last();
            var lastSnow = snowball.Last();

            decimal stdTotalDebt = standard.Count > 0 ? GetTotalDebt(standard.Last()) : 0;
            decimal snowTotalDebt = snowball.Count > 0 ? GetTotalDebt(snowball.Last()) : 0;

            DateTime? stdDebtFreeDate = FindDebtFreeDate(standard);
            DateTime? snowDebtFreeDate = FindDebtFreeDate(snowball);

            SnowballDebtFreeDate = snowDebtFreeDate;
            if (snowDebtFreeDate.HasValue && stdDebtFreeDate.HasValue) {
                var diff = stdDebtFreeDate.Value - snowDebtFreeDate.Value;
                SnowballMonthsSaved = (int)(diff.TotalDays / 30);
            }
            else {
                SnowballMonthsSaved = 0;
            }

            SnowballFinalNetWorth = lastSnow.Balance;
            SnowballNetWorthImprovement = lastSnow.Balance - lastStd.Balance;

            SnowballFinalDebt = snowTotalDebt;
            SnowballDebtReductionVsStandard = stdTotalDebt - snowTotalDebt;

            ShowSnowballAnalysis = true;

            var sb = new System.Text.StringBuilder();
            if (snowDebtFreeDate.HasValue) {
                sb.AppendLine($"Estimated Debt Free: {snowDebtFreeDate.Value:MMM yyyy}");
                if (SnowballMonthsSaved > 0) sb.AppendLine($"(Saves {SnowballMonthsSaved} months)");
            }
            else {
                sb.AppendLine($"Final Debt: {snowTotalDebt:C0}");
            }

            sb.AppendLine($"Final Net Worth: {lastSnow.Balance:C0}");
            SnowballAnalysisText = sb.ToString();
            OnPropertyChanged(nameof(StrategyTakeawayPrimary));
            OnPropertyChanged(nameof(StrategyTakeawayDilemma));
        }
        catch (Exception ex) {
            Log.Error(ex, "Error updating snowball analysis.");
        }
    }

    private decimal GetTotalDebt(ProjectionItem item) {
        try {
            decimal totalDebt = 0;
            var debtAccountNames = Accounts.Where(a =>
                    a.IsLiability)
                .Select(a => a.Name)
                .ToList();

            foreach (var name in debtAccountNames) {
                if (item.AccountBalances.TryGetValue(name, out decimal bal) && bal < 0) {
                    totalDebt += -bal;
                }
            }

            return totalDebt;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error getting total debt.");

            return 0;
        }
    }

    private DateTime? FindDebtFreeDate(List<ProjectionItem> items) {
        try {
            var debtAccountNames = Accounts.Where(a =>
                    a.IsLiability)
                .Select(a => a.Name)
                .ToList();

            foreach (var item in items) {
                bool hasDebt = false;
                foreach (var name in debtAccountNames) {
                    if (item.AccountBalances.TryGetValue(name, out decimal bal) && bal < -0.01m) {
                        hasDebt = true;
                        break;
                    }
                }

                if (!hasDebt) return item.TransactionDate;
            }

            return null;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error finding debt free date.");

            return null;
        }
    }

    private bool _isBillDropDownOpen;

    public bool IsBillDropDownOpen {
        get => _isBillDropDownOpen;
        set => SetProperty(ref _isBillDropDownOpen, value);
    }

    private async void EditingTransactionClone_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        try {
            if (e.PropertyName == nameof(Transaction.SubCategoryId)) {
                ApplyDefaultBucketForSubCategory();
            }
            else if (e.PropertyName == nameof(Transaction.Description)) {
                OnPropertyChanged(nameof(TotalTransactionItemsToSaveCount));
                OnPropertyChanged(nameof(CanSaveTransaction));
                SaveTransactionCommand.NotifyCanExecuteChanged();

                // Refresh the CollectionView filter
                FilteredBillsView?.Refresh();

                string typedText = EditingTransactionClone?.Description?.Trim() ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(typedText)) {
                    // Check if there are any real matching bills other than (None)
                    bool hasMatches = FilteredBillsView?.OfType<Bill>()
                        .Any(b => b.Id != 0) ?? false;

                    IsBillDropDownOpen = hasMatches;
                }
                else {
                    IsBillDropDownOpen = false;
                }

                await TryAutoSuggestSubCategoryAsync();
            }
            else {
                OnPropertyChanged(nameof(TotalTransactionItemsToSaveCount));
                OnPropertyChanged(nameof(CanSaveTransaction));
                SaveTransactionCommand.NotifyCanExecuteChanged();
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in EditingTransactionClone_PropertyChanged.");
        }
    }

    private async void EditingAccountClone_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        try {
            //if (e.PropertyName == nameof(Account.Name)) {
            OnPropertyChanged(nameof(TotalAccountItemsToSaveCount));
            OnPropertyChanged(nameof(CanSaveAccount));
            SaveAccountCommand.NotifyCanExecuteChanged();
            //}
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in EditingAccountClone_PropertyChanged.");
        }
    }

    private async void EditingBillClone_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        try {
            // if (e.PropertyName == nameof(Bill.Name)) {
            OnPropertyChanged(nameof(TotalBillItemsToSaveCount));
            OnPropertyChanged(nameof(CanSaveBill));
            SaveBillCommand.NotifyCanExecuteChanged();
            // }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in EditingBillClone_PropertyChanged.");
        }
    }

    private async void EditingBucketClone_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        try {
            //if (e.PropertyName == nameof(BudgetBucket.Name)) {
            OnPropertyChanged(nameof(TotalBucketItemsToSaveCount));
            OnPropertyChanged(nameof(CanSaveBucket));
            SaveBucketCommand.NotifyCanExecuteChanged();
            //}
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in EditingBucketClone_PropertyChanged.");
        }
    }

    private async void EditingCategoryClone_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        try {
            //if (e.PropertyName == nameof(Category.Name)) {
            OnPropertyChanged(nameof(AvailableParentCategories));
            OnPropertyChanged(nameof(TotalCategoryItemsToSaveCount));
            OnPropertyChanged(nameof(CanSaveCategory));
            SaveCategoryCommand.NotifyCanExecuteChanged();
            AddAnotherCategoryCommand.NotifyCanExecuteChanged();
            //}
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in EditingCategoryClone_PropertyChanged.");
        }
    }

    private async void EditingPaycheckClone_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        try {
            //if (e.PropertyName == nameof(Paycheck.Name)) {
            OnPropertyChanged(nameof(TotalPaycheckItemsToSaveCount));
            OnPropertyChanged(nameof(CanSavePaycheck));
            SavePaycheckCommand.NotifyCanExecuteChanged();
            //}
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in EditingPaycheckClone_PropertyChanged.");
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
            Log.Error(ex, "Error applying default bucket for subcategory.");
        }
    }

    private async Task TryAutoSuggestSubCategoryAsync() {
        try {
            if (EditingTransactionClone == null) return;

            string typedText = EditingTransactionClone.Description?.Trim() ?? string.Empty;

            if (EditingTransactionClone.Id == 0 &&
                !EditingTransactionClone.SubCategoryId.HasValue &&
                typedText.Length >= 2) {
                var suggestedSubId = await _budgetService.GetSuggestedSubCategoryIdAsync(
                    typedText,
                    EditingTransactionClone.TransactionDate);

                // Only assign SubCategoryId if the input text matches current typed state
                if (suggestedSubId.HasValue && EditingTransactionClone.Description?.Trim() == typedText) {
                    EditingTransactionClone.SubCategoryId = suggestedSubId.Value;
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error auto-suggesting subcategory.");
        }
    }

    private async Task PayBillAsync(ProjectionItem? projection) {
        if (projection == null || projection.Type != ProjectionEngine.ProjectionEventType.Bill) return;

        try {
            var bill = Bills.FirstOrDefault(b => b.Id == projection.BillId);
            if (bill == null) return;
            var transaction = new Transaction {
                AccountId = bill.AccountId,
                Amount = -Math.Abs(bill.ExpectedAmount),
                ToAccountId = bill.ToAccountId,
                TransactionDate = DateTime.Today,
                Description = bill.Name,
                NormalizedDescription = TransactionMatcher.NormalizeName(bill.Name),
                BillId = projection.BillId,
                BucketId = null,
                SubCategoryId = bill.SubCategoryId,
                FromAccountIsCleared = false
            };

            if (await _budgetService.UpsertTransactionAsync(transaction)) {
                SystemSounds.Asterisk.Play();

                ShowSuccessToast($"Marked bill {bill.Name} for {bill.ExpectedAmount:C} as paid.");

                await LoadPeriodDataAsync();
                await CalculateProjectionsAsync();
            }
            else {
                throw new Exception($"Failed to record bill payment for {bill.Name}.");
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error paying bill.");

            MessageBox.Show($"Failed to record bill payment: {ex.Message}", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task PayPeriodBillAsync(PeriodBill? periodBill) {
        if (periodBill == null) return;

        try {
            var bill = Bills.FirstOrDefault(b => b.Id == periodBill.BillId);
            if (bill == null) return;
            var transaction = new Transaction {
                AccountId = bill.AccountId,
                Amount = -Math.Abs(bill.ExpectedAmount),
                ToAccountId = bill.ToAccountId,
                TransactionDate = DateTime.Today,
                Description = bill.Name,
                NormalizedDescription = TransactionMatcher.NormalizeName(bill.Name),
                BillId = bill.Id,
                BucketId = null,
                SubCategoryId = bill.SubCategoryId,
                FromAccountIsCleared = false,
                IsPrincipalOnly = bill.IsPrincipalOnly
            };

            if (await _budgetService.UpsertTransactionAsync(transaction)) {
                SystemSounds.Asterisk.Play();

                ShowSuccessToast($"Marked bill {bill.Name} for {bill.ExpectedAmount:C} as paid.");

                await LoadPeriodDataAsync();
                RequestProjectionRecalculation();
            }
            else {
                throw new Exception($"Failed to record bill payment for {bill.Name}.");
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error paying period bill.");

            MessageBox.Show($"Failed to record bill payment: {ex.Message}", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    #endregion

    #region Draw down

    private async Task FundEnvelopeAsync(ProjectionItem? projection) {
        if (projection == null || projection.Type != ProjectionEngine.ProjectionEventType.AccumulatingDrawdown) return;

        try {
            var bucket = Buckets.FirstOrDefault(b => b.Id == projection.BucketId);
            if (bucket == null) return;

            var allocations = await _budgetService.GetAllocationsForBucketAsync(bucket.Id);
            var primaryAllocation = allocations.FirstOrDefault();

            DateTime transactionDate = projection.TransactionDate;
            if (primaryAllocation != null) {
                var pay = Paychecks.FirstOrDefault(p => p.Id == primaryAllocation.PaycheckId);
                if (pay != null) {
                    var nextPay = pay.StartDate;
                    while (nextPay <= projection.TransactionDate) {
                        transactionDate = nextPay;
                        nextPay = pay.Frequency switch {
                            Frequency.Weekly => nextPay.AddDays(7),
                            Frequency.BiWeekly => nextPay.AddDays(14),
                            Frequency.Monthly => nextPay.AddMonths(1),
                            _ => nextPay.AddYears(100)
                        };
                    }
                }
            }

            await _budgetService.FundPeriodBucketAsync(bucket.Id, transactionDate, projection.Amount);
            SystemSounds.Asterisk.Play();

            ShowSuccessToast($"Set aside {projection.Amount:C} for {bucket.Name}.");
            await LoadBucketDataAsync();
            await LoadPeriodDataAsync();
            await CalculateProjectionsAsync();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error funding envelope.");

            MessageBox.Show($"Failed to set aside money for envelope: {ex.Message}", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task SkipFundEnvelopeAsync(ProjectionItem? projection) {
        if (projection == null || projection.Type != ProjectionEngine.ProjectionEventType.AccumulatingDrawdown) return;

        try {
            var bucket = Buckets.FirstOrDefault(b => b.Id == projection.BucketId);
            if (bucket == null) return;

            var allocations = await _budgetService.GetAllocationsForBucketAsync(bucket.Id);
            var primaryAllocation = allocations.FirstOrDefault();

            DateTime transactionDate = projection.TransactionDate;
            if (primaryAllocation != null) {
                var pay = Paychecks.FirstOrDefault(p => p.Id == primaryAllocation.PaycheckId);
                if (pay != null) {
                    var nextPay = pay.StartDate;
                    while (nextPay <= projection.TransactionDate) {
                        transactionDate = nextPay;
                        nextPay = pay.Frequency switch {
                            Frequency.Weekly => nextPay.AddDays(7),
                            Frequency.BiWeekly => nextPay.AddDays(14),
                            Frequency.Monthly => nextPay.AddMonths(1),
                            _ => nextPay.AddYears(100)
                        };
                    }
                }
            }

            await _budgetService.FundPeriodBucketAsync(bucket.Id, transactionDate, 0);
            SystemSounds.Asterisk.Play();

            ShowSuccessToast($"Skipped funding for {bucket.Name}.");
            await LoadBucketDataAsync();
            await LoadPeriodDataAsync();
            await CalculateProjectionsAsync();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error skipping envelope funding.");

            MessageBox.Show($"Failed to skip funding for envelope: {ex.Message}", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    #endregion

    #region Dashboard Cash Availability Properties

    public decimal TotalLiquidCash => Accounts
        .Where(a => !a.IsArchived && a.IncludeInTotal &&
                    (a.Type == AccountType.Checking || a.Type == AccountType.Savings))
        .Sum(a => a.Balance);

    public decimal EnvelopeFloorRequirements => Buckets
        .Where(b => !b.IsArchived && b.Type == BucketType.UpfrontFloor)
        .Sum(b => b.TargetBalance);

    public decimal AccumulatingDrawdownReserves => Buckets
        .Where(b => !b.IsArchived && b.Type == BucketType.AccumulatingDrawdown)
        .Sum(b => b.CurrentBalance);

    public decimal UpcomingBillsRequirements => CurrentPeriodBills
        .Where(pb => !pb.HasActualAmount && pb.ActualAmount > 0)
        .Sum(pb => pb.ActualAmount);

    public decimal UnspentStandardEnvelopeRequirements => CurrentPeriodBuckets
        .Where(pb => pb.BucketType == BucketType.Standard && pb.TransactionAmount <= pb.ActualAmount)
        .Sum(pb => pb.ActualAmount - pb.TransactionAmount);

    public decimal TotalRequiredReserves => EnvelopeFloorRequirements + AccumulatingDrawdownReserves +
                                            UpcomingBillsRequirements + UnspentStandardEnvelopeRequirements;

    public decimal UnallocatedSurplusCash => Math.Max(0, TotalLiquidCash - TotalRequiredReserves);

    public decimal AvailableSurplusPool => UnallocatedSurplusCash;

    public decimal TotalActiveSweepAmount {
        get {
            try {
                if (!SnowballOptions.EnableSnowball) return 0m;

                return CurrentPeriodSnowballProjections
                    .Where(p => (p.IsSweep || p.IsSynthetic) && p.Amount > 0)
                    .Sum(p => p.Amount);
            }
            catch (Exception ex) {
                Log.Error(ex, "Error calculating TotalActiveSweepAmount.");

                return 0m;
            }
        }
    }

    public decimal RetainedCheckingBuffer => Math.Max(0m, UnallocatedSurplusCash - TotalActiveSweepAmount);

    public decimal RecommendedDebtAllocation {
        get {
            try {
                if (!SnowballOptions.EnableSnowball) return 0m;

                return CurrentPeriodSnowballProjections
                    .Where(p => (p.IsSweep || p.IsSynthetic) && p.Amount > 0 && p.ToAccountId.HasValue)
                    .Where(p => Accounts.Any(a => a.Id == p.ToAccountId && a.IsLiability))
                    .Sum(p => p.Amount);
            }
            catch (Exception ex) {
                Log.Error(ex, "Error calculating RecommendedDebtAllocation.");

                return 0m;
            }
        }
    }

    public decimal RecommendedInvestmentAllocation {
        get {
            try {
                if (!SnowballOptions.EnableSnowball) return 0m;

                return CurrentPeriodSnowballProjections
                    .Where(p => (p.IsSweep || p.IsSynthetic) && p.Amount > 0 && p.ToAccountId.HasValue)
                    .Where(p => Accounts.Any(a => a.Id == p.ToAccountId && !a.IsLiability))
                    .Sum(p => p.Amount);
            }
            catch (Exception ex) {
                Log.Error(ex, "Error calculating RecommendedInvestmentAllocation.");

                return 0m;
            }
        }
    }

    public string StrategyDisplayName => SnowballOptions.PrimaryTarget switch {
        SurplusAllocationTarget.PayDownDebt => "Pay Down Debt Only",
        SurplusAllocationTarget.InvestSurplus => "Invest Surplus Only",
        SurplusAllocationTarget.Hybrid => "Waterfall (Debt First, Then Invest)",
        _ => "Custom Strategy"
    };

    #endregion

    #region Dashboard Cash Readiness & Action Suggestions

    public enum CashHealthStatus {
        Optimal,

        [Display(Name = "Transfer Recommended")]
        TransferRecommended,
        [Display(Name = "Global Deficit")] GlobalDeficit
    }

    public decimal LowestProjectedCheckingBalance {
        get {
            try {
                if (Projections == null || !Projections.Any())
                    return TotalLiquidCash;

                var checkingAccountNames = Accounts
                    .Where(a => !a.IsArchived && a.Type == AccountType.Checking)
                    .Select(a => a.Name)
                    .ToList();

                if (!checkingAccountNames.Any()) return 0;

                var nextPaycheckItem =
                    Projections.FirstOrDefault(p => p.Type == ProjectionEngine.ProjectionEventType.Paycheck);
                DateTime horizonDate = nextPaycheckItem?.TransactionDate ?? DateTime.Today.AddDays(14);

                var relevantProjections = Projections
                    .Where(p => p.TransactionDate >= DateTime.Today && p.TransactionDate <= horizonDate)
                    .ToList();

                if (!relevantProjections.Any()) return TotalLiquidCash;

                decimal minBalance = decimal.MaxValue;
                foreach (var proj in relevantProjections) {
                    foreach (var accName in checkingAccountNames) {
                        if (proj.AccountBalances.TryGetValue(accName, out decimal bal)) {
                            if (bal < minBalance) minBalance = bal;
                        }
                    }
                }

                return minBalance == decimal.MaxValue ? 0 : minBalance;
            }
            catch (Exception ex) {
                Log.Error(ex, "Error getting LowestProjectedCheckingBalance.");

                return 0;
            }
        }
    }

    public CashHealthStatus ReadinessStatus {
        get {
            try {
                if (UnallocatedSurplusCash < 0 || TotalLiquidCash < TotalRequiredReserves) {
                    return CashHealthStatus.GlobalDeficit;
                }

                if (LowestProjectedCheckingBalance < 0) {
                    return CashHealthStatus.TransferRecommended;
                }

                return CashHealthStatus.Optimal;
            }
            catch (Exception ex) {
                Log.Error(ex, "Error evaluating ReadinessStatus.");

                return CashHealthStatus.Optimal;
            }
        }
    }

    public string ReadinessStatusTitle => ReadinessStatus switch {
        CashHealthStatus.Optimal => "Fully Funded & Ready",
        CashHealthStatus.TransferRecommended => "Action Recommended: Account Rebalance Needed",
        CashHealthStatus.GlobalDeficit => "Warning: Reserve Shortfall Detected",
        _ => "Cash Status"
    };

    public string ReadinessStatusIcon => ReadinessStatus switch {
        CashHealthStatus.Optimal => "CheckCircle",
        CashHealthStatus.TransferRecommended => "AlertCircle",
        CashHealthStatus.GlobalDeficit => "AlertOctagon",
        _ => "Information"
    };

    public SolidColorBrush ReadinessStatusHeaderBrush => ReadinessStatus switch {
        CashHealthStatus.Optimal =>
            Application.Current?.TryFindResource("ReadinessStatusHeaderOptimalBrush") as SolidColorBrush
            ?? Brushes.Green,

        CashHealthStatus.TransferRecommended =>
            Application.Current?.TryFindResource("ReadinessStatusHeaderTransferRecommendedBrush") as SolidColorBrush
            ?? Brushes.Orange,

        CashHealthStatus.GlobalDeficit =>
            Application.Current?.TryFindResource("ReadinessStatusHeaderGlobalDeficitBrush") as SolidColorBrush
            ?? Brushes.Red,

        _ => (SolidColorBrush)(new BrushConverter().ConvertFrom("#3B82F6") ?? Brushes.Blue)
    };

    public SolidColorBrush ReadinessStatusBackgroundBrush => ReadinessStatus switch {
        CashHealthStatus.Optimal =>
            Application.Current?.TryFindResource("ReadinessStatusOptimalBackgroundBrush") as SolidColorBrush
            ?? Brushes.LightGreen,

        CashHealthStatus.TransferRecommended =>
            Application.Current?.TryFindResource("ReadinessStatusTransferRecommendedBackgroundBrush") as SolidColorBrush
            ?? Brushes.LightYellow,

        CashHealthStatus.GlobalDeficit =>
            Application.Current?.TryFindResource("ReadinessStatusGlobalDeficitBackgroundBrush") as SolidColorBrush
            ?? Brushes.MistyRose,

        _ => (SolidColorBrush)(new BrushConverter().ConvertFrom("#F8FAFC") ?? Brushes.White)
    };

    public SolidColorBrush ReadinessStatusBorderBrush => ReadinessStatus switch {
        CashHealthStatus.Optimal =>
            Application.Current?.TryFindResource("ReadinessStatusOptimalBorderBrush") as SolidColorBrush
            ?? Brushes.Green,

        CashHealthStatus.TransferRecommended =>
            Application.Current?.TryFindResource("ReadinessStatusTransferRecommendedBorderBrush") as SolidColorBrush
            ?? Brushes.Orange,

        CashHealthStatus.GlobalDeficit =>
            Application.Current?.TryFindResource("ReadinessStatusGlobalDeficitBorderBrush") as SolidColorBrush
            ?? Brushes.Red,

        _ => (SolidColorBrush)(new BrushConverter().ConvertFrom("#E2E8F0") ?? Brushes.LightGray)
    };

    public string ReadinessSuggestionMessage {
        get {
            try {
                if (ReadinessStatus == CashHealthStatus.GlobalDeficit) {
                    decimal deficit = Math.Abs(TotalLiquidCash - TotalRequiredReserves);
                    return
                        $"Your total liquid cash is short by {deficit:C2} to satisfy all safety floors, accumulating drawdowns, and upcoming period expenses. Consider pausing surplus investments or debt sweeps.";
                }

                if (ReadinessStatus == CashHealthStatus.TransferRecommended) {
                    decimal transferNeeded = Math.Abs(LowestProjectedCheckingBalance);
                    var savingsBalance = Accounts
                        .Where(a => !a.IsArchived && a.Type == AccountType.Savings)
                        .Sum(a => a.Balance);

                    if (savingsBalance >= transferNeeded) {
                        return
                            $"Overall liquid cash is sufficient, but Checking is projected to drop to {LowestProjectedCheckingBalance:C2} prior to your next deposit.\n👉 Suggested Action: Move at least {transferNeeded:C2} from Savings to Primary Checking to prevent potential overdrafts.";
                    }

                    return
                        $"Checking is projected to drop to {LowestProjectedCheckingBalance:C2} before your next paycheck. Transfer funds into Checking to ensure upcoming payments clear safely.";
                }

                if (UnallocatedSurplusCash > 0) {
                    return
                        $"All account safety cushions, savings goals, and period budgets are fully covered. You have {UnallocatedSurplusCash:C2} in extra cash available to pay down debt or grow investments.";
                }

                return "All account requirements and budgets are fully satisfied by current liquid balances.";
            }
            catch (Exception ex) {
                Log.Error(ex, "Error getting ReadinessSuggestionMessage.");

                return string.Empty;
            }
        }
    }

    #endregion

    #region Reserve Alert Status Properties

    public bool IsMinimumBalanceCushionBreached {
        get {
            try {
                var checking =
                    Accounts.FirstOrDefault(a => !a.IsArchived && a.Type == AccountType.Checking && a.IsPrimary)
                    ?? Accounts.FirstOrDefault(a => !a.IsArchived && a.Type == AccountType.Checking);
                return checking != null && checking.Balance < EnvelopeFloorRequirements;
            }
            catch (Exception ex) {
                Log.Error(ex, "Error evaluating IsMinimumBalanceCushionBreached.");

                return false;
            }
        }
    }

    public bool HasOverspentEnvelopes =>
        CurrentPeriodBuckets.Any(pb => pb.BucketType == BucketType.Standard && pb.BudgetExceeded);

    public bool IsUnpaidBillsAlert => PastDueCount > 0;

    public bool IsTotalCommittedFundsBreached => TotalLiquidCash < TotalRequiredReserves;

    #endregion

    #region Action Items & Tasks

    public ObservableCollection<DashboardTaskViewModel> UpcomingStrategyTasks { get; } = new();

    public void RefreshUpcomingStrategyTasks() {
        try {
            UpcomingStrategyTasks.Clear();

            var cutoffDate = DateTime.Today.AddDays(31);

            var projectionItems = SnowballOptions.EnableSnowball ? SnowballProjections : Projections;
            var upcomingSweeps = projectionItems
                .Where(p => p.TransactionDate >= DateTime.Today &&
                            p.TransactionDate <= cutoffDate &&
                            (p.IsSweep || p.IsSynthetic) &&
                            p.Amount > 0)
                .ToList();

            foreach (var sweep in upcomingSweeps) {
                UpcomingStrategyTasks.Add(new DashboardTaskViewModel {
                    Title = sweep.Description,
                    Amount = sweep.Amount,
                    DueDate = sweep.TransactionDate,
                    TaskType = sweep.Description.Contains("Invest", StringComparison.OrdinalIgnoreCase)
                        ? StrategyTaskType.Investment
                        : StrategyTaskType.DebtPayoff
                });
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error refreshing upcoming strategy tasks.");
        }
    }

    private IEnumerable<ProjectionItem> CurrentPeriodSnowballProjections {
        get {
            try {
                if (SnowballProjections == null || !SnowballProjections.Any())
                    return Enumerable.Empty<ProjectionItem>();

                DateTime periodStart = CurrentPeriodDate;
                DateTime periodEnd = GetNextPeriodDate(periodStart);

                return SnowballProjections.Where(p =>
                    p.TransactionDate >= periodStart &&
                    p.TransactionDate < periodEnd);
            }
            catch (Exception ex) {
                Log.Error(ex, "Error getting CurrentPeriodSnowballProjections.");

                return Enumerable.Empty<ProjectionItem>();
            }
        }
    }

    #endregion

    public static void SetTheme(bool isDark) {
        try {
            var newThemeUri = new Uri(
                isDark ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml",
                UriKind.Relative
            );

            var appResources = Application.Current.Resources.MergedDictionaries;

            appResources.Clear();
            appResources.Add(new ResourceDictionary { Source = newThemeUri });

            // Force UI bindings dependent on theme brushes to re-evaluate
            if (Instance != null) {
                Instance.OnPropertyChanged(nameof(ReadinessStatusHeaderBrush));
                Instance.OnPropertyChanged(nameof(ReadinessStatusBackgroundBrush));
                Instance.OnPropertyChanged(nameof(ReadinessStatusBorderBrush));
            }

            foreach (Window window in Application.Current.Windows) {
                var charts =
                    StayOnTarget.Helpers.VisualTreeUtils.FindVisualChildren<ProjectionLiveChartControl>(window);
                foreach (var chart in charts) {
                    chart.RefreshTheme();
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error setting theme.");
        }
    }
}