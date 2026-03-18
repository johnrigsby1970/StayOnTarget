using Dapper;
using StayOnTarget.Models;

namespace StayOnTarget.Services;

public partial class BudgetService
{
    public IEnumerable<AccountAprHistory> GetAccountAprHistories(int accountId)
    {
        using var conn = _db.GetConnection();
        return conn.Query<AccountAprHistory>(@"
            SELECT aah.*
            FROM AccountAprHistory aah
            WHERE aah.AccountId = @accountId", new { accountId });
    }
    
    public void UpsertAccountAprHistory(AccountAprHistory aah)
    {
        using var conn = _db.GetConnection();
        
        var param = new
        {
            aah.Id,
            aah.AccountId,
            AsOfDate = aah.AsOfDate.ToString("yyyy-MM-dd"),
            aah.AnnualPercentageRate,
            aah.CashAdvanceRate,
            aah.BalanceTransferRate
        };
        if (aah.Id == 0)
        {
            conn.Execute(@"INSERT INTO AccountAprHistory (AccountId, AsOfDate, AnnualPercentageRate, CashAdvanceRate, BalanceTransferRate)
                           VALUES (@AccountId, @AsOfDate, @AnnualPercentageRate, @CashAdvanceRate, @BalanceTransferRate)", param);
        }
        else
        {
            conn.Execute(@"UPDATE AccountAprHistory SET AccountId=@AccountId, AsOfDate=@AsOfDate, AnnualPercentageRate=@AnnualPercentageRate,
                           CashAdvanceRate=@CashAdvanceRate, BalanceTransferRate=@BalanceTransferRate WHERE Id=@Id", param);
        }
    }

    public void AccountAprHistory(int id)
    {
        using var conn = _db.GetConnection();
        conn.Execute("DELETE FROM AccountAprHistory WHERE Id = @id", new { id });
    }
    
    public void DeleteAccountAprHistory(int id)
    {
        using var conn = _db.GetConnection();
        
        // Then delete the reconciliation
        conn.Execute("DELETE FROM AccountAprHistory WHERE Id = @id", new { id });
    }
}