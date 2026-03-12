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

    public void UpsertTransaction(Transaction t)
    {
        using var conn = _db.GetConnection();
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
            PaycheckOccurrenceDate = t.PaycheckOccurrenceDate?.ToString("yyyy-MM-dd")
        };
        if (t.Id == 0)
        {
            conn.Execute(@"INSERT INTO Transactions (Description, Amount, Date, AccountId, ToAccountId, BillId, BucketId, PeriodDate, IsPrincipalOnly, FitId, PaycheckId, PaycheckOccurrenceDate) 
                           VALUES (@Description, @Amount, @Date, @AccountId, @ToAccountId, @BillId, @BucketId, @PeriodDate, @IsPrincipalOnly, @FitId, @PaycheckId, @PaycheckOccurrenceDate)", param);
        }
        else
        {
            conn.Execute(@"UPDATE Transactions SET Description=@Description, Amount=@Amount, Date=@Date, 
                           AccountId=@AccountId, ToAccountId=@ToAccountId, BillId=@BillId, BucketId=@BucketId, PeriodDate=@PeriodDate, IsPrincipalOnly=@IsPrincipalOnly, PaycheckId=@PaycheckId, PaycheckOccurrenceDate=@PaycheckOccurrenceDate WHERE Id=@Id", param);
        }
    }

    public void DeleteTransaction(int id)
    {
        using var conn = _db.GetConnection();
        conn.Execute("DELETE FROM Transactions WHERE Id = @id", new { id });
    }
}