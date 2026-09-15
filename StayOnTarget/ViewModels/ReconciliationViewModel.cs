using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using Serilog;
using StayOnTarget.Models;
using StayOnTarget.Services;
using StayOnTarget.Views;

namespace StayOnTarget.ViewModels;

public partial class ReconciliationViewModel : ViewModelBase {
    private readonly BudgetService _budgetService = null!;
    private readonly ReconciliationService _reconciliationService = null!;
    private Account _account = null!;


    public ReconciliationViewModel(Account account, BudgetService budgetService) {
        try {
            _account = account;
            _budgetService = budgetService;
            _reconciliationService = new ReconciliationService(_budgetService);
            CancelAdjustmentCommand = new RelayCommand(() => {
                try {
                    CurrentAssetValue = EndingBalance;
                    AdjustmentTransactionAmount = null;
                    OnPropertyChanged(nameof(AdjustmentTransactionAmount));
                    AdjustmentTransactionMemo = "Balance adjustment transaction";
                    OnPropertyChanged(nameof(AdjustmentTransactionMemo));
                    IsBalanceAdjustmentVisible = false;
                    OnPropertyChanged(nameof(IsBalanceAdjustmentVisible));
                }
                catch (Exception ex) {
                    Log.Error(ex, "Error executing CancelAdjustmentCommand in ReconciliationViewModel.");
                    
                }
            });
            ShowAdjustmentCommand = new RelayCommand(() => IsBalanceAdjustmentVisible = true, () => CanShowAdjustBalance);

            AdjustBalanceCommand = new AsyncRelayCommand(AdjustBalanceAsync, () => CanExecuteAdjustBalance);
            CorrectOpeningBalanceCommand =
                new AsyncRelayCommand(CorrectOpeningBalanceAsync, () => CanExecuteCorrectOpeningBalance);
            CancelCorrectOpeningBalanceCommand = new AsyncRelayCommand(CancelCorrectOpeningBalanceAsync);

            InitializeDataCommand = new AsyncRelayCommand(LoadDataAsync);

            ToggleSelectionCommand = new RelayCommand(() => {
                try {
                    bool allSelected = ReconciliationTransactions.All(x => x.IsCleared);
                    bool allUnselected = ReconciliationTransactions.All(x => !x.IsCleared);

                    if (allSelected) {
                        foreach (var t in ReconciliationTransactions) t.IsCleared = false;
                    }
                    else if (allUnselected) {
                        foreach (var t in ReconciliationTransactions) t.IsCleared = true;
                    }
                    else {
                        foreach (var t in ReconciliationTransactions) {
                            if (!t.IsCleared) t.IsCleared = true;
                        }
                    }

                    Reconcile();
                }
                catch (Exception ex) {
                    Log.Error(ex, "Error executing ToggleSelectionCommand in ReconciliationViewModel.");
                    
                }
            });
            
            #region For orphan reconciliations
            ShowHistoricalOverlayCommand = new RelayCommand(() => IsHistoricalOverlayVisible = true);
            CancelHistoricalOverlayCommand = new RelayCommand(() => IsHistoricalOverlayVisible = false);
            ProcessHistoricalReconciliationCommand = new AsyncRelayCommand(ProcessHistoricalReconciliationAsync);
            #endregion
        }
        catch (Exception ex) {
            Log.Fatal(ex, "Critical error initializing ReconciliationViewModel.");
            
        }
    }

    #region For orphan reconciliations
    
    [ObservableProperty] private bool _isHistoricalOverlayVisible;
    [ObservableProperty] private bool _hasHistoricalOrphans;
    [ObservableProperty] private ObservableCollection<TransactionViewModel> _historicalOrphanTransactions = new();

    public IRelayCommand ShowHistoricalOverlayCommand { get; } = null!;
    public IRelayCommand CancelHistoricalOverlayCommand { get; } = null!;
    public IAsyncRelayCommand ProcessHistoricalReconciliationCommand { get; } = null!;
    
    #endregion
    
