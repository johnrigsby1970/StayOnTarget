using Dapper;
using StayOnTarget.Models;

namespace StayOnTarget.Services;

public partial class BudgetService
{
    public IEnumerable<Bill> GetAllBills()
    {
        using var conn = _db.GetConnection();
        return conn.Query<Bill>("SELECT * FROM Bills WHERE IsActive = 1");
    }

    public void UpsertBill(Bill bill)
    {
        using var conn = _db.GetConnection();
        var param = new
        {
            bill.Id,
            bill.Name,
            bill.ExpectedAmount,
            bill.Frequency,
            bill.DueDay,
            bill.AccountId,
            bill.ToAccountId,
            NextDueDate = bill.NextDueDate?.ToString("yyyy-MM-dd"),
            bill.Category,
            bill.IsActive
        };
        if (bill.Id == 0)
        {
            conn.Execute(@"INSERT INTO Bills (Name, ExpectedAmount, Frequency, DueDay, AccountId, ToAccountId, NextDueDate, Category, IsActive) 
                           VALUES (@Name, @ExpectedAmount, @Frequency, @DueDay, @AccountId, @ToAccountId, @NextDueDate, @Category, @IsActive)", param);
        }
        else
        {
            conn.Execute(@"UPDATE Bills SET Name=@Name, ExpectedAmount=@ExpectedAmount, Frequency=@Frequency, 
                           DueDay=@DueDay, AccountId=@AccountId, ToAccountId=@ToAccountId, NextDueDate=@NextDueDate, Category=@Category, IsActive=@IsActive WHERE Id=@Id", param);
        }
    }   
    
    public void DeleteBill(int id)
    {
        using var conn = _db.GetConnection();
        conn.Execute("UPDATE Transactions SET BillId=null WHERE BillId = @id", new { id }); //Disassociate the transaction from the bill
        conn.Execute("DELETE FROM PeriodBills WHERE BillId = @id", new { id });
        conn.Execute("UPDATE Bills SET IsActive = 0 WHERE Id = @id", new { id });
    }
}