using System.Data;
using System.Windows;
using Dapper;
using StayOnTarget.Models;

namespace StayOnTarget.Services;

public partial class BudgetService {
    public IEnumerable<Transaction> GetTransactions(DateTime periodDate) {
        using var conn = _db.GetConnection();

        var dbRows = conn.Query<dynamic>(@"
            SELECT t.*, t.TransactionDate as TransactionDate, a1.Name as AccountName, 
                   Bills.Name as BillName, Buckets.Name as BucketName 
            FROM Transactions t
            LEFT JOIN Accounts a1 ON t.AccountId = a1.Id
            LEFT JOIN Bills ON t.BillId = Bills.Id
            LEFT JOIN Buckets ON t.BucketId = Buckets.Id
            WHERE t.PeriodDate = @periodDate", new { periodDate = periodDate.ToString("yyyy-MM-dd") }).ToList();

        return MergeDbRowsToUiTransactions(dbRows);
    }

    public IEnumerable<Transaction> GetAllTransactions() {
        using var conn = _db.GetConnection();
        var dbRows = conn.Query<dynamic>("SELECT *, TransactionDate as TransactionDate FROM Transactions").ToList();
        return MergeDbRowsToUiTransactions(dbRows);
    }

    public IEnumerable<Transaction> GetAllPaycheckTransactions() {
        using var conn = _db.GetConnection();
        var dbRows = conn
            .Query<dynamic>(
                "SELECT *, TransactionDate as TransactionDate FROM Transactions WHERE PaycheckId IS NOT NULL").ToList();
        return MergeDbRowsToUiTransactions(dbRows);
    }

    public IEnumerable<Transaction> GetBillTransactions() {
        using var conn = _db.GetConnection();
        var dbRows = conn
            .Query<dynamic>("SELECT *, TransactionDate as TransactionDate FROM Transactions WHERE BillId IS NOT NULL")
            .ToList();
        return MergeDbRowsToUiTransactions(dbRows);
    }

    public IEnumerable<Transaction> GetBucketTransactions() {
        using var conn = _db.GetConnection();
        var dbRows = conn
            .Query<dynamic>("SELECT *, TransactionDate as TransactionDate FROM Transactions WHERE BucketId IS NOT NULL")
            .ToList();
        return MergeDbRowsToUiTransactions(dbRows);
    }

    public IEnumerable<Transaction> GetAllUnreconciledTransactions() {
        using var conn = _db.GetConnection();
        var dbRows = conn
            .Query<dynamic>(
                "SELECT *, TransactionDate as TransactionDate FROM Transactions WHERE ReconciliationId IS NULL")
            .ToList();
        return MergeDbRowsToUiTransactions(dbRows);
    }

    public IEnumerable<Transaction> GetAllUnreconciledTransactionsSinceLastReconciliation(int accountId) {
        using var conn = _db.GetConnection();
        var dbRows = conn.Query<dynamic>(@"
            SELECT t.*, t.TransactionDate as TransactionDate, d.MinDate 
            FROM Accounts a
            JOIN Transactions t ON t.AccountId = a.Id
            INNER JOIN (
                SELECT a.Id, IfNull(ar.MaxDate, a.BalanceAsOf) AS MinDate 
                FROM Accounts a
                LEFT JOIN (
                    SELECT AccountId, MAX(ReconciledAsOfDate) AS MaxDate
                    FROM AccountReconciliations
                    WHERE IsInvalidated IS NULL OR IsInvalidated = 1
                    GROUP BY AccountId
                ) AS ar ON ar.AccountId = a.Id 
                WHERE a.Id = @accountId
            ) As d ON a.Id = d.Id
            WHERE t.ReconciliationId IS NULL", new { accountId }).ToList();
        //Warning, this may only get one side of a two part transaction (from checking, to credit card, etc)
        return MergeDbRowsToUiTransactions(dbRows);
    }

    public async Task<bool> UpdateTransactionForReconciliation(Transaction transaction) {
        using var conn = _db.GetConnection();

        //Note, it is possible this transaction has two parts but only one part has been sent in for a change specific to reconciliation.
        if (transaction.AccountId.HasValue) {
            
            var oldRows = (await conn.QueryAsync<dynamic>(
                "SELECT AccountId, TransactionDate FROM Transactions WHERE AccountId=@AccountId AND TRANSACTIONID=@TransactionId AND (NOT ReconciliationId IS NULL AND NOT ReconciliationId=@ReconciliationId)",
                new {
                    AccountId = transaction.AccountId, TransactionId = transaction.TransactionId.ToString(),
                    ReconciliationId = transaction.FromAccountReconciledId
                })).ToList();

            if (oldRows.Any()) {
                //its already reconciled and with a different id
                MessageBoxResult result = MessageBox.Show(
                    $"This change will invalidate reconciliations for {transaction.AccountName}. You will need to redo your reconciliation request after first reverting prior reconciliation. Revert prior reconciliation?",
                    "Confirmation",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes) return false;

                // Execute historical drops
                await InvalidateReconciliationsAfterDate(transaction.AccountId.Value, transaction.TransactionDate);

                if (transaction.FromAccountReconciledId.HasValue) {
                    transaction.FromAccountReconciledId = null;
                }
            }
        }

        if (transaction.ToAccountId.HasValue) {
            var oldRows = (await conn.QueryAsync<dynamic>(
                "SELECT AccountId, TransactionDate FROM Transactions WHERE AccountId=@AccountId AND TRANSACTIONID=@TransactionId AND (NOT ReconciliationId IS NULL AND NOT ReconciliationId=@ReconciliationId)",
                new {
                    AccountId = transaction.ToAccountId, TransactionId = transaction.TransactionId.ToString(),
                    ReconciliationId = transaction.ToAccountReconciledId
                })).ToList();

            if (oldRows.Any()) {
                //its already reconciled and with a different id
                MessageBoxResult result = MessageBox.Show(
                    $"This change will invalidate reconciliations for {transaction.ToAccountName}. You will need to redo your reconciliation request after first reverting prior reconciliation. Revert prior reconciliation?",
                    "Confirmation",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes) return false;

                // Execute historical drops
                await InvalidateReconciliationsAfterDate(transaction.ToAccountId.Value, transaction.TransactionDate);

                if (transaction.ToAccountReconciledId.HasValue) {
                    transaction.ToAccountReconciledId = null;
                }
            }
        }
        
        await using var tx = conn.BeginTransaction();
        try {
            if (transaction.AccountId.HasValue) {
                await conn.ExecuteAsync(
                    @"UPDATE Transactions SET ReconciliationId=@ReconciliationId WHERE AccountId=@AccountId AND TRANSACTIONID=@TransactionId",
                    new {
                        AccountId = transaction.AccountId, ReconciliationId = transaction.FromAccountReconciledId,
                        TransactionId = transaction.TransactionId.ToString()
                    });
            }

            if (transaction.ToAccountId.HasValue) {
                await conn.ExecuteAsync(
                    @"UPDATE Transactions SET ReconciliationId=@ReconciliationId WHERE AccountId=@AccountId AND TRANSACTIONID=@TransactionId",
                    new {
                        AccountId = transaction.ToAccountId, ReconciliationId = transaction.ToAccountReconciledId,
                        TransactionId = transaction.TransactionId.ToString()
                    });
            }

            tx.Commit();
            return true;
        }
        catch {
            tx.Rollback();
            throw;
        }
        finally {
            if (conn.State == ConnectionState.Open) await conn.CloseAsync();
        }
    }

    public async Task<bool> UpsertTransactionAsync(Transaction t,
        bool showConfirmationOfImpactToExistingReconciliations = true) {
        try {
            t.Amount = Math.Abs(t
                .Amount); //should always be positive, what it does with it after this is a different issue.

            using var conn = _db.GetConnection();

// Step 1: Detect changes & handle invalidations using the TransactionId linkage group
            if (t.TransactionId != Guid.Empty) {
                // 1. Fetch BOTH rows from the database to see the whole story
                var oldRows = (await conn.QueryAsync<dynamic>(
                    "SELECT AccountId, Amount, TransactionDate FROM Transactions WHERE TransactionId = @TransactionId",
                    new { TransactionId = t.TransactionId.ToString() })).ToList();

                if (oldRows.Any()) {
                    // Since both rows share a date, we can safely read it from the first one
                    DateTime oldDate = DateTime.Parse(oldRows.First().TransactionDate);

                    // 1. Locate the outbound side (negative amount)
                    var oldFromRow = oldRows.FirstOrDefault(r => (decimal)r.Amount < 0);

                    // 2. Locate the inbound side (positive amount)
                    var oldToRow = oldRows.FirstOrDefault(r => (decimal)r.Amount >= 0);

                    // 3. Fallback for the outside world cases (where only 1 row exists)
                    if (oldRows.Count == 1) {
                        var singleRow = oldRows.First();
                        if ((decimal)singleRow.Amount >= 0) {
                            // It's a standalone inflow (Paycheck). The single row is actually the 'To' side.
                            oldToRow = singleRow;
                            oldFromRow = null;
                        }
                        else {
                            // It's a standalone outflow (Purchase). The single row is the 'From' side.
                            oldFromRow = singleRow;
                            oldToRow = null;
                        }
                    }

                    // Extract values safely
                    decimal oldAmount = oldRows.Count == 2 && oldFromRow != null
                        ? Math.Abs((decimal)oldFromRow.Amount)
                        : Math.Abs((decimal)oldRows.First().Amount);

                    int? oldFromAccountId = (int?)oldFromRow?.AccountId;
                    int? oldToAccountId = (int?)oldToRow?.AccountId;

                    // 2. Compute true structural variances
                    bool dateChanged = oldDate != t.TransactionDate;
                    bool amountChanged = oldAmount != Math.Abs(t.Amount);
                    bool fromAccountChanged = oldFromAccountId != t.AccountId;
                    bool toAccountChanged = oldToAccountId != t.ToAccountId;

                    if (dateChanged || amountChanged || fromAccountChanged || toAccountChanged) {
                        // Determine our calculation baseline timeline
                        var effectiveDate = oldDate <= t.TransactionDate ? oldDate : t.TransactionDate;

                        // Flag an impact if the account itself changed, OR if a date/amount modification threatens an active chain
                        bool fromImpacted = t.AccountId.HasValue &&
                                            (fromAccountChanged || dateChanged || amountChanged) &&
                                            WillInvalidateReconciliationsAfterDate(t.AccountId.Value, effectiveDate);

                        bool toImpacted = t.ToAccountId.HasValue &&
                                          (toAccountChanged || dateChanged || amountChanged) &&
                                          WillInvalidateReconciliationsAfterDate(t.ToAccountId.Value, effectiveDate);

                        // Also check if an account was completely dropped/replaced by looking at old settings
                        if (!fromImpacted && oldFromAccountId.HasValue && fromAccountChanged) {
                            fromImpacted =
                                WillInvalidateReconciliationsAfterDate(oldFromAccountId.Value, effectiveDate);
                        }

                        if (!toImpacted && oldToAccountId.HasValue && toAccountChanged) {
                            toImpacted = WillInvalidateReconciliationsAfterDate(oldToAccountId.Value, effectiveDate);
                        }

                        if (fromImpacted || toImpacted) {
                            MessageBoxResult result = showConfirmationOfImpactToExistingReconciliations
                                ? MessageBox.Show("This change will invalidate reconciliations. Proceed?",
                                    "Confirmation",
                                    MessageBoxButton.YesNo, MessageBoxImage.Question)
                                : MessageBoxResult.Yes;

                            if (result != MessageBoxResult.Yes) return false;

                            // Execute historical drops
                            if (fromImpacted && t.AccountId.HasValue)
                                await InvalidateReconciliationsAfterDate(t.AccountId.Value, effectiveDate);
                            if (oldFromAccountId.HasValue && fromAccountChanged)
                                await InvalidateReconciliationsAfterDate(oldFromAccountId.Value, effectiveDate);

                            if (toImpacted && t.ToAccountId.HasValue)
                                await InvalidateReconciliationsAfterDate(t.ToAccountId.Value, effectiveDate);
                            if (oldToAccountId.HasValue && toAccountChanged)
                                await InvalidateReconciliationsAfterDate(oldToAccountId.Value, effectiveDate);

                            // Clear state memory safely
                            t.FromAccountReconciledId = null;
                            t.ToAccountReconciledId = null;
                        }
                    }
                }
            }

            // Step 2: Save Logic using safe explicit database transaction blocks
            try {
                await conn.OpenAsync();
                using var tx = conn.BeginTransaction();
                try {
                    // Assign a fresh tracking Guid if this is a brand new creation event
                    if (t.TransactionId == Guid.Empty) {
                        t.TransactionId = Guid.NewGuid();
                    }
                    else {
                        // Wipe out previous records matching this event identifier to handle changes cleanly
                        await conn.ExecuteAsync("DELETE FROM Transactions WHERE TransactionId = @TransactionId",
                            new { TransactionId = t.TransactionId.ToString() }, tx);
                    }

                    // 1. Insert primary/outbound side
                    if (t.AccountId.HasValue) {
                        var outParam = GetInsertParameters(t, t.AccountId,
                            -Math.Abs(t.Amount), t.FromAccountReconciledId);
                        await conn.ExecuteAsync(GetInsertSql(), outParam, tx);
                    }

                    // 2. Insert inbound side if it's structured as a transfer
                    if (t.AccountId.HasValue && t.ToAccountId.HasValue) {
                        var inParam = GetInsertParameters(t, t.ToAccountId, Math.Abs(t.Amount),
                            t.ToAccountReconciledId);
                        await conn.ExecuteAsync(GetInsertSql(), inParam, tx);
                    }
                    else if (t.ToAccountId.HasValue) {
                        var inParam = GetInsertParameters(t, t.ToAccountId, t.Amount, t.ToAccountReconciledId);
                        await conn.ExecuteAsync(GetInsertSql(), inParam, tx);
                    }

                    tx.Commit();
                    return true;
                }
                catch {
                    tx.Rollback();
                    throw;
                }
                finally {
                    if (conn.State == ConnectionState.Open) await conn.CloseAsync();
                }
            }
            catch (Exception ex) {
                throw;
            }
        }
        catch (Exception ex) {
            throw;
        }
    }

    public void DeleteTransaction(Guid transactionId) {
        using var conn = _db.GetConnection();
        // Erases the entire logical transaction group by its UUID string representation
        conn.Execute("DELETE FROM Transactions WHERE TransactionId = @transactionId",
            new { transactionId = transactionId.ToString() });
    }


    #region Private Service Helpers (Mapping Engine)

    private IEnumerable<Transaction> MergeDbRowsToUiTransactions(IEnumerable<dynamic> dbRows) {
        var resultList = new List<Transaction>();

        // Group everything cleanly via the tracking Guid column string value
        var transactionGroups = dbRows.GroupBy(r => r.TransactionId?.ToString());

        foreach (var group in transactionGroups) {
            if (string.IsNullOrEmpty(group.Key)) continue;

            if (group.Count() == 2) {
                // Two matching transaction rows represent a paired ledger transfer event
                var outboundSide = group.FirstOrDefault(r => (double)r.Amount < 0);
                var inboundSide = group.FirstOrDefault(r => (double)r.Amount >= 0);
                if (Math.Abs(outboundSide.Amount) == 126.75) {
                    var s = "";
                }

                if (Math.Abs(inboundSide.Amount) == 126.75) {
                    var s = "";
                }

                var primaryRow = outboundSide ?? inboundSide;
                var uiTx = MapDynamicToTransaction(primaryRow, isTransferSide: true);

                if (outboundSide != null && inboundSide != null) {
                    uiTx.AccountId = (int)outboundSide.AccountId;
                    uiTx.FromAccountReconciledId = outboundSide.ReconciliationId != null
                        ? (int?)outboundSide.ReconciliationId
                        : null;
                    uiTx.AccountName = outboundSide.AccountName;
                    uiTx.ToAccountId = (int)inboundSide.AccountId;
                    uiTx.ToAccountReconciledId =
                        inboundSide.ReconciliationId != null ? (int?)inboundSide.ReconciliationId : null;
                    uiTx.ToAccountName = inboundSide.AccountName;
                    uiTx.Amount =
                        (decimal)uiTx.Amount; //Math.Abs((decimal)uiTx.Amount); // Normalize value output positive to UI
                }

                resultList.Add(uiTx);
            }
            else {
                // Single tracking record representing standard expense or deposit structures
                var standaloneRow = group.First();
                if (Math.Abs(standaloneRow.Amount) == 126.75) {
                    var s = "";
                }

                resultList.Add(MapDynamicToTransaction(standaloneRow, isTransferSide: false));
            }
        }

        //resultList.ForEach(r => r.Amount = Math.Abs(r.Amount));
        return resultList;
    }

    private Transaction MapDynamicToTransaction(dynamic row, bool isTransferSide) {
        int? dbAccountId = row.AccountId != null ? (int?)row.AccountId : null;
        int? dbReconciledId = row.ReconciliationId != null ? (int?)row.ReconciliationId : null;
        decimal amount = (decimal)row.Amount;

        int? uiAccountId = null;
        int? uiToAccountId = null;
        int? uiFromAccountReconciledId = null;
        int? uiToAccountReconciledId = null;

        if (isTransferSide) {
            // Paired Transfer: MergeDbRowsToUiTransactions will overwrite these anyway,
            // but we'll assign dbAccountId to AccountId as a safe baseline primary record.
            uiAccountId = dbAccountId;
            uiFromAccountReconciledId = dbReconciledId;
            uiToAccountId = null;
            uiToAccountReconciledId = null;
        }
        else {
            // Standalone Outside-World Transaction
            if (amount >= 0) {
                // Case 1: Inflow from Outside World (Paycheck/Deposit)
                // Money is coming INTO this account.
                uiAccountId = null;
                uiFromAccountReconciledId = null;
                uiToAccountId = dbAccountId;
                uiToAccountReconciledId = dbReconciledId;
            }
            else {
                // Case 2: Outflow to Outside World (Purchase/Bill)
                // Money is coming OUT of this account.
                uiAccountId = dbAccountId;
                uiToAccountId = dbReconciledId;
                uiToAccountId = null;
                uiToAccountReconciledId = null;
            }
        }

        return new Transaction {
            Description = row.Description,
            Memo = row.Memo,
            Amount = amount,
            TransactionDate = DateTime.Parse(row.TransactionDate),
            TransactionId = Guid.Parse(row.TransactionId.ToString()),
            AccountId = uiAccountId,
            ToAccountId = uiToAccountId,
            BillId = row.BillId != null ? (int)row.BillId : null,
            BillName = row.BillName,
            BucketId = row.BucketId != null ? (int)row.BucketId : null,
            BucketName = row.BucketName,
            PeriodDate = DateTime.Parse(row.PeriodDate),
            IsPrincipalOnly = row.IsPrincipalOnly == 1,
            FitId = Guid.Parse(row.FitId?.ToString()),
            PaycheckId = row.PaycheckId != null ? (int)row.PaycheckId : null,
            PaycheckOccurrenceDate =
                row.PaycheckOccurrenceDate != null ? DateTime.Parse(row.PaycheckOccurrenceDate) : null,
            FromAccountReconciledId = uiFromAccountReconciledId,
            ToAccountReconciledId = uiToAccountReconciledId
        };
    }

    private string GetInsertSql() {
        return
            @"INSERT INTO Transactions (TransactionId, Description, Memo, Amount, TransactionDate, AccountId, BillId, BucketId, PeriodDate, IsPrincipalOnly, FitId, PaycheckId, PaycheckOccurrenceDate, ReconciliationId)
                 VALUES (@TransactionId, @Description, @Memo, @Amount, @TransactionDate, @AccountId, @BillId, @BucketId, @PeriodDate, @IsPrincipalOnly, @FitId, @PaycheckId, @PaycheckOccurrenceDate, @ReconciliationId)";
    }

    private DynamicParameters GetInsertParameters(Transaction t, int? targetAccountId, decimal targetedAmount,
        int? targetReconciliationId) {
        var p = new DynamicParameters();
        p.Add("TransactionId", t.TransactionId.ToString());
        p.Add("Description", t.Description);
        p.Add("Memo", t.Memo);
        // Force truncation to 2 decimal places to keep SQLite REAL storage clean
        p.Add("Amount", Math.Round(targetedAmount, 2, MidpointRounding.AwayFromZero));
        p.Add("TransactionDate", t.TransactionDate.ToString("yyyy-MM-dd"));
        p.Add("AccountId", targetAccountId);
        p.Add("BillId", t.BillId);
        p.Add("BucketId", t.BucketId);
        p.Add("PeriodDate", t.PeriodDate.ToString("yyyy-MM-dd"));
        p.Add("IsPrincipalOnly", t.IsPrincipalOnly ? 1 : 0);
        p.Add("FitId", t.FitId.ToString());
        p.Add("PaycheckId", t.PaycheckId);
        p.Add("PaycheckOccurrenceDate", t.PaycheckOccurrenceDate?.ToString("yyyy-MM-dd"));
        p.Add("ReconciliationId", targetReconciliationId);
        return p;
    }

    #endregion
}