using System.Collections.ObjectModel;
using System.Windows.Input;
using StayOnTarget.Models;
using StayOnTarget.Services;
using System.IO;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Windows;
using CsvHelper;

namespace StayOnTarget.ViewModels;

public class ImportReconciliationViewModel : ViewModelBase {
    private ViewModelBase _activeOverlay;

    public ViewModelBase ActiveOverlay {
        get => _activeOverlay;
        set {
            _activeOverlay = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<ImportedTransactionViewModel> ImportedTransactions { get; set; } = new();
    public ObservableCollection<ManualTransactionViewModel> UnreconciledManualTransactions { get; set; } = new();

    private ImportedTransactionViewModel? _selectedImported;

    public ImportedTransactionViewModel? SelectedImported {
        get => _selectedImported;
        set {
            SetProperty(ref _selectedImported, value);
            FilterManualSuggestions();
        }
    }

    private ManualTransactionViewModel? _selectedManual;

    public ManualTransactionViewModel? SelectedManual {
        get => _selectedManual;
        set => SetProperty(ref _selectedManual, value);
    }


    private bool? _lastImportAsQfx;

    public bool? LastImportAsQfx {
        get => _lastImportAsQfx;
        set => SetProperty(ref _lastImportAsQfx, value);
    }

    private string? _lastFileName;

    public string? LastFileName {
        get => _lastFileName;
        set => SetProperty(ref _lastFileName, value);
    }

    public ICommand LinkTransactionsCommand { get; }
    public ICommand ImportAsNewCommand { get; }

    public ICommand ClearMatchCommand { get; }

    private readonly BudgetService _budgetService;
    private Account _account;

    public ICommand SaveCommand { get; }
    public ICommand ImportFileCommand { get; }
    public ICommand ImportQfxCommand { get; }
    public ICommand ImportCsvCommand { get; }

    private CsvImportMappingViewModel? _csvMapping;

    public CsvImportMappingViewModel? CsvMapping {
        get => _csvMapping;
        set => SetProperty(ref _csvMapping, value);
    }

    private bool _isMappingVisible;

    public bool IsMappingVisible {
        get => _isMappingVisible;
        set => SetProperty(ref _isMappingVisible, value);
    }

    private bool _isNewTransactionFormVisible;

    public bool IsNewTransactionFormVisible {
        get => _isNewTransactionFormVisible;
        set => SetProperty(ref _isNewTransactionFormVisible, value);
    }


    public ICommand ConfirmCsvImportCommand { get; }
    public ICommand CancelCsvImportCommand { get; }

    public ICommand CancelNewTransactionCommand { get; }
    public ICommand SaveNewTransactionCommand { get; }

    public ObservableCollection<Bill> BillsWithNone { get; } = new();
    public ObservableCollection<BudgetBucket> BucketsWithNone { get; } = new();
    public ICommand ToggleSelectionCommand { get; }

    public ImportReconciliationViewModel(Account account, BudgetService budgetService) {
        _account = account;
        _budgetService = budgetService;

        ImportFileCommand = new RelayCommand(param => PromptForFile());
        // ImportQfxCommand = new RelayCommand(param => PromptAndLoadQfx());
        // ImportCsvCommand = new RelayCommand(param => PromptAndLoadCsv());
        ConfirmCsvImportCommand = new RelayCommand(param => ConfirmCsvImport(), param => CsvMapping?.CanImport == true);
        CancelCsvImportCommand = new RelayCommand(param => { IsMappingVisible = false; });

        SaveCommand = new RelayCommand(param => Save());

        ToggleSelectionCommand = new RelayCommand(param => {
            bool allSelected = ImportedTransactions.All(x => x.IsSelected);
            bool allUnselected = ImportedTransactions.All(x => !x.IsSelected);

            if (allSelected) {
                foreach (var t in ImportedTransactions) t.IsSelected = false;
            }
            else if (allUnselected) {
                foreach (var t in ImportedTransactions) t.IsSelected = true;
            }
            else {
                foreach (var t in ImportedTransactions) {
                    if (!t.IsSelected) t.IsSelected = true;
                }
            }
        });

        // Use a lambda to capture the parameter (param) and call your method
        LinkTransactionsCommand = new RelayCommand(
            param => LinkTransactions(),
            param => SelectedImported != null && !SelectedImported.IsReconciled && SelectedManual != null && !SelectedManual.IsMatched
        );

        ImportAsNewCommand = new RelayCommand(
            param => ImportAsNew(),
            param => SelectedImported != null && !SelectedImported.IsReconciled
        );

        ClearMatchCommand = new RelayCommand(
            param => ClearMatch(),
            param => SelectedImported != null && SelectedImported.IsReconciled
        );

        LoadData(); // Replace with actual QFX parsing logic and DB call
    }

    private async void Save() {
        if (string.IsNullOrEmpty(LastFileName)) {
            return;
        }

        var fixDate = false;

        var differencesInDates = ImportedTransactions.Where(x =>
            x.IsSelected &&
            x.IsReconciled &&
            x.MatchedManualTransactionDate != null &&
            DateOnly.FromDateTime(x.MatchedManualTransactionDate.Value.Date) != DateOnly.FromDateTime(x.Date.Value));

        if (differencesInDates.Any()) {
            MessageBoxResult messageBoxResult = MessageBox.Show(
                $"Some dates are different between existing transactions and those found at your bank. Do you want to set the transaction dates to match your bank?",
                "Date Change Confirmation", MessageBoxButton.YesNo);

            if (messageBoxResult == MessageBoxResult.Yes) {
                fixDate = true;
            }
        }

        // 2. Create a list to track all your running database tasks
        var saveTasks = new List<Task>();

        // Handled matched transactions
        foreach (var match in ImportedTransactions.Where(x =>
                     x.IsSelected &&
                     x.IsReconciled && !string.IsNullOrEmpty(x.MatchedManualTransactionId) &&
                     x.MatchedManualTransactionDate != null && !string.IsNullOrEmpty(x.MatchedManualFitId))) {
            // Track each background database call
            var task = _budgetService.UpdateTransactionForBankFitId(
                _account.Id,
                match.MatchedManualTransactionId!,
                match.MatchedManualFitId!,
                match.BankId,
                fixDate ? match.Date.Value : match.MatchedManualTransactionDate!.Value,
                match.Payee
            );

            saveTasks.Add(task);
        }

        // Handle new transactions that are checked but not matched
        foreach (var newItem in ImportedTransactions.Where(x => x.IsSelected && !x.IsReconciled)) {
            var t = new Transaction {
                AccountId = _account.Id,
                Amount = newItem.Amount,
                TransactionDate = newItem.Date ?? DateTime.Now,
                Description = newItem.Payee,
                FitId = newItem.BankId,
                BillId = newItem.BillId == 0 ? null : newItem.BillId,
                BucketId = newItem.BucketId == 0 ? null : newItem.BucketId
            };
            saveTasks.Add(_budgetService.UpsertTransactionAsync(t));
        }

        // 3. Pause execution here until ALL database operations are 100% complete
        if (saveTasks.Any()) {
            await Task.WhenAll(saveTasks);
        }

        // 4. Now these run sequentially on the UI thread with accurate database states
        LoadData();

        if (LastImportAsQfx != null && LastImportAsQfx.Value) {
            ParseAndPopulateQfx(LastFileName);
        }

        if (LastImportAsQfx != null && !LastImportAsQfx.Value) {
            ParseAndPopulateCsv(LastFileName);
        }
    }

    private void PromptForFile() {
        var openFileDialog = new Microsoft.Win32.OpenFileDialog {
            Filter = "Transaction Files (*.ofx, *.qfx, *.csv)|*.ofx;*.qfx;*.csv|" +
                     "CSV Files (*.csv, *.txt)|*.csv;*.txt|" +
                     "QFX Files (*.qfx)|*.qfx|" +
                     "OFX Files (*.ofx)|*.ofx",
            Title = "Select Transaction File"
        };

        if (openFileDialog.ShowDialog() == true) {
            LastFileName = openFileDialog.FileName;
            if (LastFileName.Contains(".qfx", StringComparison.OrdinalIgnoreCase) ||
                LastFileName.Contains(".ofx", StringComparison.OrdinalIgnoreCase)) {
                ParseAndPopulateQfx(LastFileName);
            }
            else {
                var mappingPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"mapping_{_account.Id}.json");
                CsvMapping = new CsvImportMappingViewModel(LastFileName, mappingPath);

                IsMappingVisible = true;
            }
        }
    }
    
    // private void PromptAndLoadQfx() {
    //     var openFileDialog = new Microsoft.Win32.OpenFileDialog {
    //         Filter = "QFX Files (*.qfx)|*.qfx|OFX Files (*.ofx)|*.ofx",
    //         Title = "Select Bank QFX File"
    //     };
    //
    //     if (openFileDialog.ShowDialog() == true) {
    //         LastFileName = openFileDialog.FileName;
    //         ParseAndPopulateQfx(LastFileName);
    //     }
    // }
    //
    // private void PromptAndLoadCsv() {
    //     var openFileDialog = new Microsoft.Win32.OpenFileDialog {
    //         Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
    //         Title = "Select Bank CSV File"
    //     };
    //
    //     if (openFileDialog.ShowDialog() == true) {
    //         LastFileName = openFileDialog.FileName;
    //         var mappingPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"mapping_{_account.Id}.json");
    //         CsvMapping = new CsvImportMappingViewModel(LastFileName, mappingPath);
    //
    //         IsMappingVisible = true;
    //     }
    // }

    private void ConfirmCsvImport() {
        if (CsvMapping == null || !CsvMapping.CanImport) return;

        var mappingPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"mapping_{_account.Id}.json");
        CsvMapping.SaveMapping(mappingPath);

        ParseAndPopulateCsv(CsvMapping.FilePath);
        IsMappingVisible = false;
    }

