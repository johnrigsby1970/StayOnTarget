using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using StayOnTarget.Models;
using StayOnTarget.Services;
using System.IO;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Windows;

namespace StayOnTarget.ViewModels {
    // A wrapper for the QFX imported transactions
    public class ImportedTransactionViewModel : INotifyPropertyChanged {
        public string BankId { get; set; } // The FITID from the QFX
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        public string Payee { get; set; }

        private bool _isReconciled;

        public bool IsReconciled {
            get => _isReconciled;
            set {
                _isReconciled = value;
                OnPropertyChanged();
            }
        }

        private string _status = "Unmatched";

        public string Status {
            get => _status;
            set {
                _status = value;
                OnPropertyChanged();
            }
        }

        public string? MatchedManualFitId { get; set; }
        public DateTime? MatchedManualTransactionDate { get; set; }
        public string? MatchedManualTransactionId { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // A wrapper for your existing StayOnTarget manual transactions
    public class ManualTransactionViewModel : INotifyPropertyChanged {
        public string FitId { get; set; } // Your internal Guid
        public string? TransactionId { get; set; } // Your internal Transaction Id
        public DateTime TransactionDate { get; set; }
        public decimal Amount { get; set; }
        public string Description { get; set; }
        
        private bool _isMatched;

        public bool IsMatched {
            get => _isMatched;
            set {
                _isMatched = value;
                OnPropertyChanged();
            }
        }
        
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ImportReconciliationViewModel : INotifyPropertyChanged {
        public ObservableCollection<ImportedTransactionViewModel> ImportedTransactions { get; set; } = new();
        public ObservableCollection<ManualTransactionViewModel> UnreconciledManualTransactions { get; set; } = new();

        private ImportedTransactionViewModel _selectedImported;

        public ImportedTransactionViewModel SelectedImported {
            get => _selectedImported;
            set {
                _selectedImported = value;
                OnPropertyChanged();
                FilterManualSuggestions();
            }
        }

        private ManualTransactionViewModel _selectedManual;

        public ManualTransactionViewModel SelectedManual {
            get => _selectedManual;
            set {
                _selectedManual = value;
                OnPropertyChanged();
            }
        }

        private string? _lastFileName;

        public string? LastFileName {
            get => _lastFileName;
            set {
                _lastFileName = value;
                OnPropertyChanged();
            }
        }

        public ICommand LinkTransactionsCommand { get; }
        public ICommand ImportAsNewCommand { get; }

        private readonly BudgetService _budgetService;
        private Account _account;

        public ICommand SaveCommand { get; }

        public ICommand ImportQfxCommand { get; }

        public ImportReconciliationViewModel(Account account, BudgetService budgetService) {
            _account = account;
            _budgetService = budgetService;

            ImportQfxCommand = new RelayCommand(param => PromptAndLoadQfx());
            SaveCommand = new RelayCommand(param => Save());

            // Use a lambda to capture the parameter (param) and call your method
            LinkTransactionsCommand = new RelayCommand(
                param => LinkTransactions(),
                param => SelectedImported != null && SelectedManual != null
            );

            ImportAsNewCommand = new RelayCommand(
                param => ImportAsNew(),
                param => SelectedImported != null
            );

            LoadData(); // Replace with actual QFX parsing logic and DB call
        }

        private async void Save() {
            if (string.IsNullOrEmpty(LastFileName)) {
                return;
            }

            var fixDate = false;

            var differencesInDates = ImportedTransactions.Where(x =>
                x.MatchedManualTransactionDate != null &&
                DateOnly.FromDateTime(x.MatchedManualTransactionDate.Value.Date) != DateOnly.FromDateTime(x.Date));
        
            if (differencesInDates.Any()) {
                MessageBoxResult messageBoxResult = MessageBox.Show(
                    $"Some dates are different between existing transactions and those found at your bank. Do you want to set the transaction dates to match your bank?",
                    "Delete Confirmation", MessageBoxButton.YesNo);

                if (messageBoxResult == MessageBoxResult.Yes) {
                    fixDate = true;
                }
            }

            // 2. Create a list to track all your running database tasks
            var saveTasks = new List<Task>();

            foreach (var match in ImportedTransactions.Where(x =>
                         x.IsReconciled && !string.IsNullOrEmpty(x.MatchedManualTransactionId) &&
                         x.MatchedManualTransactionDate != null)) {
                 
                // Track each background database call
                var task = _budgetService.UpdateTransactionForBankFitId(
                    _account.Id,
                    match.MatchedManualTransactionId, 
                    match.MatchedManualFitId, 
                    match.BankId,
                    fixDate ? match.Date : match.MatchedManualTransactionDate.Value, 
                    match.Payee
                );
        
                saveTasks.Add(task);
            }

            // 3. Pause execution here until ALL database operations are 100% complete
            if (saveTasks.Any()) {
                await Task.WhenAll(saveTasks);
            }

            // 4. Now these run sequentially on the UI thread with accurate database states
            LoadData();
            ParseAndPopulateQfx(LastFileName);
        }

        private void PromptAndLoadQfx() {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog {
                Filter = "QFX Files (*.qfx)|*.qfx|OFX Files (*.ofx)|*.ofx|All Files (*.*)|*.*",
                Title = "Select Bank QFX File"
            };

            if (openFileDialog.ShowDialog() == true) {
                LastFileName = openFileDialog.FileName;
                ParseAndPopulateQfx(LastFileName);
            }
        }

        private void ParseAndPopulateQfx(string filePath) {
            if (!File.Exists(filePath)) return;

            ImportedTransactions.Clear();
            string content = File.ReadAllText(filePath);

            // Get all transaction blocks
            var txMatches = Regex.Matches(content, @"<STMTTRN>(.*?)</STMTTRN>", RegexOptions.Singleline);

            // Fetch existing bank IDs from your DB to skip duplicates
            // (Assuming your BudgetService/Database has a way to check already processed bank IDs)
            var processedBankIds = _budgetService.GetAlreadyImportedBankIds(_account.Id);

            foreach (Match txMatch in txMatches) {
                string txBlock = txMatch.Groups[1].Value;

                string bankId = GetQfxTagValue(txBlock, "FITID");

                // Skip if this exact transaction was already committed to the DB in a prior import

                if (processedBankIds.Contains(bankId)) {
                    //its already m,apped to a bank FitId, rtemove it so the list doesnt allow two records to be made to match
                    var rec = UnreconciledManualTransactions.SingleOrDefault(x => x.FitId == bankId);
                    if (rec != null) {
                        UnreconciledManualTransactions.Remove(rec);
                    }

                    continue;
                }

                string rawDate = GetQfxTagValue(txBlock, "DTPOSTED"); // Format typically: YYYYMMDDHHMMSS
                string rawAmount = GetQfxTagValue(txBlock, "TRNAMT");
                string payee = GetQfxTagValue(txBlock, "NAME");

                // Parse Date safely
                DateTime date = DateTime.Today;
                if (rawDate.Length >= 8 && DateTime.TryParseExact(rawDate.Substring(0, 8), "yyyyMMdd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate)) {
                    date = parsedDate;
                }

                // Parse Amount safely
                decimal.TryParse(rawAmount, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal amount);

                ImportedTransactions.Add(new ImportedTransactionViewModel {
                    BankId = bankId,
                    Date = date,
                    Amount = amount,
                    Payee = payee?.Trim(),
                    Status = "Unmatched"
                });
            }

            // Auto-match pass
            AutoMatchTransactions();
        }

        private void AutoMatchTransactions() {
            foreach (var imported in ImportedTransactions) {
                // Look for a manual entry with the exact amount and a date within a 4-day window
                var match = UnreconciledManualTransactions.FirstOrDefault(m =>
                    m.Amount == imported.Amount &&
                    Math.Abs((m.TransactionDate - imported.Date).TotalDays) <= 4);

                if (match != null) {
                    imported.IsReconciled = true;
                    imported.Status = $"Auto-Matched ({match.Description})";
                    imported.MatchedManualFitId = match.FitId;
                    imported.MatchedManualTransactionDate = match.TransactionDate;
                    imported.MatchedManualTransactionId = match.TransactionId;
                    match.IsMatched = true;
                    // Set selection defaults to help the user review
                    SelectedImported = imported;
                    SelectedManual = match;
                }
            }
        }

// Helper to extract values from unclosed SGML tags common in QFX/OFX files
        private string GetQfxTagValue(string block, string tag) {
            var match = Regex.Match(block, $@"<{tag}>([^<\r\n]+)");
            return match.Success ? match.Value.Replace($"<{tag}>", "").Trim() : string.Empty;
        }

        private void FilterManualSuggestions() {
            // Optional: Filter or highlight the Manual list here based on SelectedImported's Amount/Date
        }

        // Update the methods to handle the object parameter:
        private void LinkTransactions() {
            if (SelectedImported == null || SelectedManual == null) return;

            SelectedImported.IsReconciled = true;
            SelectedImported.Status = $"Matched to Manual ({SelectedManual.Amount:C})";
            SelectedImported.MatchedManualFitId = SelectedManual.FitId;
            SelectedImported.MatchedManualTransactionDate = SelectedManual.TransactionDate;
            SelectedImported.MatchedManualTransactionId = SelectedManual.TransactionId;


            UnreconciledManualTransactions.Remove(SelectedManual);
        }

        private void ImportAsNew() {
            if (SelectedImported == null) return;

            SelectedImported.IsReconciled = true;
            SelectedImported.Status = "Imported as New";
            SelectedImported.MatchedManualFitId = Guid.NewGuid().ToString();
        }

        private void LoadData() {
            // Mocking manual records currently in your DB
            var unreconciledTransactions = _budgetService.GetAllUnreconciledTransactions();
            unreconciledTransactions = unreconciledTransactions
                .Where(x => x.AccountId == _account.Id || x.ToAccountId == _account.Id).ToList();

            UnreconciledManualTransactions.Clear();
            foreach (var transaction in unreconciledTransactions) {
                UnreconciledManualTransactions.Add(new ManualTransactionViewModel {
                    FitId = transaction.FitId, TransactionDate = transaction.TransactionDate,
                    Amount = transaction.Amount,
                    Description = transaction.Description, TransactionId = transaction.TransactionId.ToString()
                });
            }

            // Mocking rows parsed from QFX
            // ImportedTransactions.Add(new ImportedTransactionViewModel
            //     { BankId = "BANK12345", Date = DateTime.Today.AddDays(-2), Amount = -45.50m, Payee = "KROGER #412" });
            // ImportedTransactions.Add(new ImportedTransactionViewModel
            //     { BankId = "BANK12346", Date = DateTime.Today, Amount = -15.75m, Payee = "Gas Station" });
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}