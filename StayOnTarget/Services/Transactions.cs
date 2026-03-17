using Dapper;
using StayOnTarget.Models;

namespace StayOnTarget.Services;

public partial class BudgetService
{
    public IEnumerable<Transaction> GetTransactions(DateTime periodDate)
    {
        using var conn = _db.GetConnection();
        return conn.Query<Transaction>(@"
            SELECT t.*, a1.Name as AccountName, a2.Name as ToAccountName 
            , Bills.Name as BillName 
            , Buckets.Name as BucketName 
            FROM Transactions t
            LEFT JOIN Accounts a1 ON t.AccountId = a1.Id
            LEFT JOIN Accounts a2 ON t.ToAccountId = a2.Id
            LEFT JOIN Bills ON t.BillId = Bills.Id
            LEFT JOIN Buckets ON t.BucketId = Buckets.Id
            WHERE t.PeriodDate = @periodDate", new { periodDate = periodDate.ToString("yyyy-MM-dd") });
    }

    public IEnumerable<Transaction> GetAllTransactions()
    {
        using var conn = _db.GetConnection();
        return conn.Query<Transaction>("SELECT * FROM Transactions");
    }
    
    public IEnumerable<Transaction> GetAllPaycheckTransactions()
    {
        using var conn = _db.GetConnection();
        return conn.Query<Transaction>("SELECT * FROM Transactions WHERE NOT PaycheckId IS NULL");
    }
    public IEnumerable<Transaction> GetBillTransactions()
    {
        using var conn = _db.GetConnection();
        return conn.Query<Transaction>("SELECT * FROM Transactions WHERE NOT BillId IS NULL");
    }
    
    public IEnumerable<Transaction> GetBucketTransactions()
    {
        using var conn = _db.GetConnection();
        return conn.Query<Transaction>("SELECT * FROM Transactions WHERE NOT BucketId IS NULL");
    }

    
    public IEnumerable<Transaction> GetAllUnreconciledTransactions()
    {
        using var conn = _db.GetConnection();
        var recs = conn.Query<Transaction>("SELECT * FROM Transactions");
        
        return recs
            .Where(t => (t.AccountId.HasValue && !t.FromAccountReconciledId.HasValue) ||
                        (t.ToAccountId.HasValue && !t.ToAccountReconciledId.HasValue)).ToList();
    }
    
    public IEnumerable<Transaction> GetAllUnreconciledTransactionsSinceLastReconciliation(int accountId)
    {
        using var conn = _db.GetConnection();
        return conn.Query<Transaction>(@"
            select t.*, d.MinDate from accounts a
LEFT JOIN transactions t ON t.AccountId=a.Id OR t.ToAccountId=a.Id
INNER JOIN(
select a.Id, IfNull( ar.MaxDate, a.BalanceAsOf) AS MinDate from accounts a
LEFT JOIN (
    SELECT AccountId, MAX(ReconciledAsOfDate) AS MaxDate
    FROM AccountReconciliations
	WHERE IsInvalidated IS NULL OR IsInvalidated=1
    GROUP BY AccountId
) AS ar ON ar.AccountId = a.Id 
WHERE a.Id=@accountId
) As d
WHERE a.Id=d.Id AND ((t.AccountId=a.Id AND FromAccountReconciledId IS NULL) OR (ToAccountId=a.Id AND ToAccountReconciledId IS NULL))", new { accountId = accountId });
    }

    public void UpsertTransaction(Transaction t)
    {
        using var conn = _db.GetConnection();

        // Check if transaction date changed to a date that would invalidate reconciliation
        if (t.Id != 0)
        {
            var oldTransaction = conn.QueryFirstOrDefault<Transaction>(
                "SELECT * FROM Transactions WHERE Id = @id", new { id = t.Id });

            if (oldTransaction != null && oldTransaction.Date != t.Date)
            {
                // Check if this affects reconciliations for AccountId
                if (t.AccountId.HasValue && t.FromAccountReconciledId.HasValue)
                {
                    var recon = conn.QueryFirstOrDefault<AccountReconciliation>(
                        "SELECT * FROM AccountReconciliations WHERE Id = @id",
                        new { id = t.FromAccountReconciledId.Value });

                    if (recon != null && t.Date <= recon.ReconciledAsOfDate)
                    {
                        // Invalidate this reconciliation
                        InvalidateReconciliationsAfterDate(t.AccountId.Value, t.Date);
                        t.FromAccountReconciledId = null;
                    }
                }

                // Check if this affects reconciliations for ToAccountId
                if (t.ToAccountId.HasValue && t.ToAccountReconciledId.HasValue)
                {
                    var recon = conn.QueryFirstOrDefault<AccountReconciliation>(
                        "SELECT * FROM AccountReconciliations WHERE Id = @id",
                        new { id = t.ToAccountReconciledId.Value });

                    if (recon != null && t.Date <= recon.ReconciledAsOfDate)
                    {
                        // Invalidate this reconciliation
                        InvalidateReconciliationsAfterDate(t.ToAccountId.Value, t.Date);
                        t.ToAccountReconciledId = null;
                    }
                }
            }
        }

        var param = new
        {
            t.Id,
            t.Description,
            t.Amount,
            Date = t.Date.ToString("yyyy-MM-dd"),
            t.AccountId,
            t.ToAccountId,
            t.BillId,
            t.BucketId,
            PeriodDate = t.PeriodDate.ToString("yyyy-MM-dd"),
            t.IsPrincipalOnly,
            FitId = t.FitId.ToString(),
            t.PaycheckId,
            PaycheckOccurrenceDate = t.PaycheckOccurrenceDate?.ToString("yyyy-MM-dd"),
            t.FromAccountReconciledId,
            t.ToAccountReconciledId
        };
        if (t.Id == 0)
        {
            conn.Execute(@"INSERT INTO Transactions (Description, Amount, Date, AccountId, ToAccountId, BillId, BucketId, PeriodDate, IsPrincipalOnly, FitId, PaycheckId, PaycheckOccurrenceDate, FromAccountReconciledId, ToAccountReconciledId)
                           VALUES (@Description, @Amount, @Date, @AccountId, @ToAccountId, @BillId, @BucketId, @PeriodDate, @IsPrincipalOnly, @FitId, @PaycheckId, @PaycheckOccurrenceDate, @FromAccountReconciledId, @ToAccountReconciledId)", param);
        }
        else
        {
            conn.Execute(@"UPDATE Transactions SET Description=@Description, Amount=@Amount, Date=@Date,
                           AccountId=@AccountId, ToAccountId=@ToAccountId, BillId=@BillId, BucketId=@BucketId, PeriodDate=@PeriodDate, IsPrincipalOnly=@IsPrincipalOnly, PaycheckId=@PaycheckId, PaycheckOccurrenceDate=@PaycheckOccurrenceDate, FromAccountReconciledId=@FromAccountReconciledId, ToAccountReconciledId=@ToAccountReconciledId WHERE Id=@Id", param);
        }
    }

    public void DeleteTransaction(int id)
    {
        using var conn = _db.GetConnection();
        conn.Execute("DELETE FROM Transactions WHERE Id = @id", new { id });
    }
}