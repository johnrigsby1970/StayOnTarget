using StayOnTarget.Models;

namespace StayOnTarget.Services;

public class ReconciliationService {
    private readonly BudgetService _budgetService;

    public ReconciliationService(BudgetService budgetService) {
        _budgetService = budgetService;
    }

    public IEnumerable<Transaction> GetUnreconciledTransactions(int accountId, bool isFromAccount) {
        var allTransactions = _budgetService.GetAllUnreconciledTransactions();

        if (isFromAccount) {
            // Money leaving the account (AccountId matches)
            return allTransactions
                .Where(t => t.AccountId == accountId && !t.FromAccountReconciledId.HasValue)
                .OrderBy(t => t.Date)
                .ToList();
        }
        else {
            // Money entering the account (ToAccountId matches)
            return allTransactions
                .Where(t => t.ToAccountId == accountId && !t.ToAccountReconciledId.HasValue)
                .OrderBy(t => t.Date)
                .ToList();
        }
    }

    // public decimal CalculateRunningBalance(int accountId, IEnumerable<Transaction> transactions, out DateTime? lastTransactionDate)
    // {
    //     var account = _budgetService.GetAllAccounts().FirstOrDefault(a => a.Id == accountId);
    //     if (account == null)
    //     {
    //         lastTransactionDate = null;
    //         return 0;
    //     }
    //
    //     // Start with the latest reconciliation or the account balance
    //     var latestRecon = _budgetService.GetLatestValidReconciliation(accountId);
    //     decimal balance = latestRecon?.ReconciledBalance ?? account.Balance;
    //     DateTime startDate = latestRecon?.ReconciledAsOfDate ?? account.BalanceAsOf;
    //
    //     // Apply transactions after the reconciliation date
    //     var orderedTransactions = transactions.Where(t => t.Date > startDate).OrderBy(t => t.Date).ToList();
    //
    //     foreach (var transaction in orderedTransactions)
    //     {
    //         decimal amount = Math.Abs(transaction.Amount);
    //         bool isDebitAccount = account.Type == AccountType.Mortgage ||
    //                               account.Type == AccountType.PersonalLoan ||
    //                               account.Type == AccountType.CreditCard;
    //
    //         // Money leaving the account
    //         if (transaction.AccountId == accountId)
    //         {
    //             if (isDebitAccount)
    //                 balance += amount; // Debt increases
    //             else
    //                 balance -= amount; // Asset decreases
    //         }
    //
    //         // Money entering the account
    //         if (transaction.ToAccountId == accountId)
    //         {
    //             bool isPrincipalOnly = transaction.IsPrincipalOnly;
    //             bool isRebalance = transaction.IsRebalance;
    //             bool isInterestAdjustment = transaction.IsInterestAdjustment;
    //
    //             if (account.Type == AccountType.Mortgage)
    //             {
    //                 if (isRebalance || isInterestAdjustment)
    //                     balance += amount;
    //                 else
    //                 {
    //                     decimal principal = amount;
    //                     if (!isPrincipalOnly && account.MortgageDetails != null)
    //                     {
    //                         principal = amount - account.MortgageDetails.Escrow - account.MortgageDetails.MortgageInsurance;
    //                         if (principal < 0) principal = 0;
    //                     }
    //                     balance -= principal;
    //                 }
    //             }
    //             else if (account.Type == AccountType.CreditCard)
    //             {
    //                 if (isRebalance || isInterestAdjustment)
    //                     balance += amount;
    //                 else
    //                     balance -= amount; // Payment reduces credit card balance
    //             }
    //             else if (account.Type == AccountType.PersonalLoan)
    //             {
    //                 if (isPrincipalOnly)
    //                     balance -= amount;
    //                 else if (isRebalance)
    //                     balance += amount;
    //                 else
    //                     balance += amount;
    //             }
    //             else
    //             {
    //                 balance += amount; // Asset increases
    //             }
    //         }
    //     }
    //
    //     lastTransactionDate = orderedTransactions.LastOrDefault()?.Date;
    //     return balance;
    // }

