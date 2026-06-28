using System.Collections.ObjectModel;
using System.Windows;
using Newtonsoft.Json;
using StayOnTarget.Models;
using StayOnTarget.Services;
using StayOnTarget.Views;

namespace StayOnTarget.ViewModels;

public class ReconciliationViewModel : ViewModelBase {
    private readonly BudgetService _budgetService;
    private readonly ReconciliationService _reconciliationService;
    private Account _account;

    public ReconciliationViewModel(Account account, BudgetService budgetService) {
        _account = account;
        _budgetService = budgetService;
        _reconciliationService = new ReconciliationService(_budgetService);
        LoadData();
    }

    private ObservableCollection<ReconciliationTransaction> _reconciliationTransactions = new();

    public ObservableCollection<ReconciliationTransaction> ReconciliationTransactions {
        get => _reconciliationTransactions;
        set => SetProperty(ref _reconciliationTransactions, value);
    }

    public Account Account {
        get => _account;
        set => SetProperty(ref _account, value);
    }

    private decimal _beginningBalance;

    public decimal BeginningBalance {
        get => _beginningBalance;
        set => SetProperty(ref _beginningBalance, value);
    }

    private decimal _endingBalance;

    public decimal EndingBalance {
        get => _endingBalance;
        set => SetProperty(ref _endingBalance, value);
    }

    private DateTime? _lastReconciledDate;

    public DateTime? LastReconciledDate {
        get => _lastReconciledDate;
        set => SetProperty(ref _lastReconciledDate, value);
    }

    private decimal? _newReconciledBalance = 0;

    public decimal? NewReconciledBalance {
        get => _newReconciledBalance;
        set => SetProperty(ref _newReconciledBalance, value);
    }

    private DateTime? _newReconciledDate;

    public DateTime? NewReconciledDate {
        get => _newReconciledDate;
        set => SetProperty(ref _newReconciledDate, value);
    }

    public void ImportAccount() {
        var window = new ImportReconciliationWindow(_account, _budgetService) {
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
        LoadData();
    }
    
    private void LoadData() {
        decimal beginningBalance = _account.Balance;
        DateTime? lastReconciledDate = _account.BalanceAsOf;
        var accountReconciliation = _budgetService.GetLatestValidReconciliation(_account.Id);

        if (accountReconciliation != null) {
            beginningBalance = accountReconciliation.ReconciledBalance;
            lastReconciledDate = accountReconciliation.ReconciledAsOfDate;
        }

        var transactions = _budgetService.GetAllUnreconciledTransactionsSinceLastReconciliation(_account.Id);
        transactions = transactions.OrderBy(b => b.TransactionDate).ToList();
        string json = JsonConvert.SerializeObject(transactions.ToList());
        var reconciliationTransactions = JsonConvert.DeserializeObject<List<ReconciliationTransaction>>(json);
        EndingBalance = _reconciliationService.CalculateRunningBalance(_account.Id, reconciliationTransactions!,
            out lastReconciledDate, out beginningBalance);
        BeginningBalance = beginningBalance;
        LastReconciledDate = lastReconciledDate ?? DateTime.MinValue;

        decimal? newReconciledBalance = null;
        DateTime? newReconciledDate = null;
        bool hasNewReconciled = false;
        bool notReconciledAfterLastReconciled = true;
        foreach (var t in reconciliationTransactions!.OrderBy(b => b.TransactionDate)) {
            if (t.IsReconciled) {
                if (!notReconciledAfterLastReconciled) {
                    newReconciledBalance = t.Amount;
                    newReconciledDate = t.TransactionDate;
                    hasNewReconciled = true;
                }
            }
            else {
                if (hasNewReconciled) {
                    notReconciledAfterLastReconciled = true;
                }
            }
        }

        NewReconciledBalance = newReconciledBalance;
        NewReconciledDate = newReconciledDate;
        ReconciliationTransactions = new ObservableCollection<ReconciliationTransaction>(reconciliationTransactions!);
    }

    public void UpdateReconciliationTransactions() {
        decimal? newReconciledBalance = null;
        DateTime? newReconciledDate = null;
        bool hasNewReconciled = false;
        bool notReconciledAfterLastReconciled = false;
        bool changed = false;
        foreach (var t in ReconciliationTransactions.OrderBy(b => b.TransactionDate)) {
            if (t.IsReconciled) {
                if (!notReconciledAfterLastReconciled) {
                    hasNewReconciled = true;
                }
                else {
                    if (t.IsReconciled) {
                        t.IsReconciled = false;
                        changed = true;
                    }
                }
            }
            else {
                if (hasNewReconciled) {
                    notReconciledAfterLastReconciled = true;
                }
            }
        }

        foreach (var t in ReconciliationTransactions.OrderBy(b => b.TransactionDate)) {
            if (t.IsReconciled) {
                newReconciledBalance = t.RunningBalance;
                newReconciledDate = t.TransactionDate;
            }
        }

        if (changed) OnPropertyChanged(nameof(ReconciliationTransactions));

        if ((NewReconciledBalance.HasValue || newReconciledBalance.HasValue) && (NewReconciledDate.HasValue || newReconciledDate.HasValue)) {
            _reconciliationService.ReconcileAccount(
                _account.Id,
                ReconciliationTransactions,
                NewReconciledBalance ?? newReconciledBalance ?? 0,
                NewReconciledDate ?? newReconciledDate ?? DateTime.MinValue);
        }
    }

    public void Reconcile() {
        decimal? newReconciledBalance = null;
        DateTime? newReconciledDate = null;
        bool hasNewReconciled = false;
        bool notReconciledAfterLastReconciled = false;
        foreach (var t in ReconciliationTransactions.OrderBy(b => b.TransactionDate)) {
            if (t.IsReconciled) {
                if (!notReconciledAfterLastReconciled) {
                    newReconciledBalance = t.RunningBalance;
                    newReconciledDate = t.TransactionDate;
                    hasNewReconciled = true;
                }
            }
            else {
                if (hasNewReconciled) {
                    notReconciledAfterLastReconciled = true;
                }
            }
        }

        NewReconciledBalance = newReconciledBalance;
        NewReconciledDate = newReconciledDate;
    }
}