    private void ParseAndPopulateCsv(string filePath) {
        if (!File.Exists(filePath) || CsvMapping == null) return;
        LastImportAsQfx = false;

        ImportedTransactions.Clear();
        var processedBankIds = _budgetService.GetAlreadyImportedBankIds(_account.Id);

        using (var reader = new StreamReader(filePath))
        using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture)) {
            csv.Read();
            csv.ReadHeader();
            while (csv.Read()) {
                string bankId = csv.GetField(CsvMapping.BankIdHeader!) ?? Guid.NewGuid().ToString();

                if (processedBankIds.Contains(bankId)) {
                    var rec = UnreconciledManualTransactions.SingleOrDefault(x => x.FitId == bankId);
                    if (rec != null) {
                        UnreconciledManualTransactions.Remove(rec);
                    }

                    continue;
                }

                string rawDate = csv.GetField(CsvMapping.DateHeader!) ?? "";
                string rawAmount = csv.GetField(CsvMapping.AmountHeader!) ?? "";
                string payee = csv.GetField(CsvMapping.PayeeHeader!) ?? "";

                DateTime date = DateTime.Today;
                DateTime.TryParse(rawDate, CultureInfo.CurrentCulture, DateTimeStyles.None, out date);
                if (date == DateTime.MinValue) {
                    continue;
                }

                decimal.TryParse(rawAmount, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount);

                ImportedTransactions.Add(new ImportedTransactionViewModel {
                    BankId = bankId,
                    Date = date,
                    Amount = amount,
                    Payee = payee.Trim(),
                    Status = "Unmatched"
                });
            }
        }

        AutoMatchTransactions();
    }

    private void ParseAndPopulateQfx(string filePath) {
        if (!File.Exists(filePath)) return;
        LastImportAsQfx = true;
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
        //In different levels of accuracy, try to find a transaction that is already in the system that matches. The more accurate match wins
        foreach (var imported in ImportedTransactions.Where(x => x.Date != null)) {
            if (imported.Date == null || imported.Date == DateTime.MinValue) {
                //it is a pending transaction at the bank (BoA as an example)
                continue;
            }

            //same amount, same date, very close name
            var exactMatch = UnreconciledManualTransactions.FirstOrDefault(m =>
                Math.Abs(m.Amount) == Math.Abs(imported.Amount) &&
                Math.Abs((m.TransactionDate - imported.Date.Value).TotalDays) == 0 && TransactionMatcher.IsMatch(imported.Payee, m.Description));
            if (exactMatch != null) {
                imported.IsReconciled = true;
                imported.Status = $"Auto-Matched ({exactMatch.Description})";
                imported.MatchedManualFitId = exactMatch.FitId;
                imported.MatchedManualTransactionDate = exactMatch.TransactionDate;
                imported.MatchedManualTransactionId = exactMatch.TransactionId;

                exactMatch.IsMatched = true;
                // Set selection defaults to help the user review
                SelectedImported = imported;
                SelectedManual = exactMatch;
                continue;
            }
            
            //same amount, close date, very close name
            var closerMatch = UnreconciledManualTransactions.FirstOrDefault(m =>
                Math.Abs(m.Amount) == Math.Abs(imported.Amount) &&
                Math.Abs((m.TransactionDate - imported.Date.Value).TotalDays) <= 4 && TransactionMatcher.IsMatch(imported.Payee, m.Description));
            if (closerMatch != null) {
                imported.IsReconciled = true;
                imported.Status = $"Auto-Matched ({closerMatch.Description})";
                imported.MatchedManualFitId = closerMatch.FitId;
                imported.MatchedManualTransactionDate = closerMatch.TransactionDate;
                imported.MatchedManualTransactionId = closerMatch.TransactionId;

                closerMatch.IsMatched = true;
                // Set selection defaults to help the user review
                SelectedImported = imported;
                SelectedManual = closerMatch;
                continue;
            }
            
            //same amount, same date, name can be different
            var closeMatch = UnreconciledManualTransactions.FirstOrDefault(m =>
                Math.Abs(m.Amount) == Math.Abs(imported.Amount) &&
                                               Math.Abs((m.TransactionDate - imported.Date.Value).TotalDays) == 0);
            if (closeMatch != null) {
                imported.IsReconciled = true;
                imported.Status = $"Auto-Matched ({closeMatch.Description})";
                imported.MatchedManualFitId = closeMatch.FitId;
                imported.MatchedManualTransactionDate = closeMatch.TransactionDate;
                imported.MatchedManualTransactionId = closeMatch.TransactionId;
                imported.BillId = closeMatch.BillId;
                imported.BucketId = closeMatch.BucketId;
                imported.IsSelected = true;

                closeMatch.IsMatched = true;
                // Set selection defaults to help the user review
                SelectedImported = imported;
                SelectedManual = closeMatch;
                continue;
            }
            
            // Look for a manual entry with the exact amount and a date within a 4-day window
            if (UnreconciledManualTransactions.Count(m =>
                    Math.Abs(m.Amount) ==  Math.Abs(imported.Amount) &&
                                                    Math.Abs((m.TransactionDate - imported.Date.Value).TotalDays) <= 4) > 1) {
                continue;
            }

            //same amount, close date, name can be different
            var match = UnreconciledManualTransactions.FirstOrDefault(m =>
                m.Amount == imported.Amount &&
                Math.Abs((m.TransactionDate - imported.Date.Value).TotalDays) <= 4);

            if (match != null) {
                imported.IsReconciled = true;
                imported.Status = $"Auto-Matched ({match.Description})";
                imported.MatchedManualFitId = match.FitId;
                imported.MatchedManualTransactionDate = match.TransactionDate;
                imported.MatchedManualTransactionId = match.TransactionId;
                imported.BillId = match.BillId;
                imported.BucketId = match.BucketId;
                imported.IsSelected = true;

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
        SelectedImported.Status = $"Matched to Manual ({SelectedManual.Description} {SelectedManual.Amount:C})";
        SelectedImported.MatchedManualFitId = SelectedManual.FitId;
        SelectedImported.MatchedManualTransactionDate = SelectedManual.TransactionDate;
        SelectedImported.MatchedManualTransactionId = SelectedManual.TransactionId;
        SelectedImported.BillId = SelectedManual.BillId;
        SelectedImported.BucketId = SelectedManual.BucketId;
        SelectedImported.IsSelected = true;

        SelectedManual.IsMatched = true;
        OnPropertyChanged(nameof(SelectedManual));
        OnPropertyChanged(nameof(SelectedImported));
        OnPropertyChanged(nameof(UnreconciledManualTransactions));
    }

    private void ClearMatch() {
        if (SelectedImported == null) return;

        var manual =
            UnreconciledManualTransactions.SingleOrDefault(x => x.FitId == SelectedImported.MatchedManualFitId);

        if (manual != null) {
            SelectedImported.IsReconciled = false;
            SelectedImported.Status = $"Match removed";
            SelectedImported.MatchedManualFitId = null;
            SelectedImported.MatchedManualTransactionDate = null;
            SelectedImported.MatchedManualTransactionId = null;
            SelectedImported.BillId = null;
            SelectedImported.BucketId = null;
            SelectedImported.IsSelected = false;

            manual.IsMatched = false;
        }
    }

    #region Import as new

    private void ImportAsNew() {
        if (SelectedImported == null || SelectedImported.Date == null) return;

        ActiveOverlay = new NewTransactionViewModel(_account, _budgetService, SelectedImported, (childVm, isSaved) => {
            // This code executes when the child calls _closeCallback(...)
            if (isSaved) {
                ImportedTransactions.Remove(SelectedImported);
            }
            else {
                // User canceled, no actions needed on parent data
            }

            // CLOSE THE DIALOG: Setting this to null makes the ContentControl disappear
            ActiveOverlay = null;
        });
    }

    #endregion

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
                Description = transaction.Description, TransactionId = transaction.TransactionId.ToString(),
                BillId = transaction.BillId,
                BucketId = transaction.BucketId
            });
        }

        BillsWithNone.Clear();
        BillsWithNone.Add(new Bill { Id = 0, Name = "None" });
        foreach (var bill in _budgetService.GetAllBills()) {
            BillsWithNone.Add(bill);
        }

        BucketsWithNone.Clear();
        BucketsWithNone.Add(new BudgetBucket { Id = 0, Name = "None" });
        foreach (var bucket in _budgetService.GetAllBuckets()) {
            BucketsWithNone.Add(bucket);
        }
    }
}