    public decimal CalculateRunningBalance(int accountId, IEnumerable<ReconciliationTransaction> transactions,
        out DateTime? lastTransactionDate, out decimal beginningBalance) {
        var account = _budgetService.GetAllAccounts().FirstOrDefault(a => a.Id == accountId);
        if (account == null) {
            lastTransactionDate = null;
            beginningBalance = 0;
            return 0;
        }

        // Start with the latest reconciliation or the account balance
        var latestRecon = _budgetService.GetLatestValidReconciliation(accountId);
        decimal balance = latestRecon?.ReconciledBalance ?? account.Balance;
        beginningBalance = balance;
        DateTime startDate = latestRecon?.ReconciledAsOfDate ?? account.BalanceAsOf;

        // Apply transactions after the reconciliation date
        var orderedTransactions = transactions.Where(t => t.Date >= startDate).OrderBy(t => t.Date).ToList();

        foreach (var transaction in orderedTransactions) {
            decimal amount = Math.Abs(transaction.Amount);
            bool isDebitAccount = account.Type == AccountType.Mortgage ||
                                  account.Type == AccountType.PersonalLoan ||
                                  account.Type == AccountType.CreditCard;

            // Money leaving the account
            if (transaction.AccountId == accountId) {
                if (isDebitAccount) {
                    balance += amount; // Debt increases
                }
                else {
                    balance -= amount; // Asset decreases
                }

                transaction.RunningBalance = balance;
            }

            // Money entering the account
            if (transaction.ToAccountId == accountId) {
                bool isPrincipalOnly = transaction.IsPrincipalOnly;
                bool isRebalance = transaction.IsRebalance;
                bool isInterestAdjustment = transaction.IsInterestAdjustment;

                if (account.Type == AccountType.Mortgage) {
                    if (isRebalance || isInterestAdjustment)
                        balance += amount;
                    else {
                        decimal principal = amount;
                        if (!isPrincipalOnly && account.MortgageDetails != null) {
                            principal = amount - account.MortgageDetails.Escrow -
                                        account.MortgageDetails.MortgageInsurance;
                            if (principal < 0) principal = 0;
                        }

                        balance -= principal;
                    }
                }
                else if (account.Type == AccountType.CreditCard) {
                    if (isRebalance || isInterestAdjustment)
                        balance += amount;
                    else
                        balance -= amount; // Payment reduces credit card balance
                }
                else if (account.Type == AccountType.PersonalLoan) {
                    if (isPrincipalOnly)
                        balance -= amount;
                    else if (isRebalance)
                        balance += amount;
                    else
                        balance += amount;
                }
                else {
                    balance += amount; // Asset increases
                }

                transaction.RunningBalance = balance;
            }
        }

        lastTransactionDate = orderedTransactions.LastOrDefault()?.Date;
        return balance;
    }

    public void ReconcileAccount(int accountId, IEnumerable<ReconciliationTransaction> reconciledTransactions, decimal finalBalance,
        DateTime asOfDate) {
        // Create the reconciliation record
        var reconciliation = new AccountReconciliation {
            AccountId = accountId,
            ReconciledAsOfDate = asOfDate,
            ReconciledBalance = finalBalance,
            ReconciledOnDate = DateTime.Today,
            IsInvalidated = false
        };

        _budgetService.UpsertAccountReconciliation(reconciliation);

        // Update transactions with the reconciliation ID
        foreach (var transaction in reconciledTransactions.Where(t => t.IsReconciled)) {
            if (transaction.AccountId == accountId) {
                transaction.FromAccountReconciledId = reconciliation.Id;
            }
            else if (transaction.ToAccountId == accountId) {
                transaction.ToAccountReconciledId = reconciliation.Id;
            }

            _budgetService.UpsertTransaction(transaction);
        }
    }
}