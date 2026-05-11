using Dapper;
using StayOnTarget.Models;

namespace StayOnTarget.Services;

public partial class BudgetService
{
    public IEnumerable<AccountReconciliation> GetAllAccountReconciliations()
    {
        using var conn = _db.GetConnection();
        var reconciliations = conn.Query<AccountReconciliation>("SELECT * FROM AccountReconciliations").ToList();

        // Populate account names for UI
        var accounts = GetAllAccounts().ToDictionary(a => a.Id, a => a.Name);
        foreach (var recon in reconciliations)
        {
            if (accounts.TryGetValue(recon.AccountId, out var accountName))
            {
                recon.AccountName = accountName;
            }
        }

        return reconciliations;
    }

    public AccountReconciliation? GetLatestValidReconciliation(int accountId)
    {
        using var conn = _db.GetConnection();
        return conn.QueryFirstOrDefault<AccountReconciliation>(
            @"SELECT * FROM AccountReconciliations
              WHERE AccountId = @accountId AND IsInvalidated = 0
              ORDER BY ReconciledAsOfDate DESC
              LIMIT 1",
            new { accountId });
    }

    public void UpsertAccountReconciliation(AccountReconciliation reconciliation)
    {
        using var conn = _db.GetConnection();
        var param = new
        {
            reconciliation.Id,
            reconciliation.AccountId,
            ReconciledAsOfDate = reconciliation.ReconciledAsOfDate.ToString("yyyy-MM-dd"),
            reconciliation.ReconciledBalance,
            ReconciledOnDate = reconciliation.ReconciledOnDate.ToString("yyyy-MM-dd"),
            IsInvalidated = reconciliation.IsInvalidated ? 1 : 0
        };

        if (reconciliation.Id == 0)
        {
            reconciliation.Id = conn.ExecuteScalar<int>(@"
                INSERT INTO AccountReconciliations (AccountId, ReconciledAsOfDate, ReconciledBalance, ReconciledOnDate, IsInvalidated)
                VALUES (@AccountId, @ReconciledAsOfDate, @ReconciledBalance, @ReconciledOnDate, @IsInvalidated);
                SELECT last_insert_rowid();", param);
        }
        else
        {
            conn.Execute(@"
                UPDATE AccountReconciliations
                SET AccountId=@AccountId, ReconciledAsOfDate=@ReconciledAsOfDate,
                    ReconciledBalance=@ReconciledBalance, ReconciledOnDate=@ReconciledOnDate,
                    IsInvalidated=@IsInvalidated
                WHERE Id=@Id", param);
        }
    }

    public async Task InvalidateReconciliationsAfterDate(int accountId, DateTime date)
    {
        // using var conn = _db.GetConnection();
        // conn.Execute(@"
        //     UPDATE AccountReconciliations
        //     SET IsInvalidated = 1
        //     WHERE AccountId = @accountId AND ReconciledAsOfDate >= @date",
        //     new { accountId, date = date.ToString("yyyy-MM-dd") });
        
        foreach (var r in GetInvalidateReconciliationsAfterDate(accountId, date)) {
            await DeleteAccountReconciliationAsync(r);
        }
    }

    public bool WillInvalidateReconciliationsAfterDate(int accountId, DateTime date, List<int> reconciliationsToIgnore = null)
    {
        // Ensure we have at least an empty list to avoid Dapper mapping errors
        reconciliationsToIgnore ??= new List<int>();

        using var conn = _db.GetConnection();
    
        // Use a conditional or a dummy value if the list is empty
        string sql = @"
        SELECT Count(*) From AccountReconciliations
        WHERE AccountId = @accountId 
        AND ReconciledAsOfDate >= @date";

        // Only add the NOT IN clause if there are items to ignore
        if (reconciliationsToIgnore.Any())
        {
            sql += " AND Id NOT IN @reconciliationsToIgnore";
        }

        var recordsImpacted = conn.ExecuteScalar<int>(sql, 
            new { accountId, date = date.ToString("yyyy-MM-dd"), reconciliationsToIgnore });

        return recordsImpacted > 0;
    }
    
    public List<int> GetInvalidateReconciliationsAfterDate(int accountId, DateTime date, List<int> reconciliationsToIgnore = null)
    {
        // Ensure we have at least an empty list to avoid Dapper mapping errors
        reconciliationsToIgnore ??= new List<int>();
        
        using var conn = _db.GetConnection();
        
        // Use a conditional or a dummy value if the list is empty
        string sql = @"
        SELECT Id From AccountReconciliations
        WHERE AccountId = @accountId 
        AND ReconciledAsOfDate >= @date";

        // Only add the NOT IN clause if there are items to ignore
        if (reconciliationsToIgnore.Any())
        {
            sql += " AND Id NOT IN @reconciliationsToIgnore";
        }
        
        var reconciliations = conn.Query<int>(
            sql,
            new { accountId, date = date.ToString("yyyy-MM-dd"), reconciliationsToIgnore }).ToList();
        
        return reconciliations;
    }
    
    public async Task DeleteAccountReconciliationAsync(int id)
    {
        using var conn = _db.GetConnection();

        // First, clear any transaction references to this reconciliation
        conn.ExecuteAsync(@"
            UPDATE Transactions
            SET FromAccountReconciledId = NULL
            WHERE FromAccountReconciledId = @id", new { id });

        conn.ExecuteAsync(@"
            UPDATE Transactions
            SET ToAccountReconciledId = NULL
            WHERE ToAccountReconciledId = @id", new { id });

        // Then delete the reconciliation
        conn.ExecuteAsync("DELETE FROM AccountReconciliations WHERE Id = @id", new { id });
    }
}
