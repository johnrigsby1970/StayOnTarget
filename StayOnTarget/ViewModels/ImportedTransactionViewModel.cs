namespace StayOnTarget.ViewModels {
    // A wrapper for the QFX imported transactions
    public class ImportedTransactionViewModel : ViewModelBase {
        public string BankId { get; set; } // The FITID from the QFX
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        public string Payee { get; set; }

        private bool _isReconciled;

        public bool IsReconciled {
            get => _isReconciled;
            set => SetProperty(ref _isReconciled, value);
        }

        private string _status = "Unmatched";

        public string Status {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public string? MatchedManualFitId { get; set; }
        public DateTime? MatchedManualTransactionDate { get; set; }
        public string? MatchedManualTransactionId { get; set; }
    }
}