using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using StayOnTarget.Models;
using StayOnTarget.Services;
using Serilog;

namespace StayOnTarget.ViewModels;

public class ExportTransactionsViewModel : ViewModelBase {
    private readonly BudgetService _budgetService = null!;
    private bool _includeArchivedAccounts;
    private DateFilterOption _selectedDateFilter = DateFilterOption.AllDates;
    private DateTime? _fromDate = DateTime.Today.AddMonths(-1);
    private DateTime? _toDate = DateTime.Today;
    private bool _includeBuckets = true;
    private bool _includeStatus = true;
    private bool _includeMemos = true;
    private string _exportFilePath = null!;
    private CsvExportPreset _selectedPreset = CsvExportPreset.Standard;

    public ExportTransactionsViewModel(BudgetService budgetService) {
        try {
            _budgetService = budgetService;
            Accounts = new ObservableCollection<SelectableAccount>();
            _exportFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                $"Transactions_Export_{DateTime.Now:yyyy-MM-dd}.csv");

            SelectAllCommand = new RelayCommand(SelectAll);
            ClearSelectionCommand = new RelayCommand(ClearSelection);
            BrowseCommand = new RelayCommand(Browse);
            ExportCommand = new AsyncRelayCommand(ExportAsync, () => CanExport());
            CancelCommand = new RelayCommand<Window>(Cancel);

            LoadAccountsAsync();
        }
        catch (Exception ex) {
            Log.Fatal(ex, "Critical error initializing ExportTransactionsViewModel[cite: 19].");
        }
    }

    public ObservableCollection<SelectableAccount> Accounts { get; } = null!;

    public bool IncludeArchivedAccounts {
        get => _includeArchivedAccounts;
        set {
            try {
                if (SetProperty(ref _includeArchivedAccounts, value)) {
                    LoadAccountsAsync();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting IncludeArchivedAccounts in ExportTransactionsViewModel[cite: 19].");
            }
        }
    }

    public DateFilterOption SelectedDateFilter {
        get => _selectedDateFilter;
        set {
            try {
                if (SetProperty(ref _selectedDateFilter, value)) {
                    OnPropertyChanged(nameof(IsCustomDateRange));
                    UpdateDatesFromFilter();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting SelectedDateFilter in ExportTransactionsViewModel[cite: 19].");
            }
        }
    }

    public bool IsCustomDateRange => SelectedDateFilter == DateFilterOption.Custom;

    public DateTime? FromDate {
        get => _fromDate;
        set => SetProperty(ref _fromDate, value);
    }

    public DateTime? ToDate {
        get => _toDate;
        set => SetProperty(ref _toDate, value);
    }

    public bool IncludeBuckets {
        get => _includeBuckets;
        set => SetProperty(ref _includeBuckets, value);
    }

    public bool IncludeStatus {
        get => _includeStatus;
        set => SetProperty(ref _includeStatus, value);
    }

    public bool IncludeMemos {
        get => _includeMemos;
        set => SetProperty(ref _includeMemos, value);
    }

    public string ExportFilePath {
        get => _exportFilePath;
        set {
            try {
                if (SetProperty(ref _exportFilePath, value)) {
                    ExportCommand.NotifyCanExecuteChanged();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting ExportFilePath in ExportTransactionsViewModel[cite: 19].");
            }
        }
    }

    public CsvExportPreset SelectedPreset {
        get => _selectedPreset;
        set {
            try {
                if (SetProperty(ref _selectedPreset, value)) {
                    UpdateDefaultFileName();
                    OnPropertyChanged(nameof(IsQuickenPreset));
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting SelectedPreset in ExportTransactionsViewModel[cite: 19].");
            }
        }
    }

    public bool IsQuickenPreset => SelectedPreset == CsvExportPreset.Quicken;

    private void UpdateDefaultFileName() {
        try {
            string directory = Path.GetDirectoryName(ExportFilePath) ??
                               Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string dateStr = DateTime.Now.ToString("yyyy-MM-dd");
            string fileName = SelectedPreset == CsvExportPreset.Quicken
                ? $"Quicken_Export_{dateStr}.csv"
                : $"Transactions_Export_{dateStr}.csv";
            ExportFilePath = Path.Combine(directory, fileName);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error updating default file name[cite: 19].");
        }
    }

    public IRelayCommand SelectAllCommand { get; } = null!;
    public IRelayCommand ClearSelectionCommand { get; } = null!;
    public IRelayCommand BrowseCommand { get; } = null!;
    public IAsyncRelayCommand ExportCommand { get; } = null!;
    public IRelayCommand<Window> CancelCommand { get; } = null!;

    private async void LoadAccountsAsync() {
        try {
            var accounts = await _budgetService.GetAllAccountsAsync(IncludeArchivedAccounts);
            Accounts.Clear();
            foreach (var account in accounts) {
                Accounts.Add(new SelectableAccount(account) { IsSelected = true });
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error loading accounts for export[cite: 19].");
        }
    }

    private void SelectAll() {
        try {
            foreach (var account in Accounts) {
                account.IsSelected = true;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error selecting all accounts[cite: 19].");
        }
    }

    private void ClearSelection() {
        try {
            foreach (var account in Accounts) {
                account.IsSelected = false;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error clearing account selection[cite: 19].");
        }
    }

    private void UpdateDatesFromFilter() {
        try {
            var today = DateTime.Today;
            switch (SelectedDateFilter) {
                case DateFilterOption.YearToDate:
                    FromDate = new DateTime(today.Year, 1, 1);
                    ToDate = today;
                    break;
                case DateFilterOption.CurrentMonth:
                    FromDate = new DateTime(today.Year, today.Month, 1);
                    ToDate = today;
                    break;
                case DateFilterOption.AllDates:
                    FromDate = null;
                    ToDate = null;
                    break;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error updating dates from filter[cite: 19].");
        }
    }

    private void Browse() {
        try {
            var dialog = new SaveFileDialog {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                InitialDirectory = Path.GetDirectoryName(ExportFilePath),
                FileName = Path.GetFileName(ExportFilePath)
            };

            if (dialog.ShowDialog() == true) {
                ExportFilePath = dialog.FileName;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error opening save file dialog for export[cite: 19].");
        }
    }

    private bool CanExport() {
        try {
            return !string.IsNullOrWhiteSpace(ExportFilePath);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error checking CanExport condition[cite: 19].");

            return false;
        }
    }

    private async Task ExportAsync() {
        try {
            UpdateDatesFromFilter();

            var selectedAccountIds = Accounts.Where(a => a.IsSelected).Select(a => a.Account.Id).ToList();
            if (!selectedAccountIds.Any()) {
                MessageBox.Show("Please select at least one account.", "Export Error", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var allTransactions = await _budgetService.GetAllTransactionsAsync(FromDate, ToDate);

            // Filter transactions where EITHER the From side OR the To side belongs to selected accounts
            var filteredTransactions = allTransactions
                .Where(t => (t.AccountId.HasValue && selectedAccountIds.Contains(t.AccountId.Value)) ||
                            (t.ToAccountId.HasValue && selectedAccountIds.Contains(t.ToAccountId.Value)));

            if (SelectedDateFilter != DateFilterOption.AllDates) {
                filteredTransactions = filteredTransactions
                    .Where(t =>
                        (FromDate == null || t.TransactionDate.Date >= FromDate.Value.Date) &&
                        (ToDate == null || t.TransactionDate.Date <= ToDate.Value.Date));
            }

            var sortedTransactions = filteredTransactions.OrderBy(t => t.TransactionDate).ToList();

            using (var writer = new StreamWriter(ExportFilePath)) {
                if (SelectedPreset == CsvExportPreset.Quicken) {
                    var headers = new List<string> {
                        "Date", "Payee", "FI Payee", "Amount", "Debit/Credit",
                        "Category", "Account", "Tag", "Memo", "Chknum"
                    };
                    await writer.WriteLineAsync(string.Join(",", headers.Select(EscapeCsvField)));

                    foreach (var t in sortedTransactions) {
                        foreach (var detail in t.Details) {
                            if (detail.AccountId == null || !selectedAccountIds.Contains(detail.AccountId.Value)) continue;
                                
                            bool isOutbound = t.AccountId == detail.AccountId;
                            // Format signed amount (- for outflow, + for inflow)
                            decimal signedAmount = isOutbound ? -Math.Abs(detail.Amount) : Math.Abs(detail.Amount);

                            var fields = new List<string> {
                                t.TransactionDate.ToString("MM/dd/yyyy"), // Date
                                detail.Description, // Payee / Description
                                string.Empty, // FI Payee
                                signedAmount.ToString("0.00"), // Amount
                                string.Empty, // Debit/Credit (Legacy)
                                detail.BucketName ?? string.Empty, // Category / Bucket
                                detail.AccountName ?? string.Empty, // Account Name
                                string.Empty, // Tag
                                detail.Memo ?? string.Empty, // Memo
                                string.Empty // Chknum
                            };

                            await writer.WriteLineAsync(string.Join(",", fields.Select(EscapeCsvField)));
                        }
                    }
                }
                else {
                    var headers = new List<string> { "Date", "Account", "Payee", "Amount" };
                    if (IncludeBuckets) headers.Add("Bucket");
                    if (IncludeStatus) headers.Add("Status");
                    if (IncludeMemos) headers.Add("Memo");
                    headers.Add("FITID");

                    await writer.WriteLineAsync(string.Join(",", headers.Select(EscapeCsvField)));

                    foreach (var t in sortedTransactions) {
                        foreach (var detail in t.Details) {
                            if (detail.AccountId == null || !selectedAccountIds.Contains(detail.AccountId.Value)) continue;
                                
                            bool isOutbound = t.AccountId == detail.AccountId;
                            // Format signed amount (- for outflow, + for inflow)
                            decimal signedAmount = isOutbound ? -Math.Abs(detail.Amount) : Math.Abs(detail.Amount);
                            
                            var fields = new List<string> {
                                t.TransactionDate.ToString("yyyy-MM-dd"),
                                detail.AccountName ?? string.Empty,
                                t.Description,
                                signedAmount.ToString("0.00")
                            };

                            if (IncludeBuckets) fields.Add(detail.BucketName ?? string.Empty);
                            if (IncludeStatus)
                                fields.Add(detail.ReconciliationId.HasValue ? "Reconciled" : "Cleared");
                            if (IncludeMemos) fields.Add(t.Memo ?? string.Empty);

                            fields.Add(t.AccountId.HasValue
                                ? t.FromFitId
                                : t.ToFitId);
                            
                            await writer.WriteLineAsync(string.Join(",", fields.Select(EscapeCsvField)));
                        }
                    }
                }
            }

            MessageBox.Show($"Successfully exported to {ExportFilePath}", "Export Successful", MessageBoxButton.OK,
                MessageBoxImage.Information);

            RequestClose?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error exporting transactions[cite: 19].");

            MessageBox.Show($"Error exporting transactions: {ex.Message}", "Export Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Cancel(Window? window) {
        try {
            window?.Close();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error closing export window[cite: 19].");
        }
    }

    private string EscapeCsvField(string field) {
        try {
            if (string.IsNullOrEmpty(field)) return string.Empty;
            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r")) {
                return $"\"{field.Replace("\"", "\"\"")}\"";
            }

            return field;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error escaping CSV field[cite: 19].");

            return field ?? string.Empty;
        }
    }

    public event EventHandler? RequestClose;
}

public class SelectableAccount : ViewModelBase {
    private bool _isSelected;

    public SelectableAccount(Account account) {
        Account = account;
    }

    public Account Account { get; }

    public bool IsSelected {
        get => _isSelected;
        set {
            try {
                SetProperty(ref _isSelected, value);
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting IsSelected on SelectableAccount[cite: 19].");
            }
        }
    }
}

public enum DateFilterOption {
    [Display(Name = "All Dates")] AllDates,
    [Display(Name = "Year To Date")] YearToDate,
    [Display(Name = "Current Month")] CurrentMonth,
    Custom
}

public enum CsvExportPreset {
    Standard,
    Quicken
}