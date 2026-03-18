using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using StayOnTarget.Models;
using StayOnTarget.Services;

namespace StayOnTarget.ViewModels;

public class AccountAprHistoryViewModel: ViewModelBase {
    private readonly BudgetService _budgetService;
    private readonly Account _account;
    
    public AccountAprHistoryViewModel(Account account, BudgetService budgetService) {
        _account = account;
        _budgetService = budgetService;
        LoadData();
    }   
    
    private AccountAprHistory _selectedItem;
    public AccountAprHistory? SelectedItem {
        get => _selectedItem;
        set {
            SetProperty(ref _selectedItem, value);
        }
    }
    
    private ObservableCollection<AccountAprHistory> _accountAprHistories = new();
    public ObservableCollection<AccountAprHistory> AccountAprHistories {
        get => _accountAprHistories;
        set => SetProperty(ref _accountAprHistories, value);
    }
    
    public ICommand AddCommand => new RelayCommand(_ => Add());
    public ICommand RemoveCommand => new RelayCommand(aah => Remove(aah as AccountAprHistory));
    
    private void Add()
    {
        // A new row is added to the DataGrid automatically
        if (AccountAprHistories.Count == 0) {
            AccountAprHistories.Add(new AccountAprHistory() { AccountId = _account.Id, AsOfDate=DateTime.MinValue, AnnualPercentageRate = 0, CashAdvanceRate = 0, BalanceTransferRate = 0 });
        }
        else {
            if(AccountAprHistories.Any(a => a.AsOfDate ==new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day))) return;
            var latestRecords = AccountAprHistories.Last(a => a.AsOfDate == AccountAprHistories.Max(b => b.AsOfDate));
            AccountAprHistories.Add(new AccountAprHistory() { AccountId = _account.Id, AsOfDate=new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day), AnnualPercentageRate = latestRecords.AnnualPercentageRate, CashAdvanceRate = latestRecords.CashAdvanceRate, BalanceTransferRate = latestRecords.BalanceTransferRate });
        }
        OnPropertyChanged(nameof(AccountAprHistories));
    }
    
    private void Remove(AccountAprHistory? aah)
    {
        if (aah is { Id: > 0 }) {
            MessageBoxResult messageBoxResult = MessageBox.Show(
                "Are you sure you want to delete the interest rate record?", // Message
                "Delete Confirmation", // Title
                MessageBoxButton.YesNo, // Buttons
                MessageBoxImage.Warning // Icon
            );
        
            // Check the user's response
            if (messageBoxResult == MessageBoxResult.Yes) {
                // User confirmed deletion, proceed with your delete logic here
                _budgetService.DeleteAccountAprHistory(aah.Id);
            }
        }
        
        // A new row is added to the DataGrid automatically
        if (AccountAprHistories.Count == 0) {
            AccountAprHistories.Add(new AccountAprHistory() { AccountId = _account.Id,AsOfDate=DateTime.MinValue, AnnualPercentageRate = 0, CashAdvanceRate = 0, BalanceTransferRate = 0 });
        }
        else {
            var latestRecords = AccountAprHistories.Last(a => a.AsOfDate == AccountAprHistories.Max(b => b.AsOfDate));
            AccountAprHistories.Add(new AccountAprHistory() { AccountId = _account.Id, AsOfDate=DateTime.Now, AnnualPercentageRate = latestRecords.AnnualPercentageRate, CashAdvanceRate = latestRecords.CashAdvanceRate, BalanceTransferRate = latestRecords.BalanceTransferRate });
        }
    }
    
    private void LoadData() {
        var histories = _budgetService.GetAccountAprHistories(_account.Id);
        histories = histories.OrderBy(b => b.AsOfDate).ToList();
       
        AccountAprHistories = new ObservableCollection<AccountAprHistory>(histories);
    }
    
    public void UpdateAccountAprHistories() {
        if (_account.Id > 0) {
            foreach (var aah in AccountAprHistories) {
                if(aah.AccountId==0) aah.AccountId = _account.Id;
                _budgetService.UpsertAccountAprHistory(aah);
            }
            
            var histories = _budgetService.GetAccountAprHistories(_account.Id);
            histories = histories.OrderBy(b => b.AsOfDate).ToList();
            _account.AccountAprHistory = histories.ToList();
            AccountAprHistories = new ObservableCollection<AccountAprHistory>(histories);
        }
    }
}