using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CsvHelper;
using System.Globalization;

namespace StayOnTarget.ViewModels;

public class CsvImportMappingViewModel : ViewModelBase {
    public string FilePath { get; }
    public List<string> Headers { get; private set; } = new();
    private List<Dictionary<string, string>> _rawPreviewData = new();
    public ObservableCollection<ImportedTransactionViewModel> PreviewRows { get; } = new();

    public ObservableCollection<string?> AvailableHeaders { get; } = new();

    private string? _dateHeader;
    public string? DateHeader {
        get => _dateHeader;
        set {
            if (SetProperty(ref _dateHeader, value)) {
                RefreshPreview();
                OnPropertyChanged(nameof(CanImport));
            }
        }
    }

    private string? _amountHeader;
    public string? AmountHeader {
        get => _amountHeader;
        set {
            if (SetProperty(ref _amountHeader, value)) {
                RefreshPreview();
                OnPropertyChanged(nameof(CanImport));
            }
        }
    }

    private string? _payeeHeader;
    public string? PayeeHeader {
        get => _payeeHeader;
        set {
            if (SetProperty(ref _payeeHeader, value)) {
                RefreshPreview();
                OnPropertyChanged(nameof(CanImport));
            }
        }
    }

    private string? _bankIdHeader;
    public string? BankIdHeader {
        get => _bankIdHeader;
        set {
            if (SetProperty(ref _bankIdHeader, value)) {
                RefreshPreview();
                OnPropertyChanged(nameof(CanImport));
            }
        }
    }

    public bool CanImport => !string.IsNullOrEmpty(DateHeader) &&
                            !string.IsNullOrEmpty(AmountHeader) &&
                            !string.IsNullOrEmpty(PayeeHeader) &&
                            !string.IsNullOrEmpty(BankIdHeader);

    public CsvImportMappingViewModel(string filePath, string mappingConfigPath) {
        FilePath = filePath;
        LoadPreview();
        
        AvailableHeaders.Add(null);
        foreach (var header in Headers) {
            AvailableHeaders.Add(header);
        }

        if (File.Exists(mappingConfigPath)) {
            LoadMapping(mappingConfigPath);
        } else {
            AutoDetectHeaders();
        }
    }

    private void LoadPreview() {
        using var reader = new StreamReader(FilePath);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        csv.Read();
        csv.ReadHeader();
        Headers = csv.HeaderRecord?.ToList() ?? new List<string>();

        int count = 0;
        while (csv.Read() && count < 5) {
            var row = new Dictionary<string, string>();
            foreach (var header in Headers) {
                row[header] = csv.GetField(header) ?? "";
            }
            _rawPreviewData.Add(row);
            count++;
        }
    }

    private void RefreshPreview() {
        PreviewRows.Clear();
        foreach (var rawRow in _rawPreviewData) {
            var tx = new ImportedTransactionViewModel();
            
            if (!string.IsNullOrEmpty(DateHeader) && rawRow.TryGetValue(DateHeader, out var dateStr)) {
                if (DateTime.TryParse(dateStr, CultureInfo.CurrentCulture, DateTimeStyles.None, out var d))
                    tx.Date = d;
            }

            if (!string.IsNullOrEmpty(AmountHeader) && rawRow.TryGetValue(AmountHeader, out var amountStr)) {
                if (decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var a))
                    tx.Amount = a;
            }

            if (!string.IsNullOrEmpty(PayeeHeader) && rawRow.TryGetValue(PayeeHeader, out var payee)) {
                tx.Payee = payee.Trim();
            }

            if (!string.IsNullOrEmpty(BankIdHeader) && rawRow.TryGetValue(BankIdHeader, out var bankId)) {
                tx.BankId = bankId;
            } else {
                tx.BankId = "Preview";
            }

            PreviewRows.Add(tx);
        }
    }

    private void AutoDetectHeaders() {
        foreach (var header in Headers) {
            var lower = header.ToLower();
            if (lower.Contains("date") && DateHeader == null) DateHeader = header;
            else if ((lower.Contains("amount") || lower.Contains("value")) && AmountHeader == null) AmountHeader = header;
            else if ((lower.Contains("payee") || lower.Contains("description") || lower.Contains("name")) && PayeeHeader == null) PayeeHeader = header;
            else if ((lower.Contains("id") || lower.Contains("fitid") || lower.Contains("transaction id") || lower.Contains("reference")) && BankIdHeader == null) BankIdHeader = header;
        }
        
        // If still missing, try to guess from data in the first record
        if (_rawPreviewData.Count > 0) {
            var firstRow = _rawPreviewData[0];
            foreach (var kvp in firstRow) {
                if (DateHeader == null && DateTime.TryParse(kvp.Value, out _)) DateHeader = kvp.Key;
                else if (AmountHeader == null && decimal.TryParse(kvp.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out _)) AmountHeader = kvp.Key;
            }
        }

        RefreshPreview();
    }

    public void SaveMapping(string path) {
        var mapping = new Dictionary<string, string?> {
            { "Date", DateHeader },
            { "Amount", AmountHeader },
            { "Payee", PayeeHeader },
            { "BankId", BankIdHeader }
        };
        File.WriteAllText(path, JsonSerializer.Serialize(mapping));
    }

    private void LoadMapping(string path) {
        try {
            var json = File.ReadAllText(path);
            var mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (mapping != null) {
                if (mapping.TryGetValue("Date", out var val) && Headers.Contains(val)) DateHeader = val;
                if (mapping.TryGetValue("Amount", out val) && Headers.Contains(val)) AmountHeader = val;
                if (mapping.TryGetValue("Payee", out val) && Headers.Contains(val)) PayeeHeader = val;
                if (mapping.TryGetValue("BankId", out val) && Headers.Contains(val)) BankIdHeader = val;
            }
        } catch {
            AutoDetectHeaders();
        }
    }
}
