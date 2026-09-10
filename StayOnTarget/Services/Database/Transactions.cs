using System.Data;
using System.Data.Common;
using Dapper;
using Microsoft.Data.Sqlite;
using StayOnTarget.Helpers;
using StayOnTarget.Models;
using Serilog;
using StayOnTarget.ViewModels;

namespace StayOnTarget.Services;

public partial class BudgetService {
    public async Task ReconcileHistoricalTransactionsAsync(int accountId, List<int> recordIds) {
        if (!recordIds.Any()) return;

        await using var conn = _db.GetConnection();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try {
            // Find the latest valid reconciliation record for this account to attach the orphan records to
            var latestReconciliation = await GetLatestValidReconciliationAsync(accountId);
            int? targetReconciliationId = latestReconciliation?.Id;

            // Fallback: If no account reconciliation exists yet, locate the Opening Balance transaction's ReconciliationId
            if (!targetReconciliationId.HasValue) {
                targetReconciliationId = await conn.ExecuteScalarAsync<int?>(@"
                SELECT ReconciliationId 
                FROM Transactions 
                WHERE AccountId = @accountId AND Description = @openingDesc AND ReconciliationId IS NOT NULL 
                LIMIT 1",
                    new { accountId, openingDesc = Constants.OpeningBalance }, tx);
            }

            // Batch update selected transactions
            const string sql = @"
            UPDATE Transactions 
            SET ReconciliationId = @reconciliationId, 
                IsCleared = 1 
            WHERE AccountId = @accountId 
              AND Id IN @recordIds";

            await conn.ExecuteAsync(sql, new {
                reconciliationId = targetReconciliationId,
                accountId,
                recordIds
            }, tx);

            await tx.CommitAsync();
        }
        catch (Exception ex) {
            await tx.RollbackAsync();
            Log.Error(ex, "Error reconciling historical transactions for account {AccountId}.", accountId);
            throw;
        }
    }

    public async Task UnreconcileAndResetTransactionAsync(int transactionRecordId) {
        await using var conn = _db.GetConnection();
        await conn.OpenAsync();
        await using var tx = conn.BeginTransaction();

        try {
            // 1. Generate a new random GUID to free up the old FitId for future bank imports
            var newFitId = Guid.NewGuid().ToString();

            await conn.ExecuteAsync(@"
                UPDATE Transactions 
                SET ReconciliationId = NULL, 
                    IsCleared = 0, 
                    FitId = @NewFitId
                WHERE Id = @RecordId",
                new { NewFitId = newFitId, RecordId = transactionRecordId }, tx);


            await tx.CommitAsync();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error resetting and unreconciling transaction record ID {RecordId}.", transactionRecordId);
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<IEnumerable<Transaction>> GetTransactionsAsync(DateTime periodStart, DateTime periodEnd) {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();

            var dbRows = (await conn.QueryAsync<dynamic>(@"
                SELECT t.*, a1.Name as AccountName, 
                       Bills.Name as BillName, Buckets.Name as BucketName 
                , SubCategories.Name as SubCategoryName 
                FROM Transactions t
                LEFT JOIN Accounts a1 ON t.AccountId = a1.Id
                LEFT JOIN Bills ON t.BillId = Bills.Id
                LEFT JOIN Buckets ON t.BucketId = Buckets.Id
                LEFT JOIN SubCategories ON t.SubCategoryId = SubCategories.Id
                WHERE t.TransactionDate >= @periodStart AND t.TransactionDate <= @periodEnd",
                new {
                    periodStart = periodStart.ToString("yyyy-MM-dd"),
                    periodEnd = periodEnd.ToString("yyyy-MM-dd")
                })).ToList();

            return MergeDbRowsToUiTransactions(dbRows);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error getting transactions between {PeriodStart} and {PeriodEnd}[cite: 25].", periodStart,
                periodEnd);
            return Enumerable.Empty<Transaction>();
        }
    }

