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

    public ImportReconciliationViewModel(Account account, BudgetService budgetService) {
        _account = account;
        _budgetService = budgetService;

        ImportQfxCommand = new RelayCommand(param => PromptAndLoadQfx());
        ImportCsvCommand = new RelayCommand(param => PromptAndLoadCsv());
        ConfirmCsvImportCommand = new RelayCommand(param => ConfirmCsvImport(), param => CsvMapping?.CanImport == true);
        CancelCsvImportCommand = new RelayCommand(param => { IsMappingVisible = false; });

        CancelNewTransactionCommand = new RelayCommand(param => CancelNewTransaction());
        SaveNewTransactionCommand =
            new RelayCommand(param => _ = SaveTransaction(), param => EditingTransactionClone != null);

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
        
        ClearMatchCommand = new RelayCommand(
            param => ClearMatch(),
            param => SelectedImported!=null && SelectedImported.IsReconciled
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

        foreach (var match in ImportedTransactions.Where(x =>
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

        // 3. Pause execution here until ALL database operations are 100% complete
        if (saveTasks.Any()) {
            await Task.WhenAll(saveTasks);
        }

        // 4. Now these run sequentially on the UI thread with accurate database states
        LoadData();

        if (LastImportAsQfx!=null && LastImportAsQfx.Value) {
            ParseAndPopulateQfx(LastFileName); 
        }
        if (LastImportAsQfx!=null && !LastImportAsQfx.Value) {
            ParseAndPopulateCsv(LastFileName); 
        }
    }

    private void PromptAndLoadQfx() {
        var openFileDialog = new Microsoft.Win32.OpenFileDialog {
            Filter = "QFX Files (*.qfx)|*.qfx|OFX Files (*.ofx)|*.ofx",
            Title = "Select Bank QFX File"
        };

        if (openFileDialog.ShowDialog() == true) {
            LastFileName = openFileDialog.FileName;
            ParseAndPopulateQfx(LastFileName);
        }
    }

    private void PromptAndLoadCsv() {
        
        var openFileDialog = new Microsoft.Win32.OpenFileDialog {
            Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
            Title = "Select Bank CSV File"
        };

        if (openFileDialog.ShowDialog() == true) {
            LastFileName = openFileDialog.FileName;
            var mappingPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"mapping_{_account.Id}.json");
            CsvMapping = new CsvImportMappingViewModel(LastFileName, mappingPath);
            IsMappingVisible = true;
        }
    }

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
                decimal amount = 0;
                decimal.TryParse(rawAmount, NumberStyles.Any, CultureInfo.InvariantCulture, out amount);

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
        foreach (var imported in ImportedTransactions.Where(x=>x.Date!=null)) {
            if (imported.Date==null || imported.Date == DateTime.MinValue) {
                //it is a pending transaction at the bank (BoA as an example)
                continue;
            }
            // Look for a manual entry with the exact amount and a date within a 4-day window
            if (UnreconciledManualTransactions.Count(m =>
                    m.Amount == imported.Amount &&
                    Math.Abs((m.TransactionDate - imported.Date.Value).TotalDays) <= 4) > 1) {
                continue;
            }
            var match = UnreconciledManualTransactions.FirstOrDefault(m =>
                m.Amount == imported.Amount &&
                Math.Abs((m.TransactionDate - imported.Date.Value).TotalDays) <= 4);

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
        SelectedImported.Status = $"Matched to Manual ({SelectedManual.Description} {SelectedManual.Amount:C})";
        SelectedImported.MatchedManualFitId = SelectedManual.FitId;
        SelectedImported.MatchedManualTransactionDate = SelectedManual.TransactionDate;
        SelectedImported.MatchedManualTransactionId = SelectedManual.TransactionId;
        
        SelectedManual.IsMatched = true;
        OnPropertyChanged(nameof(SelectedManual));
        OnPropertyChanged(nameof(SelectedImported));
        OnPropertyChanged(nameof(UnreconciledManualTransactions));
        
        //UnreconciledManualTransactions.Remove(SelectedManual);
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
        
            manual.IsMatched = false;
        }
        
        // OnPropertyChanged(nameof(ImportedTransactions));
        // OnPropertyChanged(nameof(CurrentPeriodBuckets));
        
        //UnreconciledManualTransactions.Remove(SelectedManual);
    }

    #region Import as new

    private ObservableCollection<Account> _accounts = new();


    private ObservableCollection<Paycheck> _paychecks = new();
    
    private ObservableCollection<BudgetBucket> _buckets = new();
    private DateTime _currentPeriodDate = DateTime.MinValue;

    private ObservableCollection<Account> _accountsWithNone = new();

    public ObservableCollection<Account> AccountsWithNone {
        get => _accountsWithNone;
        set => SetProperty(ref _accountsWithNone, value);
    }

    private ObservableCollection<Bill> _billsWithNone = new();

    public ObservableCollection<Bill> BillsWithNone {
        get => _billsWithNone;
        set => SetProperty(ref _billsWithNone, value);
    }

    private ObservableCollection<BudgetBucket> _bucketsWithNone = new();

    public ObservableCollection<BudgetBucket> BucketsWithNone {
        get => _bucketsWithNone;
        set => SetProperty(ref _bucketsWithNone, value);
    }

    private ObservableCollection<Bill> _bills = new();

    public ObservableCollection<Bill> Bills {
        get => _bills;
        set => SetProperty(ref _bills, value);
    }

    private Transaction? _editingTransactionClone;

    public Transaction? EditingTransactionClone {
        get => _editingTransactionClone;
        set => SetProperty(ref _editingTransactionClone, value);
    }

    private ObservableCollection<PeriodBill> _currentPeriodBills = new();

    public ObservableCollection<PeriodBill> CurrentPeriodBills {
        get => _currentPeriodBills;
        set => SetProperty(ref _currentPeriodBills, value);
    }

    private ObservableCollection<PeriodBucket> _currentPeriodBuckets = new();

    public ObservableCollection<PeriodBucket> CurrentPeriodBuckets {
        get => _currentPeriodBuckets;
        set => SetProperty(ref _currentPeriodBuckets, value);
    }

    public DateTime CurrentPeriodDate {
        get => _currentPeriodDate;
        set {
            if (SetProperty(ref _currentPeriodDate, value)) {
                LoadPeriodData();
            }
        }
    }

    private ObservableCollection<Transaction> _currentPeriodTransactions = new();

    public ObservableCollection<Transaction> CurrentPeriodTransactions {
        get => _currentPeriodTransactions;
        set => SetProperty(ref _currentPeriodTransactions, value);
    }
    
    private ObservableCollection<Paycheck> _paychecksWithNone = new();
    
    public ObservableCollection<Paycheck> PaychecksWithNone {
        get => _paychecksWithNone;
        set => SetProperty(ref _paychecksWithNone, value);
    }
    
    private void LoadPeriodData() {
        try {
            var accounts = _budgetService.GetAllAccounts().ToList();
            if (accounts.All(a => a.Name != "Household Cash")) {
                var cashAccount = new Account {
                    Name = "Household Cash",
                    Type = AccountType.Savings,
                    Balance = 0,
                    IncludeInTotal = true
                };
                _budgetService.UpsertAccount(cashAccount);
                accounts = _budgetService.GetAllAccounts().ToList();
            }

            accounts = accounts.OrderBy(b => b.Name).ToList();
            // foreach (var a in accounts) a.PropertyChanged += Item_PropertyChanged;
            // Accounts = new ObservableCollection<Account>(accounts);

            var accountsWithNone = new List<Account> { new Account { Id = 0, Name = "(None)" } };
            accountsWithNone.AddRange(accounts);
            AccountsWithNone = new ObservableCollection<Account>(accountsWithNone);

            var bills = _budgetService.GetAllBills();
            bills = bills.OrderBy(b => b.DueDay).ThenBy(b => b.Name).ToList();
            // foreach (var b in bills) b.PropertyChanged += Item_PropertyChanged;
            // Bills = new ObservableCollection<Bill>(bills);

            var billsWithNone = new List<Bill> { new Bill { Id = 0, Name = "(None)" } };
            billsWithNone.AddRange(bills);
            BillsWithNone = new ObservableCollection<Bill>(billsWithNone);

            var paychecks = _budgetService.GetAllPaychecks();
            paychecks = paychecks.OrderBy(b => b.Name).ToList();
            // foreach (var p in paychecks) p.PropertyChanged += Item_PropertyChanged;
            // Paychecks = new ObservableCollection<Paycheck>(paychecks);

            var paychecksWithNone = new List<Paycheck> { new Paycheck { Id = 0, Name = "(None)" } };
            paychecksWithNone.AddRange(paychecks);
            PaychecksWithNone = new ObservableCollection<Paycheck>(paychecksWithNone);

            var buckets = _budgetService.GetAllBuckets();
            buckets = buckets.OrderBy(b => b.Name).ToList();
            // foreach (var b in buckets) b.PropertyChanged += Item_PropertyChanged;
            // Buckets = new ObservableCollection<BudgetBucket>(buckets);

            var bucketsWithNone = new List<BudgetBucket> { new BudgetBucket { Id = 0, Name = "(None)" } };
            bucketsWithNone.AddRange(buckets);
            BucketsWithNone = new ObservableCollection<BudgetBucket>(bucketsWithNone);
        }
        finally {
            // _isLoadingData = false;
        }
        
        LoadPeriodBills();
        LoadPeriodBuckets();
        LoadPeriodTransactions();
    }

    private void LoadPeriodBills() {
        var pBills = _budgetService.GetPeriodBills(CurrentPeriodDate).ToList();
        pBills = pBills.OrderBy(pb => pb.DueDate).ToList();

        CurrentPeriodBills = new ObservableCollection<PeriodBill>(pBills);
        OnPropertyChanged(nameof(CurrentPeriodBills));
    }

    private void LoadPeriodBuckets() {
        var pBuckets = _budgetService.GetPeriodBucketsIncludingMonthly(CurrentPeriodDate).ToList();
        CurrentPeriodBuckets = new ObservableCollection<PeriodBucket>(pBuckets);
        OnPropertyChanged(nameof(CurrentPeriodBuckets));
    }

    private void LoadPeriodTransactions() {
        var transactions = _budgetService.GetTransactions(CurrentPeriodDate).ToList();
        transactions = transactions.OrderBy(pb => pb.TransactionDate).ToList();
        CurrentPeriodTransactions = new ObservableCollection<Transaction>(transactions);
        OnPropertyChanged(nameof(CurrentPeriodTransactions));
    }


    private ObservableCollection<Paycheck> PeriodPaychecks { get; set; }

    public ObservableCollection<Paycheck> Paychecks {
        get => _paychecks;
        set => SetProperty(ref _paychecks, value);
    }

    private void ImportAsNew() {
        if (SelectedImported == null || SelectedImported.Date==null) return;

        EditingTransactionClone = new Transaction {
            Description = SelectedImported.Payee,
            Memo = "", Amount = Math.Abs(SelectedImported.Amount), 
            TransactionDate = SelectedImported.Date.Value,
            FitId = SelectedImported.BankId, 
            AccountId = SelectedImported.Amount > 0 ? null : _account.Id,
            AccountName = SelectedImported.Amount > 0 ? null : _account.Name,
            ToAccountName = SelectedImported.Amount > 0 ? _account.Name : null
        };
        
        LoadPaychecks();

        EditingTransactionClone.PeriodDate = CurrentPeriodDate;
        
        IsNewTransactionFormVisible = true;
    }

    private void LoadPaychecks() {
        var paychecks = _budgetService.GetAllPaychecks();
        paychecks = paychecks.OrderBy(b => b.Name).ToList();
        Paychecks = new ObservableCollection<Paycheck>(paychecks);
        
        var allPaychecks = Paychecks.ToList();
        if (allPaychecks.Count == 0) {
            CurrentPeriodDate = DateTime.Today;
            return;
        }

        PeriodPaychecks = new ObservableCollection<Paycheck>(allPaychecks);
        
        var paychecksWithNone = new List<Paycheck> { new Paycheck { Id = 0, Name = "(None)" } };
        paychecksWithNone.AddRange(paychecks);
        PaychecksWithNone = new ObservableCollection<Paycheck>(paychecksWithNone);

        SetCurrentPeriodDate();
    }

    private void SetCurrentPeriodDate(int? id = null) {
        if (EditingTransactionClone == null) return;
        
        var allPaychecks = Paychecks.ToList();
        if (allPaychecks.Count == 0) {
            CurrentPeriodDate = DateTime.Today;
            return;
        }

        DateTime latestPayBeforeToday = DateTime.MinValue;
        foreach (var pay in allPaychecks.Where(p => id == null || p.Id == id)) {
            var nextPay = pay.StartDate;
            while (nextPay <= EditingTransactionClone.TransactionDate) {
                if (nextPay <= EditingTransactionClone.TransactionDate && nextPay > latestPayBeforeToday)
                    latestPayBeforeToday = nextPay;

                nextPay = pay.Frequency switch {
                    Frequency.Weekly => nextPay.AddDays(7),
                    Frequency.BiWeekly => nextPay.AddDays(14),
                    Frequency.Monthly => nextPay.AddMonths(1),
                    _ => nextPay.AddYears(100)
                };
            }
        }

        if (latestPayBeforeToday != DateTime.MinValue)
            CurrentPeriodDate = latestPayBeforeToday;
        else if (allPaychecks.Any())
            CurrentPeriodDate = allPaychecks.Min(p => p.StartDate);

        var currentPeriodPaychecks = new List<Paycheck>();
        foreach (var pay in allPaychecks.Where(p => id == null || p.Id == id)) {
            var nextPay = pay.StartDate;
            var found = false;
            while (nextPay <= CurrentPeriodDate) {
                if (nextPay.Date == CurrentPeriodDate.Date) {
                    found = true;
                    break;
                }

                nextPay = pay.Frequency switch {
                    Frequency.Weekly => nextPay.AddDays(7),
                    Frequency.BiWeekly => nextPay.AddDays(14),
                    Frequency.Monthly => nextPay.AddMonths(1),
                    _ => nextPay.AddYears(100)
                };
            }

            if (found) currentPeriodPaychecks.Add(pay);
        }

        if (id == null && currentPeriodPaychecks.Any()) {
            EditingTransactionClone.PaycheckId = currentPeriodPaychecks.First().Id;
            OnPropertyChanged(nameof(EditingTransactionClone));
        }
    }
    
    private void CancelNewTransaction() {
        if (EditingTransactionClone == null) return;
        IsNewTransactionFormVisible = false;
        EditingTransactionClone = null;
    }

    private async Task SaveTransaction() {
        if (EditingTransactionClone == null) return;

        if (EditingTransactionClone.AccountId == 0) EditingTransactionClone.AccountId = null;
        if (EditingTransactionClone.ToAccountId == 0) EditingTransactionClone.ToAccountId = null;
        if (EditingTransactionClone.BillId == 0) EditingTransactionClone.BillId = null;
        if (EditingTransactionClone.BucketId == 0) EditingTransactionClone.BucketId = null;

        await _budgetService.UpsertTransactionAsync(EditingTransactionClone);
        if(SelectedImported!=null && SelectedImported.BankId==EditingTransactionClone.FitId) {
            // SelectedImported.IsReconciled = false;//for purposes of this screen. 
            // SelectedImported.Status = "Created";
            ImportedTransactions.Remove(SelectedImported);
            
        }
        IsNewTransactionFormVisible = false;
        EditingTransactionClone = null;
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
                Description = transaction.Description, TransactionId = transaction.TransactionId.ToString()
            });
        }
    }
}