    private bool? _isAllSelected;
    public bool? IsAllSelected
    {
        get => _isAllSelected;
        set
        {
            try {
                if (_isAllSelected != value)
                {
                    _isAllSelected = value;
                    OnPropertyChanged(nameof(IsAllSelected));

                    // If user explicitly clicked header (value will be true or false, not null)
                    if (value.HasValue)
                    {
                        SelectAllRows(value.Value);
                    }
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting IsAllSelected property in ReconciliationViewModel.");
                
            }
        }
    }
    
    private void UpdateIsAllSelectedState()
    {
        try {
            if (ReconciliationTransactions == null || !ReconciliationTransactions.Any())
            {
                _isAllSelected = false;
            }
            else if (ReconciliationTransactions.All(x => x.IsCleared))
            {
                _isAllSelected = true; // All checked -> Header checked
            }
            else if (ReconciliationTransactions.All(x => !x.IsCleared))
            {
                _isAllSelected = false; // None checked -> Header unchecked
            }
            else
            {
                _isAllSelected = null; // Mix -> Header indeterminate (dash)
            }

            // Raise property change without re-triggering SelectAllRows loop
            OnPropertyChanged(nameof(IsAllSelected));
        }
        catch (Exception ex) {
            Log.Error(ex, "Error updating IsAllSelected state in ReconciliationViewModel.");
            
        }
    }

    private void SelectAllRows(bool select)
    {
        try {
            foreach (var item in ReconciliationTransactions)
            {
                // Temporarily unhook event listener to prevent infinite loops during bulk check
                item.PropertyChanged -= Item_PropertyChanged;
                item.IsCleared = select;
                item.PropertyChanged += Item_PropertyChanged;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error selecting all rows in ReconciliationViewModel.");
            
        }
    }
    
    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        try {
            if (e.PropertyName == nameof(TransactionViewModel.IsCleared))
            {
                // Re-evaluate header state whenever a single row checkbox toggles!
                UpdateIsAllSelectedState();
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling Item_PropertyChanged in ReconciliationViewModel.");
            
        }
    }
    
    public IAsyncRelayCommand InitializeDataCommand { get; } = null!;

    [ObservableProperty] private bool _canReconcile = true;

    [ObservableProperty] private string _blockingWarningMessage = string.Empty;

    private ObservableCollection<TransactionViewModel> _reconciliationTransactions = new();

    public ObservableCollection<TransactionViewModel> ReconciliationTransactions {
        get => _reconciliationTransactions;
        set => SetProperty(ref _reconciliationTransactions, value);
    }

    public Account Account {
        get => _account;
        set => SetProperty(ref _account, value);
    }

    private string _spinnerMessage = "Loading...";

    public string SpinnerMessage {
        get => _spinnerMessage;
        set => SetProperty(ref _spinnerMessage, value);
    }

    private bool _isOpeningBalanceCreated;

    public bool IsOpeningBalanceCreated {
        get => _isOpeningBalanceCreated;
        set => SetProperty(ref _isOpeningBalanceCreated, value);
    }

    private bool _isBusy;

    public bool IsBusy {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    private decimal _beginningBalance;

    public decimal BeginningBalance {
        get => _beginningBalance;
        set => SetProperty(ref _beginningBalance, value);
    }

    private decimal _endingBalance;

    public decimal EndingBalance {
        get => _endingBalance;

        set {
            try {
                if (SetProperty(ref _endingBalance, value)) {
                    OnPropertyChanged(nameof(ReconcileButtonText));
                    OnPropertyChanged(nameof(CanExecuteReconcile));
                    OnPropertyChanged(nameof(CanSave));
                    OnPropertyChanged(nameof(Difference));
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting EndingBalance in ReconciliationViewModel.");
                
            }
        }
    }

    private decimal? _openingBalance;

    public decimal? OpeningBalance {
        get => _openingBalance;
        set {
            try {
                if (SetProperty(ref _openingBalance, value)) {
                    OnPropertyChanged(nameof(CanExecuteCorrectOpeningBalance));
                    CorrectOpeningBalanceCommand.NotifyCanExecuteChanged();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting OpeningBalance in ReconciliationViewModel.");
                
            }
        }
    }

    private DateTime? _openingBalanceAsOf;

    public DateTime? OpeningBalanceAsOf {
        get => _openingBalanceAsOf;
        set {
            try {
                if (SetProperty(ref _openingBalanceAsOf, value)) {
                    OnPropertyChanged(nameof(CanExecuteCorrectOpeningBalance));
                    CorrectOpeningBalanceCommand.NotifyCanExecuteChanged();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting OpeningBalanceAsOf in ReconciliationViewModel.");
                
            }
        }
    }


    private DateTime? _openingBalanceMaximumAsOf;

    public DateTime? OpeningBalanceMaximumAsOf {
        get => _openingBalanceMaximumAsOf;
        set => SetProperty(ref _openingBalanceMaximumAsOf, value);
    }

    private decimal _currentAssetValue;

    public decimal CurrentAssetValue {
        get => _currentAssetValue;
        set {
            try {
                SetProperty(ref _currentAssetValue, value);
                CalculateAdjustmentTransactionAmount();
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting CurrentAssetValue in ReconciliationViewModel.");
                
            }
        }
    }

    private DateTime? _lastReconciledDate;

    public DateTime? LastReconciledDate {
        get => _lastReconciledDate;
        set => SetProperty(ref _lastReconciledDate, value);
    }

    private decimal? _newReconciledBalance = 0;

    public decimal? NewReconciledBalance {
        get => _newReconciledBalance;
        set {
            try {
                if (SetProperty(ref _newReconciledBalance, value)) {
                    OnPropertyChanged(nameof(CanExecuteReconcile));
                    OnPropertyChanged(nameof(ReconcileButtonText));
                    OnPropertyChanged(nameof(CanSave));
                    OnPropertyChanged(nameof(Difference));
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting NewReconciledBalance in ReconciliationViewModel.");
                
            }
        }
    }
    
    private decimal? _targetBalance = 0;
    public decimal? TargetBalance {
        get => _targetBalance;
        set {
            try {
                if (SetProperty(ref _targetBalance, value)) {
                    OnPropertyChanged(nameof(CanExecuteReconcile));
                    OnPropertyChanged(nameof(ReconcileButtonText));
                    OnPropertyChanged(nameof(CanSave));
                    OnPropertyChanged(nameof(Difference));
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting TargetBalance in ReconciliationViewModel.");
                
            }
        }
    }
    
    

    private DateTime? _newReconciledDate;

    public DateTime? NewReconciledDate {
        get => _newReconciledDate;
        set {
            try {
                if (SetProperty(ref _newReconciledDate, value)) {
                    OnPropertyChanged(nameof(CanExecuteReconcile));
                    OnPropertyChanged(nameof(CanSave));
                    OnPropertyChanged(nameof(ReconcileButtonText));
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting NewReconciledDate in ReconciliationViewModel.");
                
            }
        }
    }

    private decimal? _adjustmentTransactionAmount = 0;

    public decimal? AdjustmentTransactionAmount {
        get => _adjustmentTransactionAmount;
        set {
            try {
                if (SetProperty(ref _adjustmentTransactionAmount, value)) {
                    OnPropertyChanged(nameof(AdjustmentTransactionDescription));
                    OnPropertyChanged(nameof(CanExecuteAdjustBalance));
                    AdjustBalanceCommand.NotifyCanExecuteChanged();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting AdjustmentTransactionAmount in ReconciliationViewModel.");
                
            }
        }
    }

    private string? _adjustmentTransactionMemo = "Balance adjustment transaction";

    public string? AdjustmentTransactionMemo {
        get => _adjustmentTransactionMemo;
        set => SetProperty(ref _adjustmentTransactionMemo, value);
    }

    public string AdjustmentTransactionDescription => AdjustmentTransactionAmount.HasValue
        ? AdjustmentTransactionAmount.Value > 0 ? "[Value Increase]" : "[Value Decrease]"
        : "";

    private bool _isBalanceAdjustmentVisible;

    public bool IsBalanceAdjustmentVisible {
        get => _isBalanceAdjustmentVisible;
        set {
            try {
                if (SetProperty(ref _isBalanceAdjustmentVisible, value)) {
                    OnPropertyChanged(nameof(CanShowAdjustBalance));
                    ShowAdjustmentCommand.NotifyCanExecuteChanged();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting IsBalanceAdjustmentVisible in ReconciliationViewModel.");
                
            }
        }
    }

    private bool _isOpeningBalanceCorrectionVisible;

    public bool IsOpeningBalanceCorrectionVisible {
        get => _isOpeningBalanceCorrectionVisible;
        set {
            try {
                if (SetProperty(ref _isOpeningBalanceCorrectionVisible, value)) {
                    OnPropertyChanged(nameof(CanShowOpeningBalanceCorrection));
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting IsOpeningBalanceCorrectionVisible in ReconciliationViewModel.");
                
            }
        }
    }

    public decimal Difference => 
        (!NewReconciledBalance.HasValue || !TargetBalance.HasValue) 
            ? 0 
            : TargetBalance.Value - NewReconciledBalance.Value;

    public bool CanExecuteAdjustBalance => AdjustmentTransactionAmount != null;

    public bool CanExecuteReconcile =>
        ReconciliationTransactions.Any(x => x.IsCleared) && NewReconciledBalance == TargetBalance;

    public bool CanSave => ReconciliationTransactions.Any(x => x.IsCleared);


    public bool CanExecuteCorrectOpeningBalance => OpeningBalance != null && OpeningBalanceAsOf != null &&
                                                   OpeningBalanceAsOf <= OpeningBalanceMaximumAsOf;

    public bool CanShowAdjustBalance => IsBalanceAdjustmentVisible == false;
    public bool CanShowOpeningBalanceCorrection => IsOpeningBalanceCorrectionVisible == false;

    public IAsyncRelayCommand AdjustBalanceCommand { get; } = null!;

    public IRelayCommand CancelAdjustmentCommand { get; private set; } = null!;
    public IRelayCommand ShowAdjustmentCommand { get; private set; } = null!;

    public IAsyncRelayCommand CorrectOpeningBalanceCommand { get; } = null!;
    public IAsyncRelayCommand CancelCorrectOpeningBalanceCommand { get; } = null!;

    public IRelayCommand ToggleSelectionCommand { get; } = null!;

    public string ReconcileButtonText {
        get {
            try {
                // If cleared balance matches register balance (everything is caught up)
                if (ReconciliationTransactions.Any(x => x.IsCleared) && NewReconciledBalance == TargetBalance) {
                    return "Reconcile";
                }

                // If there are still uncleared items remaining
                return "Save Progress";
            }
            catch (Exception ex) {
                Log.Error(ex, "Error determining ReconcileButtonText in ReconciliationViewModel.");
                
                return "Save Progress";
            }
        }
    }

    public async Task ImportAccount() {
        try {
            var window = new ImportReconciliationWindow(_account, _budgetService) {
                Owner = Application.Current.MainWindow
            };
            window.ShowDialog();
            await LoadDataAsync();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error importing account in ReconciliationViewModel.");
            
        }
    }

    private async Task AdjustBalanceAsync() {
        try {
            decimal currentRunningBalance = BeginningBalance + ReconciliationTransactions.Sum(t => t.Amount);
            decimal delta = CurrentAssetValue - currentRunningBalance;

            if (delta == 0) return;

            var adjustmentTransaction = new Transaction {
                AccountId = delta > 0 ? null : _account.Id,
                ToAccountId = delta > 0 ? _account.Id : null,
                TransactionDate = DateTime.Today,
                Description = delta > 0 ? "Value Increase" : "Value Decrease",
                Memo = AdjustmentTransactionMemo,
                FromAccountIsCleared = delta > 0 ? null : true,
                ToAccountIsCleared = delta > 0 ? true : null,
                Amount = Math.Abs(delta)
            };

            await _budgetService.UpsertTransactionAsync(adjustmentTransaction);
            CurrentAssetValue = 0; // Reset so it gets recalculated in LoadData
            IsBalanceAdjustmentVisible = false;
            OnPropertyChanged(nameof(CanExecuteAdjustBalance));
            
            await LoadDataAsync();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error adjusting balance asynchronously in ReconciliationViewModel.");
            
        }
    }

    private async Task CancelCorrectOpeningBalanceAsync() {
        try {
            CanReconcile = false;
            if (OpeningBalanceMaximumAsOf.HasValue) {
                BlockingWarningMessage =
                    $"Transactions predate your Opening Balance date, or you have yet to set one. Reconciliation is not possible until this is addressed. Please provide a new opening balance as of or prior to {OpeningBalanceMaximumAsOf.Value:MM/dd/yyyy}. Return to Reconciliation once you know this value, and it will prompt you for this entry.";
            }
            else {
                BlockingWarningMessage =
                    "Transactions predate your Opening Balance date, or you have yet to set one. Reconciliation is not possible until this is addressed. Please provide a new opening balance older than the first transaction. Return to Reconciliation once you know this value, and it will prompt you for this entry.";
            }

            IsOpeningBalanceCorrectionVisible = false;
            OnPropertyChanged(nameof(CanExecuteCorrectOpeningBalance));
            CorrectOpeningBalanceCommand.NotifyCanExecuteChanged();
            CancelCorrectOpeningBalanceCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error canceling correct opening balance in ReconciliationViewModel.");
            
        }
    }

    private async Task CorrectOpeningBalanceAsync() {
        try {
            if (OpeningBalance == null) return;
            if (OpeningBalanceAsOf == null) return;

            (bool hasTransactions, decimal? openingBalance, DateTime? openingBalanceDate) openingRecord =
                await _budgetService.GetAccountBalanceOpeningStateAsync(_account.Id);

            var isDebtAccount = _account.IsLiability;

            if (openingRecord.openingBalance == null) {
                var openingBalanceTransaction = new Transaction() {
                    AccountId = isDebtAccount ? _account.Id : null,
                    ToAccountId = isDebtAccount
                        ? null
                        : _account.Id,
                    AccountName = isDebtAccount
                        ? _account.Name
                        : null,
                    ToAccountName = isDebtAccount
                        ? null
                        : _account.Name,
                    Amount = isDebtAccount
                        ? -1 * OpeningBalance.Value
                        : OpeningBalance
                            .Value,
                    TransactionDate = OpeningBalanceAsOf.Value,
                    TransactionId = Guid.NewGuid(),
                    ToFitId = Guid.NewGuid().ToString(),
                    FromFitId = Guid.NewGuid().ToString(),
                    Description = Constants.OpeningBalance,
                    Memo = Constants.OpeningBalance
                };

                await _budgetService.UpsertTransactionAsync(openingBalanceTransaction);
            }
            else {
                var allTransactions = await _budgetService.GetAccountTransactionsAsync(_account.Id);
                var openingBalanceTransaction =
                    allTransactions.FirstOrDefault(t => t.Description == Constants.OpeningBalance);
                openingBalanceTransaction!.Amount = isDebtAccount ? -1 * OpeningBalance.Value : OpeningBalance.Value;
                openingBalanceTransaction!.TransactionDate = OpeningBalanceAsOf.Value;

                await _budgetService.UpsertTransactionAsync(openingBalanceTransaction);
            }

            IsOpeningBalanceCorrectionVisible = false;
            OnPropertyChanged(nameof(CanExecuteCorrectOpeningBalance));
            CorrectOpeningBalanceCommand.NotifyCanExecuteChanged();

            await LoadDataAsync();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error correcting opening balance asynchronously in ReconciliationViewModel.");
            
        }
    }

    private void CalculateAdjustmentTransactionAmount() {
        try {
            decimal currentRunningBalance = BeginningBalance + ReconciliationTransactions.Sum(t => t.Amount);
            decimal delta = CurrentAssetValue - currentRunningBalance;

            if (delta == 0) {
                AdjustmentTransactionAmount = null;
            }
            else {
                AdjustmentTransactionAmount = delta;
            }

            OnPropertyChanged(nameof(AdjustmentTransactionAmount));
            OnPropertyChanged(nameof(CanExecuteAdjustBalance));

            // Force WPF to evaluate CanExecute immediately
            CommandManager.InvalidateRequerySuggested();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error calculating adjustment transaction amount in ReconciliationViewModel.");
            
        }
    }

    private async Task PromptForOpeningBalance(DateTime beforeDate) {
        try {
            IsOpeningBalanceCorrectionVisible = true;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error prompting for opening balance in ReconciliationViewModel.");
            
        }
    }


    private async Task LoadDataAsync() {
        try {
            SpinnerMessage = "Loading...";
            IsBusy = true;
            await Task.Delay(10);
            decimal beginningBalance = _account.IsLiability ? -_account.Balance : _account.Balance;
            DateTime? lastReconciledDate = _account.BalanceAsOf;
            (bool hasTransactions, decimal? openingBalance, DateTime? openingBalanceDate) openingRecord =
                await _budgetService.GetAccountBalanceOpeningStateAsync(_account.Id);
            IsOpeningBalanceCreated = openingRecord.openingBalance != null;
            OpeningBalance = openingRecord.openingBalance;
            OpeningBalanceAsOf = openingRecord.openingBalanceDate;
            OpeningBalanceMaximumAsOf = openingRecord.openingBalanceDate;

            var accountReconciliation = await _budgetService.GetLatestValidReconciliationAsync(_account.Id);

            if (accountReconciliation != null) {
                beginningBalance = _account.IsLiability
                    ? -accountReconciliation.ReconciledBalance
                    : accountReconciliation.ReconciledBalance;
                lastReconciledDate = accountReconciliation.ReconciledAsOfDate;
            }

            var transactions =
                await _budgetService.GetAllUnreconciledTransactionsSinceLastReconciliationAsync(_account.Id);
            transactions = transactions.Where(x=>x.AccountId == _account.Id || x.ToAccountId == _account.Id).OrderBy(b => b.TransactionDate).ToList();


            // 1. Fetch and order transactions at once
            var allTransactions = (await _budgetService.GetUnreconciledAccountLedgerAsync(_account.Id))
                .OrderBy(x => (DateTime)x.TransactionDate)
                .ToList();

            bool hasOpeningBalance = openingRecord.openingBalance != null;
            var earliestTransaction = allTransactions.FirstOrDefault(x => x.Description != Constants.OpeningBalance);

// ---------------------------------------------------------------------
// CASE 1: No Opening Balance Record Exists
// ---------------------------------------------------------------------
            if (!hasOpeningBalance) {
                if (earliestTransaction != null) {
                    OpeningBalanceMaximumAsOf = (DateTime)earliestTransaction!.TransactionDate.AddDays(-1);
                    OpeningBalanceAsOf = OpeningBalanceMaximumAsOf;

                    await PromptForOpeningBalance(OpeningBalanceMaximumAsOf.Value);

                    return; // Stops execution
                }
            }

// ---------------------------------------------------------------------
// CASE 2: Opening Balance Exists, but Unreconciled Transactions Predate It
// ---------------------------------------------------------------------
            bool hasTransactionsBeforeOpening = earliestTransaction != null
                                                && (DateTime)earliestTransaction.TransactionDate <=
                                                openingRecord.openingBalanceDate;
            IsBusy = false;
            await Task.Delay(10);

            if (hasTransactionsBeforeOpening) {
                bool isOpeningBalanceReconciled = allTransactions.Any(x =>
                    x.Description == Constants.OpeningBalance && x.ReconciliationId != null);
                bool hasAnyReconciledRecords = allTransactions.Any(x =>
                    x.Description != Constants.OpeningBalance && x.ReconciliationId != null);

                if (isOpeningBalanceReconciled && hasAnyReconciledRecords) {
                    OpeningBalanceMaximumAsOf = (DateTime)earliestTransaction!.TransactionDate.AddDays(-1);
                    OpeningBalanceAsOf = OpeningBalanceMaximumAsOf;

                    await PromptForOpeningBalance(OpeningBalanceMaximumAsOf.Value);

                    return; // Stops execution
                }
                else {
                    OpeningBalanceMaximumAsOf = (DateTime)earliestTransaction!.TransactionDate.AddDays(-1);
                    OpeningBalanceAsOf = OpeningBalanceMaximumAsOf;

                    await PromptForOpeningBalance(OpeningBalanceMaximumAsOf.Value);

                    return; // Stops execution
                }
            }

            IsBusy = true;

            await Task.Delay(10);

            string json = JsonConvert.SerializeObject(transactions.ToList());
            var clonedTransactions = JsonConvert.DeserializeObject<List<Transaction>>(json) ?? new();

            // 2. Map the cloned models directly into ViewModels
            var reconciliationTransactions = clonedTransactions
                .Select(x => {
                    var vm = new TransactionViewModel(x, Account);
                    // Capture whether the record was cleared in DB prior to UI adjustments
                    vm.WasOriginallyCleared = (x.AccountId == _account.Id ? x.FromAccountIsCleared : x.ToAccountIsCleared) ?? false;
                    return vm;
                })
                .ToList();
            
            bool hasTransactionPriorToLastReconcile = false;
            DateTime? lastTransactionDate = null;
    
            (EndingBalance, lastReconciledDate, lastTransactionDate, beginningBalance, hasTransactionPriorToLastReconcile) =
                await _reconciliationService.CalculateRunningBalanceAsync(_account.Id, reconciliationTransactions!);
            BeginningBalance = beginningBalance;
            
            // Fall back to opening balance date, and finally the account's non-nullable BalanceAsOf
            LastReconciledDate = lastReconciledDate 
                                 ?? openingRecord.openingBalanceDate 
                                 ?? _account.BalanceAsOf;
            
            if (CurrentAssetValue == 0) CurrentAssetValue = EndingBalance;

            // Historical Orphans: Backdated AND were already marked cleared in the DB
            var historicalOrphans = reconciliationTransactions
                .Where(t => LastReconciledDate != DateTime.MinValue 
                            && t.TransactionDate < LastReconciledDate 
                            && t.WasOriginallyCleared) // <--- Only grab items already flagged as cleared
                .ToList();
            
            HasHistoricalOrphans = historicalOrphans.Any();
            HistoricalOrphanTransactions = new ObservableCollection<TransactionViewModel>(historicalOrphans);

            // Active statement candidates (current window)
            var activeTransactions = reconciliationTransactions
                .Where(t => LastReconciledDate == DateTime.MinValue 
                            || t.TransactionDate >= LastReconciledDate 
                            || !t.WasOriginallyCleared) // <--- Keeps backdated pending/uncleared items in the main grid
                .ToList();
            
            decimal? newReconciledBalance = null;
            DateTime? newReconciledDate = null;
            
            newReconciledBalance = beginningBalance;
            
            foreach (var t in reconciliationTransactions!.OrderBy(b => b.TransactionDate)) {
                bool isPriorToLastReconcile = lastReconciledDate.HasValue && t.TransactionDate < lastReconciledDate.Value;

                // Preserve original cleared status if it was already cleared in the DB, 
                // otherwise enforce unchecking for uncleared prior transactions.
                if (t.WasOriginallyCleared) {
                    t.IsCleared = true;
                }
                else if (isPriorToLastReconcile) {
                    t.IsCleared = false;
                }
                else {
                    t.IsCleared = false;
                }

                if (t.IsCleared) {
                    if (t.AccountId == _account.Id) {
                        if (_account.IsLiability) {
                            newReconciledBalance += t.Amount;
                        }
                        else {
                            newReconciledBalance -= t.Amount;
                        }
                    }
                    else if (t.ToAccountId == _account.Id) {
                        if (_account.IsLiability) {
                            newReconciledBalance -= t.Amount;
                        }
                        else {
                            newReconciledBalance += t.Amount;
                        }
                    }
                }
            }
            
            // foreach (var t in reconciliationTransactions!.OrderBy(b => b.TransactionDate)) {
            //     // Only set IsCleared to true if the transaction occurred on or after the last reconciliation date
            //     bool isPriorToLastReconcile = lastReconciledDate.HasValue && t.TransactionDate < lastReconciledDate.Value;
            //
            //     if (isPriorToLastReconcile) {
            //         t.IsCleared = false;
            //     }
            //     else {
            //         if (t.AccountId == _account.Id) {
            //             if (t.FromAccountIsCleared ?? false) {
            //                 t.IsCleared = true;
            //             }
            //         }
            //         else if (t.ToAccountId == _account.Id) {
            //             if (t.ToAccountIsCleared ?? false) {
            //                 t.IsCleared = true;
            //             }
            //         }
            //     }
            //
            //     if (t.IsCleared) {
            //         if (t.AccountId == _account.Id) {
            //             if (_account.IsLiability) {
            //                 newReconciledBalance += t.Amount;
            //             }
            //             else {
            //                 newReconciledBalance -= t.Amount;
            //             }
            //         }
            //         else if (t.ToAccountId == _account.Id) {
            //             if (_account.IsLiability) {
            //                 newReconciledBalance -= t.Amount;
            //             }
            //             else {
            //                 newReconciledBalance += t.Amount;
            //             }
            //         }
            //     }
            // }
            
            NewReconciledBalance = newReconciledBalance;
            NewReconciledDate = newReconciledDate;
            ReconciliationTransactions =
                new ObservableCollection<TransactionViewModel>(reconciliationTransactions!);
        }
        catch (Exception ex) {
            Log.Error(ex, "ReconciliationViewModel failed to load data.");
            
        }
        finally {
            IsBusy = false;
        }
    }

    private async Task ProcessHistoricalReconciliationAsync() {
        try {
            var selectedRecords = HistoricalOrphanTransactions
                .Where(x => x.IsCleared)
                .Select(x => (int)(x.AccountId == _account.Id ? x.FromRecordId ?? 0 : x.ToRecordId ?? 0))
                .Where(id => id > 0)
                .ToList();

            if (selectedRecords.Any()) {
                SpinnerMessage = "Resolving historical records...";
                IsBusy = true;

                await _budgetService.ReconcileHistoricalTransactionsAsync(_account.Id, selectedRecords);
                IsHistoricalOverlayVisible = false;

                await LoadDataAsync();
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error processing historical reconciliation batch.");
        }
        finally {
            IsBusy = false;
        }
    }
    
    public async Task UpdateReconciliationTransactionsAsync() {
        try {
            decimal? newReconciledBalance = null;
            DateTime? newReconciledDate = null;
            newReconciledBalance = BeginningBalance;
            foreach (var t in ReconciliationTransactions.OrderBy(b => b.TransactionDate)) {
                if (t.IsCleared) {
                    if (t.AccountId == _account.Id) {
                        if (_account.IsLiability) {
                            newReconciledBalance += t.Amount;
                        }
                        else {
                            newReconciledBalance -= t.Amount;
                        }
                    }
                    else if (t.ToAccountId == _account.Id) {
                        if (_account.IsLiability) {
                            newReconciledBalance -= t.Amount;
                        }
                        else {
                            newReconciledBalance += t.Amount;
                        }
                    }

                    newReconciledDate = t.TransactionDate;
                }
            }
            var recordedBalance = NewReconciledBalance ?? newReconciledBalance ?? 0;
            recordedBalance = _account.IsLiability ? -recordedBalance : recordedBalance;
            var targetBalance = _account.IsLiability ? -TargetBalance : TargetBalance;
            if (ReconciliationTransactions.Any(x => x.IsCleared) &&
                recordedBalance == targetBalance) {
                foreach (var tx in ReconciliationTransactions) {
                    if (tx.IsCleared) {
                        tx.IsReconciled = true;
                    }
                }

                await _reconciliationService.ReconcileAccountAsync(
                    _account.Id,
                    ReconciliationTransactions.ToList(),
                    recordedBalance,
                    NewReconciledDate ?? newReconciledDate ?? DateTime.MinValue);
            }
            else {
                await _reconciliationService.ClearAccountAsync(
                    _account.Id,
                    ReconciliationTransactions.ToList());
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error updating reconciliation transactions asynchronously in ReconciliationViewModel.");
            
        }
    }

    public void Reconcile() {
        try {
            decimal? newReconciledBalance = null;
            DateTime? newReconciledDate = null;
            
            newReconciledBalance = BeginningBalance;
            UpdateIsAllSelectedState();
            foreach (var t in ReconciliationTransactions.OrderBy(b => b.TransactionDate)) {
                if (t.IsCleared) {
                    if (t.AccountId == _account.Id) {
                        if (_account.IsLiability) {
                            newReconciledBalance += t.Amount;
                        }
                        else {
                            newReconciledBalance -= t.Amount;
                        }
                    }
                    else if (t.ToAccountId == _account.Id) {
                        if (_account.IsLiability) {
                            newReconciledBalance -= t.Amount;
                        }
                        else {
                            newReconciledBalance += t.Amount;
                        }
                    }
                    
                    newReconciledDate = t.TransactionDate;
                }
            }

            NewReconciledBalance = newReconciledBalance;
            NewReconciledDate = newReconciledDate;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error executing Reconcile method in ReconciliationViewModel.");
            
        }
    }
}