    public async Task<IEnumerable<(int billId, decimal amount)>> GetBillsPaidInRange(DateTime periodStart,
        DateTime periodEnd) {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();

            var dbRows = (await conn.QueryAsync<dynamic>(@"
                SELECT BillId, Amount
                FROM Transactions t
                WHERE Not BillId IS NULL AND NOT AccountId IS NULL AND t.TransactionDate >= @periodStart AND t.TransactionDate <= @periodEnd",
                new {
                    periodStart = periodStart.ToString("yyyy-MM-dd"),
                    periodEnd = periodEnd.ToString("yyyy-MM-dd")
                })).ToList();

            return dbRows.Select(x => ((int)x.BillId, (decimal)x.Amount));
        }
        catch (Exception ex) {
            Log.Error(ex, "Error getting bills paid in range between {PeriodStart} and {PeriodEnd}[cite: 25].",
                periodStart, periodEnd);
            return Enumerable.Empty<(int, decimal)>();
        }
    }

    public async Task<IEnumerable<Transaction>> GetAllTransactionsAsync(DateTime? periodStart = null,
        DateTime? periodEnd = null) {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();

            const string sql = @"
            SELECT t.*, 
                   a1.Name as AccountName, 
                   Bills.Name as BillName, 
                   Buckets.Name as BucketName, 
                   SubCategories.Name as SubCategoryName 
            FROM Transactions t
            LEFT JOIN Accounts a1 ON t.AccountId = a1.Id
            LEFT JOIN Bills ON t.BillId = Bills.Id
            LEFT JOIN Buckets ON t.BucketId = Buckets.Id
            LEFT JOIN SubCategories ON t.SubCategoryId = SubCategories.Id
            WHERE (@periodStart IS NULL OR t.TransactionDate >= @periodStart)
              AND (@periodEnd IS NULL OR t.TransactionDate <= @periodEnd)
            ORDER BY t.TransactionDate DESC";

            var parameters = new {
                periodStart = periodStart?.ToString("yyyy-MM-dd"),
                periodEnd = periodEnd?.ToString("yyyy-MM-dd")
            };

            var dbRows = (await conn.QueryAsync<dynamic>(sql, parameters)).ToList();
            return MergeDbRowsToUiTransactions(dbRows);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error getting all transactions[cite: 25].");
            return Enumerable.Empty<Transaction>();
        }
    }

    public async Task<IEnumerable<Transaction>> GetRawTransactionsAsync() {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();

            var dbRows = (await conn.QueryAsync<dynamic>(@"
                SELECT t.*, a.Name as AccountName, b.Name as BucketName
                , SubCategories.Name as SubCategoryName 
                FROM Transactions t
                LEFT JOIN Accounts a ON t.AccountId = a.Id
                LEFT JOIN Buckets b ON t.BucketId = b.Id
                LEFT JOIN SubCategories ON t.SubCategoryId = SubCategories.Id")).ToList();

            return dbRows.Select(row => (Transaction)MapDynamicToTransaction(row, isTransferSide: false)).ToList();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error getting raw transactions[cite: 25].");
            return Enumerable.Empty<Transaction>();
        }
    }

    public async Task<IEnumerable<Transaction>> GetAccountTransactionsAsync(int accountId) {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();

            var dbRows = (await conn.QueryAsync<dynamic>(@"
                SELECT t.*, a1.Name as AccountName, 
                       Bills.Name as BillName, Buckets.Name as BucketName 
                , SubCategories.Name as SubCategoryName 
                FROM Transactions t
                LEFT JOIN Accounts a1 ON t.AccountId = a1.Id
                LEFT JOIN Bills ON t.BillId = Bills.Id
                LEFT JOIN Buckets ON t.BucketId = Buckets.Id
                LEFT JOIN SubCategories ON t.SubCategoryId = SubCategories.Id  WHERE t.AccountId=@accountId",
                new { accountId })).ToList();

            return MergeDbRowsToUiTransactions(dbRows);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error getting account transactions for account ID {AccountId}[cite: 25].", accountId);
            return Enumerable.Empty<Transaction>();
        }
    }

    public async Task<IEnumerable<Ledger>> GetAccountTransactionsAsDynamicAsync(int accountId) {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();

            var dbRows = (await conn
                .QueryAsync<Ledger>("SELECT * FROM Transactions WHERE AccountId=@accountId",
                    new { accountId })).ToList();
            return dbRows;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error getting account transactions as dynamic for account ID {AccountId}[cite: 25].",
                accountId);
            return Enumerable.Empty<Ledger>();
        }
    }

    public async Task<IEnumerable<Ledger>> GetUnreconciledAccountLedgerAsync(int accountId) {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();

            var dbRows = (await conn
                .QueryAsync<Ledger>(
                    "SELECT * FROM Transactions WHERE AccountId=@accountId AND ReconciliationId IS NULL",
                    new { accountId })).ToList();
            return dbRows;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error getting unreconciled account ledger for account ID {AccountId}[cite: 25].", accountId);
            return Enumerable.Empty<Ledger>();
        }
    }

    public async Task<IEnumerable<Transaction>> GetAllPaycheckTransactionsAsync() {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();

            var dbRows = (await conn.QueryAsync<dynamic>(@"
                SELECT t.*, a1.Name as AccountName, 
                       Bills.Name as BillName, Buckets.Name as BucketName 
                , SubCategories.Name as SubCategoryName 
                FROM Transactions t
                LEFT JOIN Accounts a1 ON t.AccountId = a1.Id
                LEFT JOIN Bills ON t.BillId = Bills.Id
                LEFT JOIN Buckets ON t.BucketId = Buckets.Id
                LEFT JOIN SubCategories ON t.SubCategoryId = SubCategories.Id WHERE t.PaycheckId IS NOT NULL"))
                .ToList();

            return MergeDbRowsToUiTransactions(dbRows);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error getting all paycheck transactions[cite: 25].");
            return Enumerable.Empty<Transaction>();
        }
    }

    public async Task<IEnumerable<Transaction>> GetBillTransactionsAsync() {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();

            var dbRows = (await conn.QueryAsync<dynamic>(@"
                SELECT t.*, a1.Name as AccountName, 
                       Bills.Name as BillName, Buckets.Name as BucketName 
                , SubCategories.Name as SubCategoryName 
                FROM Transactions t
                LEFT JOIN Accounts a1 ON t.AccountId = a1.Id
                LEFT JOIN Bills ON t.BillId = Bills.Id
                LEFT JOIN Buckets ON t.BucketId = Buckets.Id
                LEFT JOIN SubCategories ON t.SubCategoryId = SubCategories.Id WHERE t.BillId IS NOT NULL")).ToList();
            return MergeDbRowsToUiTransactions(dbRows);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error getting bill transactions[cite: 25].");
            return Enumerable.Empty<Transaction>();
        }
    }

    public async Task<IEnumerable<Transaction>> GetBucketTransactionsAsync() {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();

            var dbRows = (await conn.QueryAsync<dynamic>(@"
                SELECT t.*, a1.Name as AccountName, 
                       Bills.Name as BillName, Buckets.Name as BucketName 
                , SubCategories.Name as SubCategoryName 
                FROM Transactions t
                LEFT JOIN Accounts a1 ON t.AccountId = a1.Id
                LEFT JOIN Bills ON t.BillId = Bills.Id
                LEFT JOIN Buckets ON t.BucketId = Buckets.Id
                LEFT JOIN SubCategories ON t.SubCategoryId = SubCategories.Id WHERE t.BucketId IS NOT NULL")).ToList();

            return MergeDbRowsToUiTransactions(dbRows);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error getting bucket transactions[cite: 25].");
            return Enumerable.Empty<Transaction>();
        }
    }

    public async Task<List<string>> GetAlreadyImportedBankIdsAsync(int accountId) {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();

            var dbRows = (await conn
                    .QueryAsync<string>(
                        "SELECT FitId FROM Transactions WHERE AccountId=@accountId", new { accountId })
                ).ToList();
            return dbRows;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error getting already imported bank IDs for account ID {AccountId}[cite: 25].", accountId);
            return new List<string>();
        }
    }

    public async Task<List<string>> GetAlreadyImportedBankIdsAsync(int accountId, List<string> bankIds) {
        if (bankIds == null || !bankIds.Any()) {
            return new List<string>();
        }

        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();

            const string sql = @"
            SELECT DISTINCT FitId 
            FROM Transactions 
            WHERE AccountId = @accountId 
              AND IsCleared = 1 
              AND FitId IN @bankIds";

            var results = await conn.QueryAsync<string>(sql, new { accountId, bankIds });
            return results.ToList();
        }
        catch (Exception ex) {
            Log.Error(ex,
                "Error getting already imported bank IDs for account ID {AccountId} with specific bank IDs[cite: 25].",
                accountId);
            return new List<string>();
        }
    }

    public async Task<IEnumerable<Transaction>> GetAllUnreconciledTransactionsAsync(int? accountId = null) {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();

            string sql;
            object param;

            if (accountId.HasValue) {
                sql = @"
                SELECT t.*, 
                       a1.Name as AccountName, 
                       Bills.Name as BillName, 
                       Buckets.Name as BucketName, 
                       SubCategories.Name as SubCategoryName 
                FROM Transactions t
                LEFT JOIN Accounts a1 ON t.AccountId = a1.Id
                LEFT JOIN Bills ON t.BillId = Bills.Id
                LEFT JOIN Buckets ON t.BucketId = Buckets.Id
                LEFT JOIN SubCategories ON t.SubCategoryId = SubCategories.Id 
                WHERE t.TransactionId IN (
                    SELECT DISTINCT TransactionId 
                    FROM Transactions 
                    WHERE ReconciliationId IS NULL 
                      AND AccountId = @accountId
                )";
                param = new { accountId };
            }
            else {
                sql = @"
                SELECT t.*, 
                       a1.Name as AccountName, 
                       Bills.Name as BillName, 
                       Buckets.Name as BucketName, 
                       SubCategories.Name as SubCategoryName 
                FROM Transactions t
                LEFT JOIN Accounts a1 ON t.AccountId = a1.Id
                LEFT JOIN Bills ON t.BillId = Bills.Id
                LEFT JOIN Buckets ON t.BucketId = Buckets.Id
                LEFT JOIN SubCategories ON t.SubCategoryId = SubCategories.Id 
                WHERE t.TransactionId IN (
                    SELECT DISTINCT TransactionId 
                    FROM Transactions 
                    WHERE ReconciliationId IS NULL
                )";
                param = new { };
            }

            var dbRows = (await conn.QueryAsync<dynamic>(sql, param)).ToList();

            return MergeDbRowsToUiTransactions(dbRows);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error getting all unreconciled transactions[cite: 25].");
            return Enumerable.Empty<Transaction>();
        }
    }

    public async Task<IEnumerable<Transaction>> GetAllUnclearedTransactionsAsync(int? accountId = null) {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();

            string sql;
            object param;

            if (accountId.HasValue) {
                sql = @"
                SELECT t.*, 
                       a1.Name as AccountName, 
                       Bills.Name as BillName, 
                       Buckets.Name as BucketName, 
                       SubCategories.Name as SubCategoryName 
                FROM Transactions t
                LEFT JOIN Accounts a1 ON t.AccountId = a1.Id
                LEFT JOIN Bills ON t.BillId = Bills.Id
                LEFT JOIN Buckets ON t.BucketId = Buckets.Id
                LEFT JOIN SubCategories ON t.SubCategoryId = SubCategories.Id 
                WHERE t.TransactionId IN (
                    SELECT DISTINCT TransactionId 
                    FROM Transactions 
                    WHERE IsCleared != 1 
                      AND AccountId = @accountId
                )";
                param = new { accountId };
            }
            else {
                sql = @"
                SELECT t.*, 
                       a1.Name as AccountName, 
                       Bills.Name as BillName, 
                       Buckets.Name as BucketName, 
                       SubCategories.Name as SubCategoryName 
                FROM Transactions t
                LEFT JOIN Accounts a1 ON t.AccountId = a1.Id
                LEFT JOIN Bills ON t.BillId = Bills.Id
                LEFT JOIN Buckets ON t.BucketId = Buckets.Id
                LEFT JOIN SubCategories ON t.SubCategoryId = SubCategories.Id 
                WHERE t.TransactionId IN (
                    SELECT DISTINCT TransactionId 
                    FROM Transactions 
                    WHERE IsCleared != 1
                )";
                param = new { };
            }

            var dbRows = (await conn.QueryAsync<dynamic>(sql, param)).ToList();

            return MergeDbRowsToUiTransactions(dbRows);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error getting all uncleared transactions[cite: 25].");
            return Enumerable.Empty<Transaction>();
        }
    }

    public async Task<IEnumerable<Transaction>> GetAllUnreconciledTransactionsSinceLastReconciliationAsync(
        int accountId) {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();

            const string sql = @"
        WITH TargetTransactionIds AS (
            SELECT DISTINCT TransactionId
            FROM Transactions
            WHERE AccountId = @accountId
              AND ReconciliationId IS NULL
        )
        SELECT t.*, 
               a1.Name as AccountName, 
               Bills.Name as BillName, 
               Buckets.Name as BucketName, 
               SubCategories.Name as SubCategoryName
        FROM Transactions t
        INNER JOIN TargetTransactionIds target ON t.TransactionId = target.TransactionId
        LEFT JOIN Accounts a1 ON t.AccountId = a1.Id
        LEFT JOIN Bills ON t.BillId = Bills.Id
        LEFT JOIN Buckets ON t.BucketId = Buckets.Id
        LEFT JOIN SubCategories ON t.SubCategoryId = SubCategories.Id;";

            // const string sql = @"
            // WITH AccountMinDate AS (
            //     SELECT a.Id AS AccountId, IFNULL(ar.MaxDate, a.BalanceAsOf) AS MinDate 
            //     FROM Accounts a
            //     LEFT JOIN (
            //         SELECT AccountId, MAX(ReconciledAsOfDate) AS MaxDate
            //         FROM AccountReconciliations
            //         WHERE IsInvalidated IS NULL OR IsInvalidated = 0
            //         GROUP BY AccountId
            //     ) AS ar ON ar.AccountId = a.Id 
            //     WHERE a.Id = @accountId
            // ),
            // TargetTransactionIds AS (
            //     SELECT DISTINCT t.TransactionId
            //     FROM Transactions t
            //     INNER JOIN AccountMinDate amd ON t.AccountId = amd.AccountId
            //     WHERE t.ReconciliationId IS NULL
            //       AND date(t.TransactionDate) >= date(amd.MinDate)
            // )
            // SELECT t.*, 
            //        a1.Name as AccountName, 
            //        Bills.Name as BillName, 
            //        Buckets.Name as BucketName, 
            //        SubCategories.Name as SubCategoryName
            // FROM Transactions t
            // INNER JOIN TargetTransactionIds target ON t.TransactionId = target.TransactionId
            // LEFT JOIN Accounts a1 ON t.AccountId = a1.Id
            // LEFT JOIN Bills ON t.BillId = Bills.Id
            // LEFT JOIN Buckets ON t.BucketId = Buckets.Id
            // LEFT JOIN SubCategories ON t.SubCategoryId = SubCategories.Id;";

            var dbRows = (await conn.QueryAsync<dynamic>(sql, new { accountId })).ToList();

            return MergeDbRowsToUiTransactions(dbRows);
        }
        catch (Exception ex) {
            Log.Error(ex,
                "Error getting unreconciled transactions since last reconciliation for account ID {AccountId}[cite: 25].",
                accountId);
            return Enumerable.Empty<Transaction>();
        }
    }

    public async Task ProcessMultiMatchSplitAsync(
        int accountId,
        string manualTransactionId,
        List<ImportedTransactionViewModel> bankItems) {
        await using var conn = _db.GetConnection();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try {
            // 1. Guard Check: Ensure this is a simple single-legged transaction before splitting
            int legCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM Transactions WHERE TransactionId = @manualTransactionId",
                new { manualTransactionId }, tx);

            if (legCount > 1) {
                throw new InvalidOperationException(
                    $"Transaction {manualTransactionId} has multiple legs ({legCount}). Ex. It involves two accounts. Splits are currently only supported on single-legged transactions.");
            }

            // Fetch original manual record from DB
            var original = (await conn.QueryAsync<Ledger>(
                "SELECT * FROM Transactions WHERE TransactionId = @manualTransactionId AND AccountId = @accountId",
                new { manualTransactionId, accountId }, tx)).FirstOrDefault();

            if (original == null) return;

            // Set to collect all distinct bucket IDs affected during this split operation
            var bucketsToRecalculate = new HashSet<int>();
            if (original.BucketId.HasValue) {
                bucketsToRecalculate.Add(original.BucketId.Value);
            }

            decimal totalBankAmount = bankItems.Sum(x => Math.Abs(x.Amount));
            decimal originalAmount = Math.Abs(original.Amount);
            decimal remainderAmount = originalAmount - totalBankAmount;

            // 2. Process Bank Charges (Update 1st, Insert subsequent N-1)
            for (int i = 0; i < bankItems.Count; i++) {
                var item = bankItems[i];
                int? itemBucketId = item.BucketId ?? original.BucketId;

                if (itemBucketId.HasValue) {
                    bucketsToRecalculate.Add(itemBucketId.Value);
                }

                if (i == 0) {
                    // Update original record with 1st bank charge amount & Bank FITID
                    original.Amount = Math.Abs(item.Amount);
                    original.FitId = item.BankId ?? string.Empty;
                    original.IsCleared = true;
                    original.BucketId = itemBucketId;
                    original.BillId = item.BillId ?? original.BillId;
                    original.SubCategoryId = item.SubCategoryId ?? original.SubCategoryId;

                    var updateParams = GetUpdateParameters(
                        original,
                        accountId,
                        -Math.Abs(item.Amount),
                        original.ReconciliationId,
                        original.Id,
                        targetIsCleared: true,
                        targetFitId: item.BankId ?? string.Empty);

                    await conn.ExecuteAsync(GetUpdateSql(), updateParams, tx);
                }
                else {
                    // Spawn new cleared record with its own unique TransactionId
                    var child = new Ledger {
                        TransactionId = Guid.NewGuid(),
                        Description = original.Description,
                        Memo = original.Memo,
                        Amount = Math.Abs(item.Amount),
                        TransactionDate = original.TransactionDate,
                        AccountId = accountId,
                        BillId = item.BillId ?? original.BillId,
                        BucketId = itemBucketId,
                        SubCategoryId = item.SubCategoryId ?? original.SubCategoryId,
                        IsCleared = true,
                        FitId = item.BankId ?? string.Empty
                    };

                    var insertParams = GetInsertParameters(
                        child,
                        accountId,
                        -Math.Abs(item.Amount),
                        targetReconciliationId: null,
                        targetIsCleared: true,
                        targetFitId: item.BankId ?? string.Empty);

                    await conn.ExecuteAsync(GetInsertSql(), insertParams, tx);
                }
            }

            // 3. Insert Uncleared Remainder Record with its own unique TransactionId
            if (remainderAmount > 0) {
                var remainderChild = new Transaction {
                    TransactionId = Guid.NewGuid(),
                    Description = original.Description,
                    Memo = original.Memo,
                    Amount = remainderAmount,
                    TransactionDate = original.TransactionDate,
                    AccountId = accountId,
                    BillId = original.BillId,
                    BucketId = original.BucketId,
                    SubCategoryId = original.SubCategoryId,
                    FromAccountIsCleared = false,
                    FromFitId = Guid.NewGuid().ToString() // Fresh FitID so it stays available for future imports
                };

                var remainderParams = GetInsertParameters(
                    remainderChild,
                    accountId,
                    -Math.Abs(remainderAmount),
                    targetReconciliationId: null,
                    targetIsCleared: false,
                    targetFitId: remainderChild.FromFitId);

                await conn.ExecuteAsync(GetInsertSql(), remainderParams, tx);
            }

            // 4. Recalculate envelope balances for ALL affected buckets
            foreach (var bucketId in bucketsToRecalculate) {
                await RecalculateBucketBalanceAsync(bucketId, tx);
            }

            await tx.CommitAsync();
        }
        catch (Exception ex) {
            await tx.RollbackAsync();
            Log.Error(ex, "Error processing multi-match split for transaction ID {TransactionId}.",
                manualTransactionId);
            throw;
        }
    }

    public async Task<bool> UpdateTransactionForBankFitIdAsync(
        int accountId,
        string transactionId,
        string bankFitId,
        bool isCleared) {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            try {
                // Update ALL rows (splits) sharing this TransactionId for the target account
                await conn.ExecuteAsync(@"
                UPDATE Transactions 
                SET FitId = @bankFitId, 
                    IsCleared = @isCleared 
                WHERE AccountId = @accountId 
                  AND TransactionId = @transactionId",
                    new {
                        bankFitId,
                        isCleared = isCleared ? 1 : 0,
                        accountId,
                        transactionId
                    }, tx);

                await tx.CommitAsync();
                return true;
            }
            catch {
                await tx.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error updating transaction splits for bank FitID.");
            throw;
        }
    }

    // public async Task<bool> UpdateTransactionForBankFitIdAsync(int accountId, string transactionId,
    //     string bankFitId, bool isCleared, int id) {
    //     try {
    //         await using var conn = _db.GetConnection();
    //         await conn.OpenAsync();
    //         await using var tx = conn.BeginTransaction();
    //
    //         try {
    //             await conn.ExecuteAsync(@"
    //             UPDATE Transactions 
    //             SET FitId = @bankFitId, IsCleared = @isCleared 
    //             WHERE AccountId = @accountId AND TRANSACTIONID = @transactionId AND ID = @id",
    //                 new {
    //                     bankFitId,
    //                     isCleared = isCleared ? 1 : 0,
    //                     accountId,
    //                     transactionId,
    //                     id
    //                 }, tx);
    //
    //             await tx.CommitAsync();
    //             return true;
    //         }
    //         catch {
    //             await tx.RollbackAsync();
    //             throw;
    //         }
    //     }
    //     catch (Exception ex) {
    //         Log.Error(ex, "Error updating transaction for bank FitID[cite: 25].");
    //         throw;
    //     }
    // }

    private async Task<decimal> GetAccountBalanceAsOfAsync(int accountId, DateTime asOfDate,
        SqliteConnection? conn = null, IDbTransaction? tx = null) {
        try {
            var account =
                (await GetAllAccountsAsOfAsync(asOfDate: asOfDate, cn: conn, tx: tx))
                .FirstOrDefault(a => a.Id == accountId);
            return account?.Balance ?? 0;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error getting account balance as of date for account ID {AccountId}[cite: 25].", accountId);
            return 0m;
        }
    }

    private async Task<decimal> CalculateAccruedInterestAsync(DateTime paymentDate, decimal apr, int statementDay,
        int accountId, SqliteConnection? cn = null, IDbTransaction? tx = null) {
        try {
            int year = paymentDate.Year;
            int month = paymentDate.Month;

            if (paymentDate.Day < statementDay) {
                var prevMonthDate = paymentDate.AddMonths(-1);
                year = prevMonthDate.Year;
                month = prevMonthDate.Month;
            }

            int safeDay = Math.Min(statementDay, DateTime.DaysInMonth(year, month));
            DateTime targetStatementDate = new DateTime(year, month, safeDay).Date.AddDays(1).AddTicks(-1);

            decimal priorBalance = await GetAccountBalanceAsOfAsync(accountId, targetStatementDate, cn, tx);

            decimal monthlyRate = (apr / 100m) / 12m;
            decimal interestAmount = Math.Round(priorBalance * monthlyRate, 2, MidpointRounding.AwayFromZero);

            return interestAmount;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error calculating accrued interest for account ID {AccountId}[cite: 25].", accountId);
            return 0m;
        }
    }

    public async Task<bool> UpsertTransactionAsync(Transaction t) {
        try {
            t.Amount = Math.Abs(t.Amount);

            await using var conn = _db.GetConnection();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            try {
                var bucketsToRecalculate = new HashSet<int>();

                if ((t.BillId.HasValue && t.BillId.Value > 0) || (t.BucketId.HasValue && t.BucketId.Value > 0)) {
                    var payChecks = await conn.QueryAsync<Paycheck>(
                        "SELECT * FROM Paychecks WHERE EndDate IS NULL OR EndDate >= @TransactionDate",
                        new { TransactionDate = t.TransactionDate.ToString("yyyy-MM-dd") }, tx);

                    // Ensure PeriodBill and PeriodBucket snapshots exist for any linked Bill or Bucket
                    //We want to preserve what the expected amount state was at the time of the transaction. 
                    //Whether its the specific amount or an override, this should capture it.
                    //In this way we can say if a bill or envelope exceeded the expected amount as it was at the
                    //time of the transaction. This allows future projections to be governed by a new expected amount.
                    if (t.BillId.HasValue && t.BillId.Value > 0) {
                        foreach (var payCheck in payChecks) {
                            var periodDate = HelperMethods.GetCurrentPeriodStart(payCheck.StartDate, t.TransactionDate,
                                payCheck.Frequency);
                            await EnsurePeriodBillSnapshotAsync(conn, tx, t.BillId.Value, periodDate);
                        }

                        var monthStart = new DateTime(t.TransactionDate.Year, t.TransactionDate.Month, 1);
                        await EnsurePeriodBillSnapshotAsync(conn, tx, t.BillId.Value, monthStart);
                    }

                    if (t.BucketId.HasValue && t.BucketId.Value > 0) {
                        foreach (var payCheck in payChecks) {
                            var periodDate = HelperMethods.GetCurrentPeriodStart(payCheck.StartDate, t.TransactionDate,
                                payCheck.Frequency);

                            await EnsurePeriodBucketSnapshotAsync(conn, tx, t.BucketId.Value, periodDate);
                        }

                        var monthStart = new DateTime(t.TransactionDate.Year, t.TransactionDate.Month, 1);
                        await EnsurePeriodBucketSnapshotAsync(conn, tx, t.BucketId.Value, monthStart);
                    }
                }

                // Step 1: Capture historical Bucket IDs linked to this TransactionId prior to saving
                if (t.TransactionId != Guid.Empty) {
                    var oldRows = (await conn.QueryAsync<dynamic>(
                        "SELECT BucketId FROM Transactions WHERE TransactionId = @TransactionId",
                        new { TransactionId = t.TransactionId.ToString() }, tx)).ToList();

                    foreach (var row in oldRows) {
                        if (row.BucketId != null) {
                            bucketsToRecalculate.Add((int)row.BucketId);
                        }
                    }
                }

                // Guard: Clean up orphaned Reconciliation IDs if referenced record was invalidated/deleted
                if (t.FromAccountReconciliationId.HasValue) {
                    var exists = await conn.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM AccountReconciliations WHERE Id = @id",
                        new { id = t.FromAccountReconciliationId.Value }, tx);
                    if (exists == 0) t.FromAccountReconciliationId = null;
                }

                if (t.ToAccountReconciliationId.HasValue) {
                    var exists = await conn.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM AccountReconciliations WHERE Id = @id",
                        new { id = t.ToAccountReconciliationId.Value }, tx);
                    if (exists == 0) t.ToAccountReconciliationId = null;
                }

                if (t.TransactionId == Guid.Empty) {
                    t.TransactionId = Guid.NewGuid();
                }

                if (t.BucketId.HasValue) {
                    bucketsToRecalculate.Add(t.BucketId.Value);
                }

                string insertWithIdSql = GetInsertSql() + "; SELECT last_insert_rowid();";

                // Guard Check: Identify true single-account multi-category splits
                bool isMultiCategorySplit = t.Details != null
                                            && t.Details.Count > 1
                                            && t.Details.Select(d => d.AccountId ?? t.AccountId).Distinct().Count() ==
                                            1;

                // Step 2: Handle Multi-Category Splits
                if (isMultiCategorySplit) {
                    var currentDetailIds = t.Details!.Where(d => d.Id > 0).Select(d => d.Id).ToList();
                    if (currentDetailIds.Any()) {
                        await conn.ExecuteAsync(
                            "DELETE FROM Transactions WHERE TransactionId = @TxId AND Id NOT IN @Ids",
                            new { TxId = t.TransactionId.ToString(), Ids = currentDetailIds }, tx);
                    }

                    foreach (var detail in t.Details!) {
                        if (detail.BucketId.HasValue) {
                            bucketsToRecalculate.Add(detail.BucketId.Value);
                        }

                        var legTx = new Transaction {
                            TransactionId = t.TransactionId,
                            Description = detail.Description,
                            Memo = detail.Memo,
                            Amount = detail.Amount,
                            TransactionDate = t.TransactionDate,
                            AccountId = detail.AccountId ?? t.AccountId,
                            BillId = detail.BillId,
                            BucketId = detail.BucketId,
                            SubCategoryId = detail.SubCategoryId,
                            IsPrincipalOnly = t.IsPrincipalOnly,
                            IsInterestOnly = false,
                            PaycheckId = t.PaycheckId,
                            PaycheckOccurrenceDate = t.PaycheckOccurrenceDate,
                            FromAccountIsCleared = detail.IsCleared,
                            FromFitId = detail.FitId
                        };

                        decimal legAmount = -Math.Abs(detail.Amount);

                        if (detail.Id > 0) {
                            var p = GetUpdateParameters(legTx, legTx.AccountId, legAmount, detail.ReconciliationId,
                                detail.Id, detail.IsCleared, detail.FitId);
                            await conn.ExecuteAsync(GetUpdateSql(), p, tx);
                        }
                        else {
                            var p = GetInsertParameters(legTx, legTx.AccountId, legAmount, detail.ReconciliationId,
                                detail.IsCleared, detail.FitId);
                            detail.Id = await conn.ExecuteScalarAsync<int>(insertWithIdSql, p, tx);
                        }
                    }
                }
                else {
                    // Step 3: Standard Single-Leg Entry or 2-Account Transfer Path

                    // --- 1. OUTBOUND / FROM SIDE ---
                    if (t.AccountId.HasValue) {
                        decimal amount = -Math.Abs(t.Amount);
                        if (t.FromRecordId.HasValue && t.FromRecordId > 0) {
                            var p = GetUpdateParameters(t, t.AccountId, amount, t.FromAccountReconciliationId,
                                t.FromRecordId, t.FromAccountIsCleared ?? false, t.FromFitId);
                            await conn.ExecuteAsync(GetUpdateSql(), p, tx);
                        }
                        else {
                            var p = GetInsertParameters(t, t.AccountId, amount, t.FromAccountReconciliationId,
                                t.FromAccountIsCleared ?? false, t.FromFitId);
                            t.FromRecordId = await conn.ExecuteScalarAsync<int>(insertWithIdSql, p, tx);
                        }
                    }
                    else if (t.FromRecordId.HasValue && t.FromRecordId > 0) {
                        await conn.ExecuteAsync("DELETE FROM Transactions WHERE Id = @Id", new { Id = t.FromRecordId },
                            tx);
                        t.FromRecordId = null;
                    }

                    // --- 2. INBOUND / TO SIDE ---
                    if (t.ToAccountId.HasValue) {
                        decimal amount = t.AccountId.HasValue ? Math.Abs(t.Amount) : t.Amount;

                        if (t.ToRecordId.HasValue && t.ToRecordId > 0) {
                            var p = GetUpdateParameters(t, t.ToAccountId, amount, t.ToAccountReconciliationId,
                                t.ToRecordId,
                                t.ToAccountIsCleared ?? false, t.ToFitId);
                            await conn.ExecuteAsync(GetUpdateSql(), p, tx);
                        }
                        else {
                            var p = GetInsertParameters(t, t.ToAccountId, amount, t.ToAccountReconciliationId,
                                t.ToAccountIsCleared ?? false, t.ToFitId);
                            t.ToRecordId = await conn.ExecuteScalarAsync<int>(insertWithIdSql, p, tx);
                        }
                    }
                    else if (t.ToRecordId.HasValue && t.ToRecordId > 0) {
                        await conn.ExecuteAsync("DELETE FROM Transactions WHERE Id = @Id", new { Id = t.ToRecordId },
                            tx);
                        t.ToRecordId = null;
                    }
                }

                // Step 4: Mortgage Interest Computation
                if (t.ToAccountId.HasValue && !t.IsPrincipalOnly) {
                    var toAccount = (await GetAllAccountsAsOfAsync(asOfDate: t.TransactionDate, cn: conn, tx: tx))
                        .FirstOrDefault(a => a.Id == t.ToAccountId.Value);

                    if (toAccount != null && toAccount.IsLoanAccount && toAccount.MortgageDetails != null) {
                        int statementDay = toAccount.MortgageDetails.StatementDay;
                        int year = t.TransactionDate.Year;
                        int month = t.TransactionDate.Month;

                        if (t.TransactionDate.Day < statementDay) {
                            var prevMonthDate = t.TransactionDate.AddMonths(-1);
                            year = prevMonthDate.Year;
                            month = prevMonthDate.Month;
                        }

                        int safeDay = Math.Min(statementDay, DateTime.DaysInMonth(year, month));
                        DateTime targetStatementDate = new DateTime(year, month, safeDay);
                        DateTime nextStatementDate = targetStatementDate.AddMonths(1);

                        var memoToSearch = $"as of statement date {targetStatementDate:M/d/yyyy}";

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

                        if (existingInterestOnStatement == null ||
                            existingInterestOnStatement?.TransactionId.ToString() == t.TransactionId.ToString()) {
                            var interestAmount = await CalculateAccruedInterestAsync(t.TransactionDate,
                                toAccount.MortgageDetails.InterestRate, statementDay, t.ToAccountId.Value, conn, tx);

                            var existingInterest = await conn.QueryFirstOrDefaultAsync<dynamic>(
                                "SELECT Id, ReconciliationId, TransactionDate FROM Transactions WHERE TransactionId = @TransactionId AND IsInterestOnly = 1",
                                new { TransactionId = t.TransactionId.ToString() }, tx);

                            int? interestReconciliationId = existingInterest != null
                                ? (int?)existingInterest.ReconciliationId
                                : null;
                            int? existingInterestId = existingInterest != null ? (int?)existingInterest.Id : null;
                            bool interestIsCleared = existingInterest != null && existingInterest!.IsCleared == 1;

                            var interestTx = new Transaction {
                                TransactionId = t.TransactionId,
                                Description = "Interest",
                                Memo = memoToSearch,
                                Amount = Math.Abs(interestAmount),
                                TransactionDate = t.TransactionDate,
                                PeriodDate = t.PeriodDate,
                                IsInterestOnly = true,
                                FromAccountIsCleared = false,
                                ToAccountIsCleared = false
                            };

                            if (existingInterestId.HasValue) {
                                var intParam = GetUpdateParameters(interestTx, t.ToAccountId.Value,
                                    -Math.Abs(interestAmount), interestReconciliationId, existingInterestId.Value,
                                    interestIsCleared, t.ToFitId);
                                await conn.ExecuteAsync(GetUpdateSql(), intParam, tx);
                            }
                            else {
                                var intParam = GetInsertParameters(interestTx, t.ToAccountId.Value,
                                    -Math.Abs(interestAmount), interestReconciliationId, interestIsCleared, t.ToFitId);
                                await conn.ExecuteAsync(GetInsertSql(), intParam, tx);
                            }
                        }
                    }
                }

                // Step 5: Recalculate envelope balances for all impacted buckets
                foreach (var bucketId in bucketsToRecalculate) {
                    await RecalculateBucketBalanceAsync(bucketId, tx);
                }

                await tx.CommitAsync();
                return true;
            }
            catch {
                await tx.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error upserting transaction.");
            throw;
        }
    }

    public async Task RecalculateBucketBalanceAsync(int bucketId, IDbTransaction? tx = null) {
        try {
            var conn = tx?.Connection ?? _db.GetConnection();
            bool isLocalConn = tx == null;

            try {
                if (isLocalConn && conn is DbConnection dbConn && dbConn.State != ConnectionState.Open) {
                    await dbConn.OpenAsync();
                }

                var bucket = await conn.QuerySingleOrDefaultAsync<BudgetBucket>(
                    "SELECT * FROM Buckets WHERE Id = @bucketId", new { bucketId }, tx);

                if (bucket == null || bucket.Type != BucketType.AccumulatingDrawdown) return;

                var totalSpent = await conn.ExecuteScalarAsync<decimal?>(@"
                SELECT ABS(SUM(Amount)) 
                FROM Transactions 
                WHERE BucketId = @bucketId AND Amount < 0", new { bucketId }, tx) ?? 0m;

                var totalContributed = await conn.ExecuteScalarAsync<decimal?>(@"
                SELECT SUM(ActualAmount) 
                FROM PeriodBuckets 
                WHERE BucketId = @bucketId AND IsPaid = 1", new { bucketId }, tx) ?? 0m;

                decimal newCurrentBalance = Math.Max(0, bucket.InitialBalance + totalContributed - totalSpent);

                await conn.ExecuteAsync(@"
                UPDATE Buckets 
                SET CurrentBalance = @newCurrentBalance 
                WHERE Id = @bucketId", new { bucketId, newCurrentBalance }, tx);
            }
            finally {
                if (isLocalConn && conn is IAsyncDisposable asyncDisposable) {
                    await asyncDisposable.DisposeAsync();
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error recalculating bucket balance for bucket ID {BucketId}[cite: 25].", bucketId);
        }
    }

    public async Task DeleteTransactionAsync(Guid transactionId) {
        try {
            await using var conn = _db.GetConnection();
            await conn.ExecuteAsync("DELETE FROM Transactions WHERE TransactionId = @transactionId",
                new { transactionId = transactionId.ToString() });
        }
        catch (Exception ex) {
            Log.Error(ex, "Error deleting transaction with ID {TransactionId}[cite: 25].", transactionId);
            throw;
        }
    }

    #region Dealign with snapshots of expectedamount

    //Call these both for the first of that month and for the period of each paycheck that exists at the time.
    private async Task EnsurePeriodBillSnapshotAsync(IDbConnection conn, IDbTransaction tx, long billId,
    DateTime transactionDate) {
    string targetPeriodDate = transactionDate.ToString("yyyy-MM-dd");

    const string checkSql = @"
    SELECT COUNT(1) 
    FROM PeriodBills 
    WHERE BillId = @billId AND PeriodDate = @targetPeriodDate;";

    int exists = await conn.ExecuteScalarAsync<int>(checkSql, new { billId, targetPeriodDate }, tx);

    if (exists == 0) {
        const string snapshotSql = @"
        INSERT INTO PeriodBills (BillId, PeriodDate, DueDate, ActualAmount, IsPaid, FitId)
        SELECT 
            b.Id,
            @targetPeriodDate,
            date(@targetPeriodDate, '+' || (b.DueDay - 1) || ' days'),
            COALESCE(
                json_extract(b.Overrides, '$.' || strftime('%m', @targetPeriodDate)), 
                b.ExpectedAmount
            ) AS ActualAmount,
            0 AS IsPaid,
            @fitId AS FitId
        FROM Bills b
        WHERE b.Id = @billId;";

        await conn.ExecuteAsync(snapshotSql, new { 
            billId, 
            targetPeriodDate, 
            fitId = Guid.NewGuid().ToString() 
        }, tx);
    }
}

private async Task EnsurePeriodBucketSnapshotAsync(IDbConnection conn, IDbTransaction tx, long bucketId,
    DateTime transactionDate) {
    string targetPeriodDate = transactionDate.ToString("yyyy-MM-dd");

    const string checkSql = @"
    SELECT 
        (SELECT COUNT(1) FROM PeriodBuckets WHERE BucketId = @bucketId AND PeriodDate = @targetPeriodDate) AS RecordCount,
        Type
    FROM Buckets 
    WHERE Id = @bucketId;";

    var result = await conn.QueryFirstOrDefaultAsync<dynamic>(checkSql, new { bucketId, targetPeriodDate }, tx);

    if (result != null && result!.Type == 0 && result!.RecordCount == 0) {
        const string snapshotSql = @"
        INSERT INTO PeriodBuckets (BucketId, PeriodDate, ActualAmount, IsPaid, FitId)
        SELECT 
            b.Id,
            @targetPeriodDate,
            COALESCE(
                json_extract(b.Overrides, '$.' || strftime('%m', @targetPeriodDate)), 
                b.ExpectedAmount
            ) AS ActualAmount,
            0 AS IsPaid,
            @fitId AS FitId
        FROM Buckets b
        WHERE b.Id = @bucketId;";

        await conn.ExecuteAsync(snapshotSql, new { 
            bucketId, 
            targetPeriodDate, 
            fitId = Guid.NewGuid().ToString() 
        }, tx);
    }
}

    #endregion


    #region Private Service Helpers (Mapping Engine)

    // private IEnumerable<Transaction> MergeDbRowsToUiTransactions(IEnumerable<dynamic> dbRows) {
    //     try {
    //         var resultList = new List<Transaction>();
    //         var transactionGroups = dbRows.GroupBy(r => r.TransactionId?.ToString());
    //
    //         foreach (var group in transactionGroups) {
    //             if (string.IsNullOrEmpty(group.Key)) continue;
    //
    //             var list = group.ToList();
    //
    //             // Project all rows in this group to TransactionDetail items
    //             var details = list.Select(MapDynamicToTransactionDetail).ToList();
    //
    //             bool hasInterestOnly =
    //                 list.Any(r => r.IsInterestOnly != null && Convert.ToInt32(r.IsInterestOnly) == 1);
    //
    //             if (list.Count() >= 2) {
    //                 if (hasInterestOnly) {
    //                     var normalRows = list.Where(r => r.IsInterestOnly != 1).ToList();
    //                     var interestRows = list.Where(r => r.IsInterestOnly == 1).ToList();
    //
    //                     if (normalRows.Count == 2) {
    //                         var tx = MergeRows(normalRows);
    //                         tx.Details.AddRange(normalRows.Select(MapDynamicToTransactionDetail));
    //                         resultList.Add(tx);
    //                     }
    //                     else if (normalRows.Count == 1) {
    //                         var tx = MapDynamicToTransaction(normalRows[0], false);
    //                         tx.Details.Add(MapDynamicToTransactionDetail(normalRows[0]));
    //                         resultList.Add(tx);
    //                     }
    //
    //                     foreach (var ir in interestRows) {
    //                         if ((double)ir.Amount < 0) {
    //                             var intTx = MapDynamicToTransaction(ir, false);
    //                             intTx.Details.Add(MapDynamicToTransactionDetail(ir));
    //                             resultList.Add(intTx);
    //                         }
    //                     }
    //
    //                     continue;
    //                 }
    //
    //                 // Standard 2-leg transfer or multi-row split
    //                 var transaction = MergeRows(list);
    //
    //                 // If it's a split expense (multiple rows under 1 account) vs a 2-account transfer
    //                 transaction.Details.AddRange(details);
    //                 resultList.Add(transaction);
    //             }
    //             else {
    //                 var standaloneRow = list.First();
    //
    //                 if (standaloneRow.IsInterestOnly == 1) {
    //                     if ((double)standaloneRow.Amount < 0) {
    //                         var tx = MapDynamicToTransaction(standaloneRow, isTransferSide: false);
    //                         tx.Details.Add(details.First());
    //                         resultList.Add(tx);
    //                     }
    //                 }
    //                 else {
    //                     var tx = MapDynamicToTransaction(standaloneRow, isTransferSide: false);
    //                     tx.Details.Add(details
    //                         .First()); // Ensures 1-leg standalone entries still get their Details populated!
    //                     resultList.Add(tx);
    //                 }
    //             }
    //         }
    //
    //         return resultList;
    //     }
    //     catch (Exception ex) {
    //         Log.Error(ex, "Error merging DB rows to UI transactions.");
    //         return Enumerable.Empty<Transaction>();
    //     }
    // }

    private IEnumerable<Transaction> MergeDbRowsToUiTransactions(IEnumerable<dynamic> dbRows) {
        try {
            var resultList = new List<Transaction>();
            var transactionGroups = dbRows.GroupBy(r => r.TransactionId?.ToString());

            foreach (var group in transactionGroups) {
                if (string.IsNullOrEmpty(group.Key)) continue;

                var list = group.ToList();
                var details = list.Select(MapDynamicToTransactionDetail).ToList();

                var distinctAccountIds = list
                    .Where(r => r.AccountId != null)
                    .Select(r => (int)r.AccountId)
                    .Distinct()
                    .ToList();

                bool hasInterestOnly =
                    list.Any(r => r.IsInterestOnly != null && Convert.ToInt32(r.IsInterestOnly) == 1);

                if (hasInterestOnly) {
                    var normalRows = list.Where(r => r.IsInterestOnly != 1).ToList();
                    var interestRows = list.Where(r => r.IsInterestOnly == 1).ToList();

                    if (normalRows.Count == 2 && distinctAccountIds.Count > 1) {
                        var tx = MergeRows(normalRows);
                        tx.Details.AddRange(normalRows.Select(MapDynamicToTransactionDetail));
                        resultList.Add(tx);
                    }
                    else {
                        foreach (var row in normalRows) {
                            var tx = MapDynamicToTransaction(row, isTransferSide: false);
                            tx.Details.Add(MapDynamicToTransactionDetail(row));
                            resultList.Add(tx);
                        }
                    }

                    foreach (var ir in interestRows) {
                        if ((double)ir.Amount < 0) {
                            var intTx = MapDynamicToTransaction(ir, isTransferSide: false);
                            intTx.Details.Add(MapDynamicToTransactionDetail(ir));
                            resultList.Add(intTx);
                        }
                    }

                    continue;
                }

                // CASE A: Standard 2-Account Transfer (e.g., Checking -> Credit Card)
                if (distinctAccountIds.Count > 1 && list.Count >= 2) {
                    var transferTx = MergeRows(list);
                    transferTx.Details.AddRange(details);
                    resultList.Add(transferTx);
                }
                // CASE B: Multi-row Category Split within the SAME Account
                else if (list.Count > 1) {
                    var primaryRow = list.First();

                    var splitTx = new Transaction {
                        TransactionId = Guid.Parse(primaryRow.TransactionId.ToString()),
                        Description = primaryRow.Description ?? string.Empty,
                        NormalizedDescription = primaryRow.NormalizedDescription ?? string.Empty,
                        Memo = primaryRow.Memo,
                        TransactionDate = DateTime.Parse(primaryRow.TransactionDate),
                        AccountId = (int?)primaryRow.AccountId,
                        AccountName = primaryRow.AccountName,
                        Amount = list.Sum(r => Math.Abs((decimal)r.Amount)), // Total header outflow

                        // FromRecordId is NULL at header level because multiple ledger rows make up this total
                        FromRecordId = null,
                        FromFitId = primaryRow.FitId?.ToString() ?? string.Empty,
                        FromAccountIsCleared = list.All(r => r.IsCleared == 1),
                        FromAccountReconciliationId = primaryRow.ReconciliationId != null
                            ? (int?)primaryRow.ReconciliationId
                            : null,

                        ToAccountId = null,
                        ToRecordId = null,
                        ToFitId = string.Empty
                    };

                    // Each detail item retains its own distinct DB Id (FromRecordId)
                    splitTx.Details.AddRange(details);
                    resultList.Add(splitTx);
                }
                // CASE C: Standard 1-Leg Single Row Entry
                else {
                    var standaloneRow = list.First();
                    var tx = MapDynamicToTransaction(standaloneRow, isTransferSide: false);
                    tx.Details.Add(details.First());
                    resultList.Add(tx);
                }
            }

            return resultList;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error merging DB rows to UI transactions.");
            return Enumerable.Empty<Transaction>();
        }
    }

    //to take dynamic and create TransactionDetail to become Transaction.Details
    private TransactionDetail MapDynamicToTransactionDetail(dynamic row) {
        return new TransactionDetail {
            Id = Convert.ToInt32(row.Id),
            TransactionId = Guid.Parse(row.TransactionId.ToString()),
            Description = row.Description ?? string.Empty,
            Memo = row.Memo,
            Amount = Math.Abs((decimal)row.Amount),
            TransactionDate = DateTime.Parse(row.TransactionDate),
            AccountId = row.AccountId != null ? (int?)row.AccountId : null,
            BillId = row.BillId != null ? (int?)row.BillId : null,
            BucketId = row.BucketId != null ? (int?)row.BucketId : null,
            SubCategoryId = row.SubCategoryId != null ? (int?)row.SubCategoryId : null,
            ReconciliationId = row.ReconciliationId != null ? (int?)row.ReconciliationId : null,
            IsCleared = row.IsCleared == 1,
            FitId = row.FitId?.ToString() ?? string.Empty,
            AccountName = row.AccountName,
            BillName = row.BillName,
            BucketName = row.BucketName
        };
    }

    private Transaction MergeRows(IEnumerable<dynamic> group) {
        var groupList = group.ToList();

        var outboundSide = groupList.FirstOrDefault(r => r != null && (double)r!.Amount < 0);
        var inboundSide = groupList.FirstOrDefault(r => r != null && (double)r!.Amount >= 0);

        var primaryRow = outboundSide ?? inboundSide;
        if (primaryRow == null) {
            throw new InvalidOperationException("Cannot merge an empty group of transaction rows.");
        }

        var uiTx = MapDynamicToTransaction(primaryRow, isTransferSide: true);

        if (outboundSide != null && inboundSide != null) {
            uiTx.FromRecordId = Convert.ToInt64(outboundSide!.Id);
            uiTx.FromFitId = outboundSide!.FitId != null ? Convert.ToString(outboundSide!.FitId) : "";

            uiTx.AccountId = (int)outboundSide.AccountId;
            uiTx.FromAccountReconciliationId = outboundSide.ReconciliationId != null
                ? (int?)outboundSide.ReconciliationId
                : null;
            uiTx.AccountName = outboundSide.AccountName;
            uiTx.FromAccountIsCleared = outboundSide.IsCleared == 1;
            uiTx.ToRecordId = Convert.ToInt64(inboundSide!.Id);
            uiTx.ToFitId = inboundSide!.FitId != null ? Convert.ToString(inboundSide!.FitId) : "";
            uiTx.ToAccountId = (int)inboundSide.AccountId;
            uiTx.ToAccountReconciliationId =
                inboundSide.ReconciliationId != null ? (int?)inboundSide.ReconciliationId : null;
            uiTx.ToAccountName = inboundSide.AccountName;
            uiTx.ToAccountIsCleared = inboundSide.IsCleared == 1;
            uiTx.Amount = Math.Abs((decimal)uiTx.Amount);
        }

        return uiTx;
    }

    private Transaction MapDynamicToTransaction(dynamic row, bool isTransferSide) {
        int? dbAccountId = row.AccountId != null ? (int?)row.AccountId : null;
        int? dbReconciliationId = row.ReconciliationId != null ? (int?)row.ReconciliationId : null;
        decimal amount = (decimal)row.Amount;

        int? uiAccountId = null;
        int? uiToAccountId = null;
        bool? uiIsCleared = null;
        bool? uiToIsCleared = null;
        long? uiRecordId = null;
        long? uiToRecordId = null;
        int? uiFromAccountReconciliationId = null;
        int? uiToAccountReconciliationId = null;
        string uiFromFitId = "";
        string uiToFitId = "";

        if (isTransferSide) {
            uiAccountId = dbAccountId;
            uiFromAccountReconciliationId = dbReconciliationId;
            uiRecordId = Convert.ToInt64(row.Id);
            uiIsCleared = row.IsCleared == 1;
            uiToIsCleared = null;
            uiToRecordId = null;
            uiToAccountId = null;
            uiToAccountReconciliationId = null;
            uiFromFitId = row.FitId != null ? Convert.ToString(row.FitId) : "";
            uiToFitId = "";
        }
        else {
            if (amount >= 0) {
                uiAccountId = null;
                uiFromAccountReconciliationId = null;
                uiRecordId = null;
                uiToRecordId = Convert.ToInt64(row.Id);
                uiIsCleared = null;
                uiToIsCleared = row.IsCleared == 1;
                uiToAccountId = dbAccountId;
                uiToAccountReconciliationId = dbReconciliationId;
                uiToFitId = row.FitId != null ? Convert.ToString(row.FitId) : "";
                uiFromFitId = "";
                uiFromAccountReconciliationId = null;
            }
            else {
                uiAccountId = dbAccountId;
                uiFromAccountReconciliationId = dbReconciliationId;
                uiIsCleared = row.IsCleared == 1;
                uiToIsCleared = null;
                uiToAccountId = null;
                uiToAccountReconciliationId = null;
                uiRecordId = Convert.ToInt64(row.Id);
                uiToRecordId = null;
                uiFromFitId = row.FitId != null ? Convert.ToString(row.FitId) : "";
                uiToFitId = "";
            }
        }

        return new Transaction {
            Description = row.Description,
            NormalizedDescription = row.NormalizedDescription,
            Memo = row.Memo,
            Amount = Math.Abs(amount),
            TransactionDate = DateTime.Parse(row.TransactionDate),
            TransactionId = Guid.Parse(row.TransactionId.ToString()),
            FromRecordId = uiRecordId,
            ToRecordId = uiToRecordId,
            AccountId = uiAccountId,
            AccountName = uiAccountId == null ? "" : row.AccountName,
            ToAccountId = uiToAccountId,
            ToAccountName = uiToAccountId == null ? "" :
                string.IsNullOrEmpty(row.ToAccountName) ? row.AccountName : row.ToAccountName,
            BillId = row.BillId != null ? (int)row.BillId : null,
            BillName = row.BillName,
            BucketId = row.BucketId != null ? (int)row.BucketId : null,
            SubCategoryId = row.SubCategoryId != null ? (int)row.SubCategoryId : null,
            BucketName = row.BucketName,
            IsPrincipalOnly = row.IsPrincipalOnly == 1,
            IsInterestOnly = row.IsInterestOnly == 1,
            FromFitId = uiFromFitId,
            ToFitId = uiToFitId,
            PaycheckId = row.PaycheckId != null ? (int)row.PaycheckId : null,
            PaycheckOccurrenceDate =
                row.PaycheckOccurrenceDate != null ? DateTime.Parse(row.PaycheckOccurrenceDate) : null,
            FromAccountReconciliationId = uiFromAccountReconciliationId,
            ToAccountReconciliationId = uiToAccountReconciliationId,
            FromAccountIsCleared = uiIsCleared,
            ToAccountIsCleared = uiToIsCleared,
        };
    }

    private string GetInsertSql() {
        return
            @"INSERT INTO Transactions (TransactionId, Description, Memo, Amount, TransactionDate, AccountId, BillId, BucketId, PeriodDate, IsPrincipalOnly, IsInterestOnly, FitId, PaycheckId, PaycheckOccurrenceDate, ReconciliationId, NormalizedDescription, IsCleared, SubCategoryId)
                 VALUES (@TransactionId, @Description, @Memo, @Amount, @TransactionDate, @AccountId, @BillId, @BucketId, @PeriodDate, @IsPrincipalOnly, @IsInterestOnly, @FitId, @PaycheckId, @PaycheckOccurrenceDate, @ReconciliationId, @NormalizedDescription, @IsCleared, @SubCategoryId)";
    }

    private string GetUpdateSql() {
        return
            @"UPDATE Transactions SET TransactionId=@TransactionId, Description=@Description, Memo=@Memo, Amount=@Amount, TransactionDate=@TransactionDate, AccountId=@AccountId, BillId=@BillId, BucketId=@BucketId, PeriodDate=@PeriodDate, IsPrincipalOnly= @IsPrincipalOnly, IsInterestOnly=@IsInterestOnly, FitId=@FitId, PaycheckId=@PaycheckId, PaycheckOccurrenceDate=@PaycheckOccurrenceDate, ReconciliationId=@ReconciliationId, NormalizedDescription=@NormalizedDescription, IsCleared=@IsCleared, SubCategoryId=@SubCategoryId
                 WHERE Id=@Id";
    }

    private DynamicParameters GetInsertParameters(Transaction t, int? targetAccountId, decimal targetedAmount,
        int? targetReconciliationId, bool targetIsCleared, string targetFitId) {
        var p = new DynamicParameters();
        p.Add("TransactionId", t.TransactionId.ToString());
        p.Add("Description", t.Description);
        p.Add("Memo", t.Memo);
        p.Add("Amount", Math.Round(targetedAmount, 2, MidpointRounding.AwayFromZero));
        p.Add("TransactionDate", t.TransactionDate.ToString("yyyy-MM-dd"));
        p.Add("AccountId", targetAccountId);
        p.Add("BillId", t.BillId);
        p.Add("BucketId", t.BucketId);
        p.Add("PeriodDate", "1900-01-01");
        p.Add("IsPrincipalOnly", t.IsPrincipalOnly ? 1 : 0);
        p.Add("IsInterestOnly", t.IsInterestOnly ? 1 : 0);
        p.Add("FitId", targetFitId);
        p.Add("PaycheckId", t.PaycheckId);
        p.Add("PaycheckOccurrenceDate", t.PaycheckOccurrenceDate?.ToString("yyyy-MM-dd"));
        p.Add("ReconciliationId", targetReconciliationId);
        p.Add("NormalizedDescription", TransactionMatcher.NormalizeName(t.Description));
        p.Add("IsCleared", targetIsCleared);
        p.Add("SubCategoryId", t.SubCategoryId);
        return p;
    }

    private DynamicParameters GetInsertParameters(Ledger t, int? targetAccountId, decimal targetedAmount,
        int? targetReconciliationId, bool targetIsCleared, string targetFitId) {
        var p = new DynamicParameters();
        p.Add("TransactionId", t.TransactionId.ToString());
        p.Add("Description", t.Description);
        p.Add("Memo", t.Memo);
        p.Add("Amount", Math.Round(targetedAmount, 2, MidpointRounding.AwayFromZero));
        p.Add("TransactionDate", t.TransactionDate.ToString("yyyy-MM-dd"));
        p.Add("AccountId", targetAccountId);
        p.Add("BillId", t.BillId);
        p.Add("BucketId", t.BucketId);
        p.Add("PeriodDate", "1900-01-01");
        p.Add("IsPrincipalOnly", t.IsPrincipalOnly ? 1 : 0);
        p.Add("IsInterestOnly", t.IsInterestOnly ? 1 : 0);
        p.Add("FitId", targetFitId);
        p.Add("PaycheckId", t.PaycheckId);
        p.Add("PaycheckOccurrenceDate", t.PaycheckOccurrenceDate?.ToString("yyyy-MM-dd"));
        p.Add("ReconciliationId", targetReconciliationId);
        p.Add("NormalizedDescription", TransactionMatcher.NormalizeName(t.Description));
        p.Add("IsCleared", targetIsCleared);
        p.Add("SubCategoryId", t.SubCategoryId);
        return p;
    }

    private DynamicParameters GetUpdateParameters(Transaction t, int? targetAccountId, decimal targetedAmount,
        int? targetReconciliationId, long? id, bool targetIsCleared, string targetFitId) {
        var p = new DynamicParameters();
        p.Add("TransactionId", t.TransactionId.ToString());
        p.Add("Description", t.Description);
        p.Add("Memo", t.Memo);
        p.Add("Amount", Math.Round(targetedAmount, 2, MidpointRounding.AwayFromZero));
        p.Add("TransactionDate", t.TransactionDate.ToString("yyyy-MM-dd"));
        p.Add("AccountId", targetAccountId);
        p.Add("BillId", t.BillId);
        p.Add("BucketId", t.BucketId);
        p.Add("PeriodDate", "1900-01-01");
        p.Add("IsPrincipalOnly", t.IsPrincipalOnly ? 1 : 0);
        p.Add("IsInterestOnly", t.IsInterestOnly ? 1 : 0);
        p.Add("FitId", targetFitId);
        p.Add("PaycheckId", t.PaycheckId);
        p.Add("PaycheckOccurrenceDate", t.PaycheckOccurrenceDate?.ToString("yyyy-MM-dd"));
        p.Add("ReconciliationId", targetReconciliationId);
        p.Add("Id", id);
        p.Add("NormalizedDescription", TransactionMatcher.NormalizeName(t.Description));
        p.Add("IsCleared", targetIsCleared);
        p.Add("SubCategoryId", t.SubCategoryId);
        return p;
    }

    private DynamicParameters GetUpdateParameters(Ledger t, int? targetAccountId, decimal targetedAmount,
        int? targetReconciliationId, long? id, bool targetIsCleared, string targetFitId) {
        var p = new DynamicParameters();
        p.Add("TransactionId", t.TransactionId.ToString());
        p.Add("Description", t.Description);
        p.Add("Memo", t.Memo);
        p.Add("Amount", Math.Round(targetedAmount, 2, MidpointRounding.AwayFromZero));
        p.Add("TransactionDate", t.TransactionDate.ToString("yyyy-MM-dd"));
        p.Add("AccountId", targetAccountId);
        p.Add("BillId", t.BillId);
        p.Add("BucketId", t.BucketId);
        p.Add("PeriodDate", "1900-01-01");
        p.Add("IsPrincipalOnly", t.IsPrincipalOnly ? 1 : 0);
        p.Add("IsInterestOnly", t.IsInterestOnly ? 1 : 0);
        p.Add("FitId", targetFitId);
        p.Add("PaycheckId", t.PaycheckId);
        p.Add("PaycheckOccurrenceDate", t.PaycheckOccurrenceDate?.ToString("yyyy-MM-dd"));
        p.Add("ReconciliationId", targetReconciliationId);
        p.Add("Id", id);
        p.Add("NormalizedDescription", TransactionMatcher.NormalizeName(t.Description));
        p.Add("IsCleared", targetIsCleared);
        p.Add("SubCategoryId", t.SubCategoryId);
        return p;
    }

    #endregion
}