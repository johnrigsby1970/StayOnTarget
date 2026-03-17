using System.Collections.ObjectModel;
using System.Windows;
using StayOnTarget.Models;
using StayOnTarget.Services;
using StayOnTarget.ViewModels;

namespace StayOnTarget;

public partial class ReconciliationWindow : Window {
    private readonly ReconciliationViewModel _viewModel;

    public ReconciliationWindow(Account account, BudgetService budgetService) {
        InitializeComponent();
        _viewModel = new ReconciliationViewModel(account, budgetService);
        HeaderLabel.Text = $"Reconciliation for {account.Name}";
        DataContext = _viewModel;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) {
        MessageBoxResult messageBoxResult = System.Windows.MessageBox.Show(
            $"I certify that there are no pending transactions on this account prior to {_viewModel.NewReconciledDate:MM/dd/yyyy} and that the balance is {_viewModel.NewReconciledBalance}?",
            "Delete Confirmation", System.Windows.MessageBoxButton.YesNo);

        if (messageBoxResult == MessageBoxResult.Yes) {
            _viewModel.UpdateReconciliationTransactions();
            DialogResult = true;
        }
        else {
            System.Windows.MessageBox.Show("Reconciliation cancelled.");
        }
    }

    public void HandleCheck(object sender, RoutedEventArgs e) {
        _viewModel.Reconcile();
    }

    public void HandleUnchecked(object sender, RoutedEventArgs e) {
        _viewModel.Reconcile();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) {
        DialogResult = false;
    }
}