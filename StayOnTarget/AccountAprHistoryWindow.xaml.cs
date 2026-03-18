using System.Windows;
using System.Windows.Input;
using StayOnTarget.Models;
using StayOnTarget.Services;
using StayOnTarget.ViewModels;

namespace StayOnTarget;

public partial class AccountAprHistoryWindow : Window {
    private readonly AccountAprHistoryViewModel _viewModel;

    public AccountAprHistoryWindow(Account account, BudgetService budgetService) {
        InitializeComponent();
        _viewModel = new AccountAprHistoryViewModel(account, budgetService);
        HeaderLabel.Text = $"Annual Interest Rates for {account.Name}";
        DataContext = _viewModel;
    }
    
    private void OkButton_Click(object sender, RoutedEventArgs e) {
        _viewModel.UpdateAccountAprHistories();
        DialogResult = true;
    }
    
    private void CancelButton_Click(object sender, RoutedEventArgs e) {
        DialogResult = false;
    }
}