using System.Windows;
using StayOnTarget.Models;
using StayOnTarget.Services;
using StayOnTarget.ViewModels;

namespace StayOnTarget.Views
{
    public partial class ImportReconciliationWindow : Window
    {
        private readonly ImportReconciliationViewModel _viewModel;
        
        public ImportReconciliationWindow(Account account, BudgetService budgetService) {
            InitializeComponent();
            _viewModel = new ImportReconciliationViewModel(account, budgetService);
            HeaderLabel.Text = $"Import Transactions for {account.Name}";
            DataContext = _viewModel;
        }
    }
}