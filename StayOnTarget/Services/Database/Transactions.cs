using System.Data;
using System.Windows;
using Dapper;
using StayOnTarget.Models;

namespace StayOnTarget.Services;

public partial class BudgetService {
    public IEnumerable<Transaction> GetTransactions(DateTime periodStart, DateTime periodEnd) {
        using var conn = _db.GetConnection();

        var dbRows = conn.Query<dynamic>(@"
            SELECT t.*, t.TransactionDate as TransactionDate, a1.Name as AccountName, 
                   Bills.Name as BillName, Buckets.Name as BucketName 
            FROM Transactions t
            LEFT JOIN Accounts a1 ON t.AccountId = a1.Id
            LEFT JOIN Bills ON t.BillId = Bills.Id
            LEFT JOIN Buckets ON t.BucketId = Buckets.Id
            WHERE t.TransactionDate >= @periodStart AND t.TransactionDate < @periodEnd", 
            new { 
                periodStart = periodStart.ToString("yyyy-MM-dd"),
                periodEnd = periodEnd.ToString("yyyy-MM-dd")
            }).ToList();

        return MergeDbRowsToUiTransactions(dbRows);
    }

    public IEnumerable<Transaction> GetAllTransactions() {
        using var conn = _db.GetConnection();
        var dbRows = conn.Query<dynamic>("SELECT *, TransactionDate as TransactionDate FROM Transactions").ToList();
        return MergeDbRowsToUiTransactions(dbRows);
    }
    
    public IEnumerable<Transaction> GetAccountTransactions(int accountId) {
        using var conn = _db.GetConnection();
        var dbRows = conn.Query<dynamic>("SELECT *, TransactionDate as TransactionDate FROM Transactions WHERE AccountId=@accountId", new { accountId }).ToList();
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

    public List<string> GetAlreadyImportedBankIds(int accountId) {
        using var conn = _db.GetConnection();
        var dbRows = conn
            .Query<string>(
                "SELECT FitId FROM Transactions WHERE AccountId=@accountId", new { accountId })
            .ToList();
        return dbRows;
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
        try {
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
                    await InvalidateReconciliationsAfterDate(transaction.ToAccountId.Value,
                        transaction.TransactionDate);

                    if (transaction.ToAccountReconciledId.HasValue) {
                        transaction.ToAccountReconciledId = null;
                    }
                }
            }

            await conn.OpenAsync();
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
        catch (Exception ex) {
            throw;
        }
        finally { }
    }

