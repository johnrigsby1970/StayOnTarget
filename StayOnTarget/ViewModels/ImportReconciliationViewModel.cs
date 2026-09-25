using System.ComponentModel;
using StayOnTarget.Models;
using StayOnTarget.Services;
using System.IO;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using CsvHelper;
using StayOnTarget.Helpers;
using Serilog;

namespace StayOnTarget.ViewModels;

public class ImportReconciliationViewModel : ViewModelBase {
    private ViewModelBase? _activeOverlay;

    public ViewModelBase? ActiveOverlay {
        get => _activeOverlay;
        set {
            try {
                _activeOverlay = value;
                OnPropertyChanged();
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting ActiveOverlay in ImportReconciliationViewModel.");
            }
        }
    }

    public RangeObservableCollection<ImportedTransactionViewModel> ImportedTransactions { get; set; } = new();
    public RangeObservableCollection<ManualTransactionViewModel> UnreconciledManualTransactions { get; set; } = new();

    private ImportedTransactionViewModel? _selectedImported;

    public ImportedTransactionViewModel? SelectedImported {
        get => _selectedImported;
        set {
            try {
                if (SetProperty(ref _selectedImported, value)) {
                    FilterManualSuggestions();
                    LinkTransactionsCommand.NotifyCanExecuteChanged();
                    ImportAsNewCommand.NotifyCanExecuteChanged();
                    ClearMatchCommand.NotifyCanExecuteChanged();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting SelectedImported in ImportReconciliationViewModel.");
            }
        }
    }

    private ManualTransactionViewModel? _selectedManual;

    public ManualTransactionViewModel? SelectedManual {
        get => _selectedManual;
        set {
            try {
                if (SetProperty(ref _selectedManual, value)) {
                    LinkTransactionsCommand.NotifyCanExecuteChanged();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting SelectedManual in ImportReconciliationViewModel.");
            }
        }
    }

    private bool _isBusy;

    public bool IsBusy {
        get => _isBusy;
        set {
            try {
                if (SetProperty(ref _isBusy, value)) { }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting IsBusy in ImportReconciliationViewModel.");
            }
        }
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

    public IRelayCommand LinkTransactionsCommand { get; } = null!;
    public IRelayCommand ImportAsNewCommand { get; } = null!;

    public IRelayCommand ClearMatchCommand { get; } = null!;

    private readonly BudgetService _budgetService = null!;
    private Account _account = null!;

    public IRelayCommand SaveCommand { get; } = null!;
    public IAsyncRelayCommand ImportFileCommand { get; } = null!;

    private CsvImportMappingViewModel? _csvMapping;

    public CsvImportMappingViewModel? CsvMapping {
        get => _csvMapping;
        set {
            try {
                if (_csvMapping != null)
                    _csvMapping.PropertyChanged -= OnCsvMappingPropertyChanged;
                if (SetProperty(ref _csvMapping, value)) {
                    if (_csvMapping != null)
                        _csvMapping.PropertyChanged += OnCsvMappingPropertyChanged;
                    ConfirmCsvImportCommand.NotifyCanExecuteChanged();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting CsvMapping in ImportReconciliationViewModel.");
            }
        }
    }

    private void OnCsvMappingPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        try {
            if (e.PropertyName == nameof(CsvImportMappingViewModel.CanImport))
                ConfirmCsvImportCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in OnCsvMappingPropertyChanged.");
        }
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


    public IAsyncRelayCommand ConfirmCsvImportCommand { get; } = null!;
    public IRelayCommand CancelCsvImportCommand { get; } = null!;

    public RangeObservableCollection<Bill> BillsWithNone { get; } = new();
    public RangeObservableCollection<BudgetBucket> BucketsWithNone { get; } = new();

    public RangeObservableCollection<SubCategory> SubCategoriesWithNone { get; } = new();

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        try {
            if (e.PropertyName == nameof(ImportedTransactionViewModel.IsSelected)) {
                UpdateIsAllSelectedState();
                OnPropertyChanged(nameof(CanImport));
                SaveCommand.NotifyCanExecuteChanged();
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in Item_PropertyChanged for ImportedTransactionViewModel.");
        }
    }

    private bool? _isAllSelected;

    public bool? IsAllSelected {
        get => _isAllSelected;
        set {
            try {
                bool targetState = value ?? false;

                if (_isAllSelected != targetState) {
                    _isAllSelected = targetState;
                    OnPropertyChanged(nameof(IsAllSelected));

                    foreach (var item in ImportedTransactions) {
                        item.PropertyChanged -= Item_PropertyChanged;
                        item.IsSelected = targetState;
                        item.PropertyChanged += Item_PropertyChanged;
                    }

                    OnPropertyChanged(nameof(CanImport));
                    SaveCommand.NotifyCanExecuteChanged();
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "Error setting IsAllSelected in ImportReconciliationViewModel.");
            }
        }
    }

    private void UpdateIsAllSelectedState() {
        try {
            if (ImportedTransactions == null || !ImportedTransactions.Any()) {
                _isAllSelected = false;
            }
            else if (ImportedTransactions.All(x => x.IsSelected)) {
                _isAllSelected = true;
            }
            else if (ImportedTransactions.All(x => !x.IsSelected)) {
                _isAllSelected = false;
            }
            else {
                _isAllSelected = null;
            }

            OnPropertyChanged(nameof(IsAllSelected));
            OnPropertyChanged(nameof(CanImport));
            SaveCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error updating IsAllSelected state.");
        }
    }

    public bool CanImport => ImportedTransactions.Any(x => x.IsSelected);

    public ImportReconciliationViewModel(Account account, BudgetService budgetService) {
        try {
            _account = account;
            _budgetService = budgetService;

            ImportFileCommand = new AsyncRelayCommand(PromptForFileAsync);

            ConfirmCsvImportCommand = new AsyncRelayCommand(ConfirmCsvImportAsync, () => CsvMapping?.CanImport == true);
            CancelCsvImportCommand = new RelayCommand(() => { IsMappingVisible = false; });

            SaveCommand = new RelayCommand(SaveAsync, () => CanImport);

            ImportedTransactions.CollectionChanged += (s, e) => {
                try {
                    if (e.NewItems != null) {
                        foreach (ImportedTransactionViewModel item in e.NewItems) {
                            item.GetDefaultBucketForSubCategory = (subCatId) => {
                                return SubCategoriesWithNone
                                    .FirstOrDefault(sub => sub.Id == subCatId)?
                                    .DefaultBucketId;
                            };

                            item.PropertyChanged -= Item_PropertyChanged;
                            item.PropertyChanged += Item_PropertyChanged;
                        }
                    }

                    if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset ||
                        e.NewItems == null) {
                        foreach (var item in ImportedTransactions) {
                            item.GetDefaultBucketForSubCategory = (subCatId) => {
                                return SubCategoriesWithNone
                                    .FirstOrDefault(sub => sub.Id == subCatId)?
                                    .DefaultBucketId;
                            };

                            item.PropertyChanged -= Item_PropertyChanged;
                            item.PropertyChanged += Item_PropertyChanged;
                        }
                    }

                    if (e.OldItems != null) {
                        foreach (ImportedTransactionViewModel item in e.OldItems) {
                            item.PropertyChanged -= Item_PropertyChanged;
                        }
                    }

                    UpdateIsAllSelectedState();
                }
                catch (Exception ex) {
                    Log.Error(ex, "Error handling ImportedTransactions CollectionChanged.");
                }
            };

            LinkTransactionsCommand = new RelayCommand(
                LinkTransactions,
                () => SelectedImported != null && !SelectedImported.IsMatched && SelectedManual != null &&
                      !SelectedManual.IsMatched
            );

            ImportAsNewCommand = new RelayCommand(
                ImportAsNew,
                () => SelectedImported != null && !SelectedImported.IsMatched
            );

            ClearMatchCommand = new RelayCommand(
                ClearMatch,
                () => SelectedImported != null && SelectedImported.IsMatched
            );

            InitializeDataCommand = new AsyncRelayCommand(LoadDataAsync);
        }
        catch (Exception ex) {
            Log.Fatal(ex, "Critical error initializing ImportReconciliationViewModel.");
        }
    }

    public string LinkButtonText {
        get {
            if (SelectedImported != null && SelectedManual != null &&
                Math.Abs(SelectedImported.Amount) < Math.Abs(SelectedManual.Amount)) {
                return "Split & Link";
            }

            return "Link Selections";
        }
    }

    public IAsyncRelayCommand InitializeDataCommand { get; } = null!;

    private async void SaveAsync() {
        if (!ImportedTransactions.Any()) return;

        // Check if user staged bank items AND highlighted a manual record without clicking "Link"
        var selectedUnmatchedBankItems = ImportedTransactions.Where(x => x.IsSelected && !x.IsMatched).ToList();
    
        if (selectedUnmatchedBankItems.Any() && SelectedManual != null && !SelectedManual.IsMatched) {
            var result = MessageBox.Show(
                $"You have selected bank transaction(s) and manual entry '{SelectedManual.Description}' below, but haven't linked them yet.\n\n" +
                "Would you like to LINK these items before updating?\n\n" +
                "• Yes: Link/Split them and save\n" +
                "• No: Import bank items as NEW transactions\n" +
                "• Cancel: Return to editing",
                "Unlinked Selection Detected",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel) return;

            if (result == MessageBoxResult.Yes) {
                // Ensure the active/highlighted bank row is flagged if user didn't explicitly check it
                if (SelectedImported != null && !SelectedImported.IsMatched && !SelectedImported.IsSelected) {
                    SelectedImported.IsSelected = true;
                }
                
                LinkTransactions(); // Automatically executes your Link/Split logic first
            }
        }
        
        try {
            IsBusy = true;
            await Task.Delay(50);

            var matchedToSave = ImportedTransactions
                .Where(x => x.IsSelected &&
                            x.IsMatched &&
                            !string.IsNullOrEmpty(x.MatchedManualTransactionId) &&
                            x.Id.HasValue &&
                            !string.IsNullOrEmpty(x.BankId) &&
                            !string.IsNullOrEmpty(x.Payee))
                .ToList();

            var newToSave = ImportedTransactions
                .Where(x => x.IsSelected &&
                            !x.IsMatched &&
                            !string.IsNullOrEmpty(x.BankId) &&
                            !string.IsNullOrEmpty(x.Payee))
                .ToList();

            // --- FIX: Group split matches by target manual transaction ---
            var splitGroups = matchedToSave
                .Where(x => x.IsSplitMatch)
                .GroupBy(x => x.MatchedManualTransactionId);

            foreach (var group in splitGroups) {
                var bankItems = group.ToList();
                var manualTxId = group.Key!;

                try {
                    await _budgetService.ProcessMultiMatchSplitAsync(_account.Id, manualTxId, bankItems);
                }
                catch (InvalidOperationException ex) {
                    MessageBox.Show(ex.Message, "Split Not Allowed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            // --- Process Standard 1:1 Matches ---
            var standardMatches = matchedToSave.Where(x => !x.IsSplitMatch);
            foreach (var match in standardMatches) {
                await _budgetService.UpdateTransactionForBankFitIdAsync(
                    _account.Id,
                    match.MatchedManualTransactionId!,
                    match.BankId!,
                    match.IsCleared
                    //, match.Id!.Value
                );
            }

            // --- Process New Imports ---
            foreach (var newItem in newToSave) {
                var t = new Transaction {
                    AccountId = newItem.Amount > 0 ? null : _account.Id,
                    ToAccountId = newItem.Amount > 0 ? _account.Id : null,
                    Amount = newItem.Amount,
                    TransactionDate = newItem.Date ?? DateTime.Now,
                    Description = newItem.Payee!,
                    FromFitId = newItem.Amount > 0 ? "" : newItem.BankId!,
                    ToFitId = newItem.Amount > 0 ? newItem.BankId! : "",
                    BillId = newItem.BillId == 0 ? null : newItem.BillId,
                    BucketId = newItem.BucketId == 0 ? null : newItem.BucketId,
                    SubCategoryId = newItem.SubCategoryId == 0 ? null : newItem.SubCategoryId,
                    FromAccountIsCleared = newItem.Amount > 0 ? null : newItem.IsCleared,
                    ToAccountIsCleared = newItem.Amount > 0 ? newItem.IsCleared : null,
                };

                await _budgetService.UpsertTransactionAsync(t);
            }

            // Reload data from SQLite so remainder records appear on screen
            await LoadDataAsync();

            var savedItems = matchedToSave.Concat(newToSave).ToList();
            foreach (var item in savedItems) {
                ImportedTransactions.Remove(item);
            }

            if (ImportedTransactions.Any()) {
                AutoMatchTransactions();
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error during SaveAsync in ImportReconciliationViewModel.");
            MessageBox.Show("Failed to save imported transactions. See log for details.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally {
            IsBusy = false;
        }
    }

    private async Task PromptForFileAsync() {
        try {
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
                    await ParseAndPopulateQfxAsync(LastFileName);
                }
                else {
                    var mappingPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                        $"mapping_{_account.Id}.json");
                    CsvMapping = new CsvImportMappingViewModel(LastFileName, mappingPath);

                    IsMappingVisible = true;
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error prompting for import file.");

            MessageBox.Show("Failed to open file dialog. See log for details.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task ConfirmCsvImportAsync() {
        try {
            if (CsvMapping == null || !CsvMapping.CanImport) return;

            var mappingPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"mapping_{_account.Id}.json");
            CsvMapping.SaveMapping(mappingPath);

            await ParseAndPopulateCsv(CsvMapping.FilePath);
            IsMappingVisible = false;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error confirming CSV import.");

            MessageBox.Show("Failed to import CSV file. See log for details.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task ParseAndPopulateCsv(string filePath) {
        try {
            if (!File.Exists(filePath) || CsvMapping == null) return;
            LastImportAsQfx = false;

            ImportedTransactions.Clear();

            var rawParsedRecords = new List<ImportedTransactionViewModel>();

            using (var reader = new StreamReader(filePath))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture)) {
                await csv.ReadAsync();
                csv.ReadHeader();

                while (await csv.ReadAsync()) {
                    string bankId = csv.GetField(CsvMapping.BankIdHeader!) ?? Guid.NewGuid().ToString();
                    string rawDate = csv.GetField(CsvMapping.DateHeader!) ?? "";
                    string rawAmount = csv.GetField(CsvMapping.AmountHeader!) ?? "";
                    string payee = csv.GetField(CsvMapping.PayeeHeader!) ?? "";

                    if (!DateTime.TryParse(rawDate, CultureInfo.CurrentCulture, DateTimeStyles.None, out var date)) {
                        continue;
                    }

                    decimal.TryParse(rawAmount, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount);

                    rawParsedRecords.Add(new ImportedTransactionViewModel {
                        BankId = bankId,
                        Date = date,
                        Amount = amount,
                        Payee = payee.Trim(),
                        Status = "Unmatched",
                        IsCleared = true
                    });
                }
            }

            if (!rawParsedRecords.Any()) return;

            var incomingBankIds = rawParsedRecords
                .Where(x => !string.IsNullOrWhiteSpace(x.BankId))
                .Select(x => x.BankId!)
                .ToList();

            var existingDbIds = await _budgetService.GetAlreadyImportedBankIdsAsync(_account.Id, incomingBankIds);
            var processedBankIds = new HashSet<string>(existingDbIds, StringComparer.OrdinalIgnoreCase);

            var manualLookup = UnreconciledManualTransactions
                .Where(x => !string.IsNullOrWhiteSpace(x.FitId))
                .ToLookup(x => x.FitId!, StringComparer.OrdinalIgnoreCase);

            var filteredImports = new List<ImportedTransactionViewModel>(rawParsedRecords.Count);
            var manualRecordsToRemove = new List<ManualTransactionViewModel>();

            foreach (var record in rawParsedRecords) {
                if (processedBankIds.Contains(record.BankId!)) {
                    var matches = manualLookup[record.BankId!];
                    manualRecordsToRemove.AddRange(matches);
                }
                else {
                    filteredImports.Add(record);
                }
            }

            foreach (var rec in manualRecordsToRemove) {
                UnreconciledManualTransactions.Remove(rec);
            }

            if (ImportedTransactions is RangeObservableCollection<ImportedTransactionViewModel> rangeCollection) {
                rangeCollection.AddRange(filteredImports);
            }
            else {
                foreach (var item in filteredImports) {
                    ImportedTransactions.Add(item);
                }
            }

            AutoMatchTransactions();
            await AutoApplySubCategory();
            AutoApplyBill();

            OnPropertyChanged(nameof(CanImport));
            SaveCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error parsing and populating CSV file.");

            MessageBox.Show("Failed to parse CSV file. See log for details.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task ParseAndPopulateQfxAsync(string filePath) {
        try {
            if (!File.Exists(filePath)) return;

            LastImportAsQfx = true;
            ImportedTransactions.Clear();

            string content = await File.ReadAllTextAsync(filePath);

            var matches = Regex.Matches(content, @"<STMTTRN>(.*?)(?=</STMTTRN>|<STMTTRN>|</STMTRS>)",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            var rawParsedRecords = new List<ImportedTransactionViewModel>();

            foreach (Match m in matches) {
                string txBlock = m.Groups[1].Value;
                string bankId = GetQfxTagValue(txBlock, "FITID");
                if (string.IsNullOrWhiteSpace(bankId)) continue;

                string rawDate = GetQfxTagValue(txBlock, "DTPOSTED");
                string rawAmount = GetQfxTagValue(txBlock, "TRNAMT");
                string payee = GetQfxTagValue(txBlock, "NAME");

                DateTime date = DateTime.Today;
                if (rawDate.Length >= 8 && DateTime.TryParseExact(rawDate.Substring(0, 8), "yyyyMMdd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate)) {
                    date = parsedDate;
                }

                decimal.TryParse(rawAmount, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal amount);

                rawParsedRecords.Add(new ImportedTransactionViewModel {
                    BankId = bankId,
                    Date = date,
                    Amount = amount,
                    Payee = payee,
                    Status = "Unmatched",
                    IsCleared = true
                });
            }

            if (!rawParsedRecords.Any()) return;

            var incomingBankIds = rawParsedRecords
                .Where(x => !string.IsNullOrWhiteSpace(x.BankId))
                .Select(x => x.BankId!)
                .ToList();

            var existingDbIds = await _budgetService.GetAlreadyImportedBankIdsAsync(_account.Id, incomingBankIds);
            var processedBankIds = new HashSet<string>(existingDbIds, StringComparer.OrdinalIgnoreCase);

            var manualLookup = UnreconciledManualTransactions
                .Where(x => !string.IsNullOrWhiteSpace(x.FitId))
                .ToLookup(x => x.FitId!, StringComparer.OrdinalIgnoreCase);

            var filteredImports = new List<ImportedTransactionViewModel>(rawParsedRecords.Count);
            var transactionsToRemove = new List<ManualTransactionViewModel>();

            foreach (var record in rawParsedRecords) {
                if (processedBankIds.Contains(record.BankId!)) {
                    var matchesToRemove = manualLookup[record.BankId!];
                    transactionsToRemove.AddRange(matchesToRemove);
                }
                else {
                    filteredImports.Add(record);
                }
            }

            foreach (var rec in transactionsToRemove) {
                UnreconciledManualTransactions.Remove(rec);
            }

            if (ImportedTransactions is RangeObservableCollection<ImportedTransactionViewModel> rangeCollection) {
                rangeCollection.AddRange(filteredImports);
            }
            else {
                foreach (var item in filteredImports) {
                    ImportedTransactions.Add(item);
                }
            }

            AutoMatchTransactions();
            await AutoApplySubCategory();

            OnPropertyChanged(nameof(CanImport));
            SaveCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error parsing and populating QFX/OFX file.");

            MessageBox.Show("Failed to parse QFX/OFX file. See log for details.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task AutoApplySubCategory() {
        try {
            foreach (var x in ImportedTransactions.Where(x => !x.IsMatched)) {
                if (x.SubCategoryId == null && !string.IsNullOrWhiteSpace(x.Payee)) {
                    x.SubCategoryId = await _budgetService.GetSuggestedSubCategoryIdAsync(x.Payee, x.Date);
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in AutoApplySubCategory.");
        }
    }
    
    private void AutoApplyBill() {
        try {
            foreach (var x in ImportedTransactions.Where(x => !x.IsMatched)) {
                // Only suggest if one hasn't been set yet and we have a Payee to search with
                if (x.BillId == null && !string.IsNullOrWhiteSpace(x.Payee)) {
                
                    // Search the full list in memory (not the UI view) for a match
                    var suggestedBill = BillsWithNone.FirstOrDefault(b => 
                        b.Id != 0 && // Ignore the "None" placeholder
                        b.Name.Contains(x.Payee, StringComparison.OrdinalIgnoreCase));
                
                    if (suggestedBill != null) {
                        // This sets the selected item in the UI, but leaves the dropdown list full
                        x.BillId = suggestedBill.Id;
                    }
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in AutoApplyBill.");
        }
    }

    private string GetQfxTagValue(string block, string tag) {
        try {
            var match = Regex.Match(block, $@"<{tag}>([^<\r\n]+)", RegexOptions.IgnoreCase);
            if (!match.Success) return string.Empty;

            string value = match.Groups[1].Value;

            if (value.Contains("</")) {
                value = value.Split(new[] { "</" }, StringSplitOptions.None)[0];
            }

            return value.Trim();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error extracting QFX tag value for tag {Tag}.", tag);

            return string.Empty;
        }
    }

    private void AutoMatchTransactions() {
        try {
            var matchedManualIds = new HashSet<string>();

            foreach (var imported in ImportedTransactions.Where(x => x.Date != null)) {
                if (imported.Date == null || imported.Date == DateTime.MinValue) {
                    continue;
                }

                var exactMatch = UnreconciledManualTransactions.FirstOrDefault(m =>
                    !string.IsNullOrEmpty(m.TransactionId) &&
                    !matchedManualIds.Contains(m.TransactionId) &&
                    Math.Abs(m.Amount) == Math.Abs(imported.Amount) &&
                    Math.Abs((m.TransactionDate - imported.Date.Value).TotalDays) == 0 &&
                    TransactionMatcher.IsMatch(imported.Payee ?? "", m.Description));

                if (exactMatch != null) {
                    ApplyMatch(imported, exactMatch, $"Auto-Matched ({exactMatch.Description})");
                    matchedManualIds.Add(exactMatch.TransactionId!);
                    imported.BillId = exactMatch.BillId;
                    imported.BucketId = exactMatch.BucketId;
                    imported.IsSelected = true;
                    imported.IsCleared = true;
                    continue;
                }

                var closerMatch = UnreconciledManualTransactions.FirstOrDefault(m =>
                    !string.IsNullOrEmpty(m.TransactionId) &&
                    !matchedManualIds.Contains(m.TransactionId) &&
                    Math.Abs(m.Amount) == Math.Abs(imported.Amount) &&
                    Math.Abs((m.TransactionDate - imported.Date.Value).TotalDays) <= 4 &&
                    TransactionMatcher.IsMatch(imported.Payee ?? "", m.Description));

                if (closerMatch != null) {
                    ApplyMatch(imported, closerMatch, $"Auto-Matched ({closerMatch.Description})");
                    matchedManualIds.Add(closerMatch.TransactionId!);
                    continue;
                }

                var closeMatch = UnreconciledManualTransactions.FirstOrDefault(m =>
                    !string.IsNullOrEmpty(m.TransactionId) &&
                    !matchedManualIds.Contains(m.TransactionId) &&
                    Math.Abs(m.Amount) == Math.Abs(imported.Amount) &&
                    Math.Abs((m.TransactionDate - imported.Date.Value).TotalDays) == 0);

                if (closeMatch != null) {
                    ApplyMatch(imported, closeMatch, $"Auto-Matched ({closeMatch.Description})");
                    matchedManualIds.Add(closeMatch.TransactionId!);
                    continue;
                }

                int ambiguousCount = UnreconciledManualTransactions.Count(m =>
                    !string.IsNullOrEmpty(m.TransactionId) &&
                    !matchedManualIds.Contains(m.TransactionId) &&
                    Math.Abs(m.Amount) == Math.Abs(imported.Amount) &&
                    Math.Abs((m.TransactionDate - imported.Date.Value).TotalDays) <= 4);

                if (ambiguousCount > 1) {
                    continue;
                }

                var match = UnreconciledManualTransactions.FirstOrDefault(m =>
                    !string.IsNullOrEmpty(m.TransactionId) &&
                    !matchedManualIds.Contains(m.TransactionId) &&
                    Math.Abs(m.Amount) == Math.Abs(imported.Amount) &&
                    Math.Abs((m.TransactionDate - imported.Date.Value).TotalDays) <= 4);

                if (match != null) {
                    ApplyMatch(imported, match, $"Auto-Matched ({match.Description})");
                    matchedManualIds.Add(match.TransactionId!);
                }
            }

            var firstUnmatchedImport = ImportedTransactions.FirstOrDefault(x => !x.IsMatched);
            if (firstUnmatchedImport != null) {
                SelectedImported = firstUnmatchedImport;
                SelectedManual = UnreconciledManualTransactions.FirstOrDefault(m =>
                    !m.IsMatched && Math.Abs(m.Amount) == Math.Abs(firstUnmatchedImport.Amount));
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in AutoMatchTransactions.");
        }
    }

    private void ApplyMatch(ImportedTransactionViewModel imported, ManualTransactionViewModel manual,
        string statusText) {
        try {
            imported.Id = manual.Id;
            imported.IsMatched = true;
            imported.IsCleared = true;
            imported.IsSelected = true;
            imported.Status = statusText;
            imported.MatchedManualFitId = manual.FitId;
            imported.MatchedManualTransactionDate = manual.TransactionDate;
            imported.MatchedManualTransactionId = manual.TransactionId;
            imported.BillId = manual.BillId;
            imported.BucketId = manual.BucketId;
            
            manual.IsMatched = true;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in ApplyMatch.");
        }
    }

    private void FilterManualSuggestions() {
        try {
            // Optional filter logic placeholder
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in FilterManualSuggestions.");
        }
    }

    private void LinkTransactions() {
        try {
            // Now evaluates ALL rows highlighted in the top grid
            var targetedImports = ImportedTransactions
                .Where(x => x.IsSelected && !x.IsMatched)
                .ToList();

            if (!targetedImports.Any() || SelectedManual == null) {
                MessageBox.Show("Please select at least one bank transaction and a manual entry below.",
                    "Selection Required", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            decimal totalImportedAmount = targetedImports.Sum(x => Math.Abs(x.Amount));
            decimal manualAmount = Math.Abs(SelectedManual.Amount);

            if (totalImportedAmount > manualAmount) {
                MessageBox.Show(
                    $"Selected bank transactions ({totalImportedAmount:C}) exceed the manual entry amount ({manualAmount:C}).",
                    "Amount Mismatch", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Handle partial splits if bank total is strictly less than manual entry
            if (totalImportedAmount < manualAmount) {
                decimal remainderAmount = manualAmount - totalImportedAmount;

                var confirmResult = MessageBox.Show(
                    $"The selected bank items ({totalImportedAmount:C}) partially match your manual entry ({manualAmount:C}).\n\n" +
                    $"Linking will split this entry and leave an uncleared remainder for {remainderAmount:C}.\n\nDo you want to proceed?",
                    "Confirm Partial Match & Split",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirmResult != MessageBoxResult.Yes) return;

                SelectedManual.Amount = totalImportedAmount;

                var remainderVm = new ManualTransactionViewModel {
                    Id = null,
                    TransactionId = Guid.NewGuid().ToString(),
                    TransactionDate = SelectedManual.TransactionDate,
                    Amount = remainderAmount,
                    Description = $"{SelectedManual.Description} (Remainder)",
                    BillId = SelectedManual.BillId,
                    BucketId = SelectedManual.BucketId,
                    IsMatched = false
                };

                UnreconciledManualTransactions.Add(remainderVm);
            }

            // Apply matched state and check both boxes
            bool isSplit = targetedImports.Count > 1 || totalImportedAmount < manualAmount;

            foreach (var bankItem in targetedImports) {
                bankItem.Id = SelectedManual.Id;
                bankItem.IsMatched = true;
                bankItem.IsCleared = true;
                bankItem.IsSelected = true; // Confirms both checked states!
                bankItem.IsSplitMatch = isSplit;
                bankItem.Status = isSplit
                    ? $"Split Matched ({SelectedManual.Description})"
                    : $"Matched ({SelectedManual.Description})";

                bankItem.MatchedManualFitId = SelectedManual.FitId;
                bankItem.MatchedManualTransactionDate = SelectedManual.TransactionDate;
                bankItem.MatchedManualTransactionId = SelectedManual.TransactionId;
                bankItem.BillId = SelectedManual.BillId;
                bankItem.BucketId = SelectedManual.BucketId;
            }

            SelectedManual.IsMatched = true;

            OnPropertyChanged(nameof(SelectedManual));
            OnPropertyChanged(nameof(ImportedTransactions));
            OnPropertyChanged(nameof(UnreconciledManualTransactions));
        }
        catch (Exception ex) {
            Log.Error(ex, "Error linking transactions.");
        }
    }

    private void ClearMatch() {
        try {
            if (SelectedImported == null) return;

            var manual =
                UnreconciledManualTransactions.SingleOrDefault(x => x.FitId == SelectedImported.MatchedManualFitId);

            if (manual != null) {
                SelectedImported.Id = null;
                SelectedImported.IsMatched = false;
                SelectedImported.IsCleared = false;
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
        catch (Exception ex) {
            Log.Error(ex, "Error clearing transaction match.");
        }
    }

    #region Import as new

    private void ImportAsNew() {
        try {
            if (SelectedImported == null || SelectedImported.Date == null) return;

            if (string.IsNullOrWhiteSpace(SelectedImported?.Payee) ||
                SelectedImported.Date == null ||
                string.IsNullOrWhiteSpace(SelectedImported.BankId)) {
                MessageBox.Show(
                    "The transaction lacks required fields: payee, transaction date, and a bank transaction id.",
                    "Missing Information", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ActiveOverlay = new NewTransactionViewModel(_account, _budgetService, SelectedImported,
                (childVm, isSaved) => {
                    try {
                        if (isSaved) {
                            ImportedTransactions.Remove(SelectedImported);
                        }

                        ActiveOverlay = null;
                    }
                    catch (Exception ex) {
                        Log.Error(ex, "Error in NewTransactionViewModel callback.");
                    }
                });
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in ImportAsNew.");
        }
    }

    #endregion

    private async Task LoadDataAsync() {
        try {
            var unreconciledTransactions = (await _budgetService.GetAllUnclearedTransactionsAsync()).ToList();
            unreconciledTransactions = unreconciledTransactions
                .Where(x => (x.AccountId == _account.Id && x.FromAccountIsCleared == false) ||
                            (x.ToAccountId == _account.Id && x.ToAccountIsCleared == false)).ToList();

            UnreconciledManualTransactions.Clear();
            var temp = new List<ManualTransactionViewModel>(unreconciledTransactions.Count);
            foreach (var transaction in unreconciledTransactions) {
                temp.Add(new ManualTransactionViewModel {
                    Id = (int?)(transaction.AccountId == _account.Id
                        ? transaction.FromRecordId
                        : transaction.ToRecordId),
                    FitId = (transaction.AccountId == _account.Id ? transaction.FromFitId : transaction.ToFitId),
                    TransactionDate = transaction.TransactionDate,
                    Amount = transaction.Amount,
                    Description = transaction.Description, TransactionId = transaction.TransactionId.ToString(),
                    BillId = transaction.BillId,
                    BucketId = transaction.BucketId
                });
            }

            UnreconciledManualTransactions.AddRange(temp);

            var billsFromDb = (await _budgetService.GetAllBillsAsync()).ToList();

            var billsTemp = new List<Bill>(billsFromDb.Count + 1) {
                new Bill { Id = 0, Name = "None" }
            };

            billsTemp.AddRange(billsFromDb);

            BillsWithNone.Clear();
            BillsWithNone.AddRange(billsTemp);

            var bucketsFromDb = (await _budgetService.GetAllBucketsAsync()).ToList();

            var bucketsTemp = new List<BudgetBucket>(bucketsFromDb.Count + 1) {
                new BudgetBucket { Id = 0, Name = "None" }
            };

            bucketsTemp.AddRange(bucketsFromDb);

            BucketsWithNone.Clear();
            BucketsWithNone.AddRange(bucketsTemp);

            var subCategoriesFromDb = (await _budgetService.GetAllSubCategoriesAsync()).ToList();

            var subCategoriesTemp = new List<SubCategory>(subCategoriesFromDb.Count + 1) {
                new SubCategory { Id = 0, Name = "None" }
            };

            subCategoriesTemp.AddRange(subCategoriesFromDb);

            SubCategoriesWithNone.Clear();
            SubCategoriesWithNone.AddRange(subCategoriesTemp);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error loading import reconciliation data.");

            MessageBox.Show("Failed to load reconciliation data. See log for details.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}