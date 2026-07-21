namespace StayOnTarget.ViewModels {
    // A wrapper for the QFX imported transactions
    public class ImportedTransactionViewModel : ViewModelBase {
        public string BankId { get; set; } // The FITID from the QFX
        public DateTime? Date { get; set; }
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

        private int? _bucketId;
        public int? BucketId {
            get => _bucketId;
            set => SetProperty(ref _bucketId, value);
        }

        private int? _billId;
        public int? BillId {
            get => _billId;
            set => SetProperty(ref _billId, value);
        }

        private bool _isSelected;
        public bool IsSelected {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }
}