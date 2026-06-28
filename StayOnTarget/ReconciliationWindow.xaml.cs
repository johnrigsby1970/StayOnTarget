using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StayOnTarget.Models;
using StayOnTarget.Services;
using StayOnTarget.ViewModels;
using StayOnTarget.Views;

namespace StayOnTarget;

public partial class ReconciliationWindow : Window {
    private readonly ReconciliationViewModel _viewModel;
private readonly BudgetService _budgetService;
    public ReconciliationWindow(Account account, BudgetService budgetService) {
        InitializeComponent();
        _viewModel = new ReconciliationViewModel(account, budgetService);
        HeaderLabel.Text = $"Reconciliation for {account.Name}";
        DataContext = _viewModel;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) {
        MessageBoxResult messageBoxResult = MessageBox.Show(
            $"I certify that there are no pending transactions on this account prior to {_viewModel.NewReconciledDate:MM/dd/yyyy} and that the balance is {_viewModel.NewReconciledBalance}?",
            "Delete Confirmation", MessageBoxButton.YesNo);

        if (messageBoxResult == MessageBoxResult.Yes) {
            _viewModel.UpdateReconciliationTransactions();
            DialogResult = true;
        }
        else {
            MessageBox.Show("Reconciliation cancelled.");
        }
    }
    
    public void HandleImportAccount_Click(object sender, RoutedEventArgs e) {
        _viewModel.ImportAccount();
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
    
    private void ReconciliationDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            var grid = sender as DataGrid;
            if (grid?.SelectedItem != null)
            {
                // Access your specific transaction class
                var transaction = grid.SelectedItem as ReconciliationTransaction;
                if (transaction != null)
                {
                    // Toggle the property
                    transaction.IsReconciled = !transaction.IsReconciled;
                
                    // Mark event as handled so the grid doesn't scroll
                    e.Handled = true; 
                }
            }
        }
    }
}