    public async Task<bool> UpdateTransactionForBankFitId(int accountId, string transactionId, string fitId,
        string bankFitId, DateTime transactionDate, string description) {
        using var conn = _db.GetConnection();

        await conn.OpenAsync();
        await using var tx = conn.BeginTransaction();
        try {
            //Description=@description, 
            await conn.ExecuteAsync(
                @"UPDATE Transactions SET FitId=@bankFitId, TransactionDate=@transactionDate WHERE AccountId=@accountId AND TRANSACTIONID=@transactionId AND FITID=@fitId",
                new {
                    bankFitId,
                    transactionDate,
                    accountId,
                    transactionId,
                    fitId
                });

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
    
    public decimal GetAccountBalanceAsOf(int accountId, DateTime asOfDate) {
        var account = GetAllAccountsAsOf(asOfDate).FirstOrDefault(a => a.Id == accountId);
        return account?.Balance ?? 0;
    }

    public decimal CalculateAccruedInterest(DateTime paymentDate, decimal apr, int statementDay, int accountId) {
        // 1. Determine target month/year for the prior statement
        int year = paymentDate.Year;
        int month = paymentDate.Month;

        // If payment date is before the statement day in the current month, look back to previous month
        if (paymentDate.Day < statementDay) {
            var prevMonthDate = paymentDate.AddMonths(-1);
            year = prevMonthDate.Year;
            month = prevMonthDate.Month;
        }

        // 2. Safe statement date handling (e.g., handles Feb 28/29 for a 31st statement day)
        int safeDay = Math.Min(statementDay, DateTime.DaysInMonth(year, month));
        DateTime targetStatementDate = new DateTime(year, month, safeDay).Date.AddDays(1).AddTicks(-1); // 23:59:59.999

        // 3. Get ledger balance on that date
        decimal priorBalance = GetAccountBalanceAsOf(accountId, targetStatementDate);

        // 4. Calculate monthly interest (assuming negative liability balance)
        decimal monthlyRate = (apr / 100m) / 12m;
        decimal interestAmount = Math.Round(priorBalance * monthlyRate, 2, MidpointRounding.AwayFromZero);

        return interestAmount; // Will return negative value if priorBalance is negative
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

                            // Interest Calculation for Mortgage
                    if (t.ToAccountId.HasValue && !t.IsPrincipalOnly) {
                        var toAccount = GetAllAccountsAsOf(t.TransactionDate).FirstOrDefault(a => a.Id == t.ToAccountId.Value);
                        if (toAccount != null && toAccount.Type == AccountType.Mortgage && toAccount.MortgageDetails != null) {
                            int statementDay = toAccount.MortgageDetails.StatementDay;

                            // Re-calculate targetStatementDate to use in search
                            int year = t.TransactionDate.Year;
                            int month = t.TransactionDate.Month;
                            if (t.TransactionDate.Day < statementDay) {
                                var prevMonthDate = t.TransactionDate.AddMonths(-1);
                                year = prevMonthDate.Year;
                                month = prevMonthDate.Month;
                            }
                            int safeDay = Math.Min(statementDay, DateTime.DaysInMonth(year, month));
                            DateTime targetStatementDate = new DateTime(year, month, safeDay);

                            // The next statement date defines the end of the period where we'd expect the interest transaction to be recorded
                            var nextMonthDate = targetStatementDate.AddMonths(1);
                            int nextSafeDay = Math.Min(statementDay, DateTime.DaysInMonth(nextMonthDate.Year, nextMonthDate.Month));
                            DateTime nextStatementDate = new DateTime(nextMonthDate.Year, nextMonthDate.Month, nextSafeDay);

                            var memoToSearch = $"as of statement date {targetStatementDate:M/d/yyyy}";

                            // Before line 403, determine if there is already an interestonly transaction that should be applicable to the statement date.
                            // One could have been generated by another transaction, having a different transactionid.
                            // We look for any interest-only transaction between targetStatementDate and nextStatementDate
                            var existingInterestOnStatement = await conn.QueryFirstOrDefaultAsync<dynamic>(
                                @"SELECT TransactionId, ReconciliationId FROM Transactions 
                                  WHERE AccountId = @accountId 
                                  AND IsInterestOnly = 1 
                                  AND TransactionDate > @start 
                                  AND TransactionDate <= @end",
                                new { 
                                    accountId = t.ToAccountId.Value, 
                                    start = targetStatementDate.ToString("yyyy-MM-dd"),
                                    end = nextStatementDate.ToString("yyyy-MM-dd")
                                }, tx);

                            if (existingInterestOnStatement != null && existingInterestOnStatement.TransactionId.ToString() != t.TransactionId.ToString()) {
                                // Already exists for another transaction. We don't want to create two.
                                // Skip interest calculation/insertion for this transaction.
                            }
                            else {
                                var interestAmount = CalculateAccruedInterest(t.TransactionDate, toAccount.MortgageDetails.InterestRate, statementDay, t.ToAccountId.Value);

                                // Find existing interest transaction if any (specifically for THIS transaction group)
                                var existingInterest = await conn.QueryFirstOrDefaultAsync<dynamic>(
                                    "SELECT ReconciliationId, TransactionDate FROM Transactions WHERE TransactionId = @TransactionId AND IsInterestOnly = 1",
                                    new { TransactionId = t.TransactionId.ToString() }, tx);

                                int? interestReconciledId = null;
                                if (existingInterest != null) {
                                    interestReconciledId = (int?)existingInterest.ReconciliationId;
                                    if (interestReconciledId.HasValue) {
                                        await InvalidateReconciliationsAfterDate(t.ToAccountId.Value, t.TransactionDate, tx);
                                        interestReconciledId = null;
                                    }
                                    // Delete existing interest so we can re-insert it
                                    await conn.ExecuteAsync("DELETE FROM Transactions WHERE TransactionId = @TransactionId AND IsInterestOnly = 1",
                                        new { TransactionId = t.TransactionId.ToString() }, tx);
                                }

                                // Create interest transaction (outflow from ToAccountId)
                                var interestTx = new Transaction {
                                    TransactionId = t.TransactionId,
                                    Description = "Interest",
                                    Memo = memoToSearch,
                                    Amount = Math.Abs(interestAmount), // GetInsertParameters handles sign
                                    TransactionDate = t.TransactionDate,
                                    PeriodDate = t.PeriodDate,
                                    IsInterestOnly = true
                                };

                                var intParam = GetInsertParameters(interestTx, t.ToAccountId.Value, -Math.Abs(interestAmount), interestReconciledId);
                                await conn.ExecuteAsync(GetInsertSql(), intParam, tx);
                            }
                        }
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

        // Filter out interest-only transactions unless they are the "from" part (negative amount)
        // and have no other parts to merge with.
        // Actually, the requirement says: "ignore the IsInterestOnly account for that transaction merger unless it is the from part of a transaction and then there will be nothing to merge it with."
        // This implies we should group and then decide.

        // Group everything cleanly via the tracking Guid column string value
        var transactionGroups = dbRows.GroupBy(r => r.TransactionId?.ToString());

        foreach (var group in transactionGroups) {
            if (string.IsNullOrEmpty(group.Key)) continue;

            var list = group.ToList();
            
            // Check if there's an interest-only transaction in the group
            bool hasInterestOnly = list.Any(r => r.IsInterestOnly == 1);

            if (list.Count() == 2) {
                if (hasInterestOnly) {
                    // If it's interest-only and has 2 rows, we need to decide what to do.
                    // The requirement says "ignore ... unless it is the from part ... and then there will be nothing to merge it with".
                    // This suggests interest-only transactions might sometimes have 2 rows but shouldn't be merged?
                    // "unless it is the from part of a transaction and then there will be nothing to merge it with"
                    // If there are 2 rows, they ARE merged.
                    // If one is IsInterestOnly, should it be ignored? 
                    // "the IsInterestOnly account should be ignored for that transaction merger"
                    
                    // Let's re-read: "the IsInterestOnly account should be ignored for that transaction merger unless it is the from part of a transaction and then there will be nothing to merge it with."
                    // If we have a normal payment (2 rows: checking -> mortgage) and an interest transaction (1 or 2 rows?).
                    // Interest is taken from ToAccountId (mortgage). So it's an outflow from Mortgage.
                    // Interest transaction: From: Mortgage, To: null (outside world). 1 row.
                    
                    // If it's a merger of a normal payment, we should NOT include any interest-only rows in that merger.
                    // But they have the same TransactionId. So they WILL be in the same group.
                    
                    var normalRows = list.Where(r => r.IsInterestOnly != 1).ToList();
                    var interestRows = list.Where(r => r.IsInterestOnly == 1).ToList();

                    if (normalRows.Count == 2) {
                        // Merge normal rows
                        resultList.Add(MergeRows(normalRows));
                    } else if (normalRows.Count == 1) {
                        resultList.Add(MapDynamicToTransaction(normalRows[0], false));
                    }

                    foreach (var ir in interestRows) {
                        // "unless it is the from part of a transaction and then there will be nothing to merge it with"
                        // Interest taken from ToAccountId means it's an outflow (amount < 0).
                        if ((double)ir.Amount < 0) {
                            resultList.Add(MapDynamicToTransaction(ir, false));
                        }
                    }
                    continue;
                }

                // Two matching transaction rows represent a paired ledger transfer event
                resultList.Add(MergeRows(list));
            }
            else {
                // Single tracking record representing standard expense or deposit structures
                var standaloneRow = list.First();
                
                // For interest only, check if it's the from part
                if (standaloneRow.IsInterestOnly == 1) {
                    if ((double)standaloneRow.Amount < 0) {
                        resultList.Add(MapDynamicToTransaction(standaloneRow, isTransferSide: false));
                    }
                } else {
                    resultList.Add(MapDynamicToTransaction(standaloneRow, isTransferSide: false));
                }
            }
        }

        //resultList.ForEach(r => r.Amount = Math.Abs(r.Amount));
        return resultList;
    }

    private Transaction MergeRows(IEnumerable<dynamic> group) {
        var outboundSide = group.FirstOrDefault(r => (double)r.Amount < 0);
        var inboundSide = group.FirstOrDefault(r => (double)r.Amount >= 0);

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
            uiTx.Amount = (decimal)uiTx.Amount;
        }

        return uiTx;
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
            AccountName= uiAccountId==null ? "" : row.AccountName,
            ToAccountId = uiToAccountId,
            ToAccountName= uiToAccountId==null ? "" : string.IsNullOrEmpty(row.ToAccountName) ? row.AccountName : row.ToAccountName,
            BillId = row.BillId != null ? (int)row.BillId : null,
            BillName = row.BillName,
            BucketId = row.BucketId != null ? (int)row.BucketId : null,
            BucketName = row.BucketName,
            IsPrincipalOnly = row.IsPrincipalOnly == 1,
            IsInterestOnly = row.IsInterestOnly == 1,
            FitId = row.FitId?.ToString(),
            PaycheckId = row.PaycheckId != null ? (int)row.PaycheckId : null,
            PaycheckOccurrenceDate =
                row.PaycheckOccurrenceDate != null ? DateTime.Parse(row.PaycheckOccurrenceDate) : null,
            FromAccountReconciledId = uiFromAccountReconciledId,
            ToAccountReconciledId = uiToAccountReconciledId
        };
    }

    private string GetInsertSql() {
        return
            @"INSERT INTO Transactions (TransactionId, Description, Memo, Amount, TransactionDate, AccountId, BillId, BucketId, PeriodDate, IsPrincipalOnly, IsInterestOnly, FitId, PaycheckId, PaycheckOccurrenceDate, ReconciliationId)
                 VALUES (@TransactionId, @Description, @Memo, @Amount, @TransactionDate, @AccountId, @BillId, @BucketId, @PeriodDate, @IsPrincipalOnly, @IsInterestOnly, @FitId, @PaycheckId, @PaycheckOccurrenceDate, @ReconciliationId)";
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
        p.Add("PeriodDate", "1900-01-01");
        p.Add("IsPrincipalOnly", t.IsPrincipalOnly ? 1 : 0);
        p.Add("IsInterestOnly", t.IsInterestOnly ? 1 : 0);
        p.Add("FitId", t.FitId.ToString());
        p.Add("PaycheckId", t.PaycheckId);
        p.Add("PaycheckOccurrenceDate", t.PaycheckOccurrenceDate?.ToString("yyyy-MM-dd"));
        p.Add("ReconciliationId", targetReconciliationId);
        return p;
    }

    #endregion
}