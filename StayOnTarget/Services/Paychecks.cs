using Dapper;
using StayOnTarget.Models;

namespace StayOnTarget.Services;

public partial class BudgetService
{
    public IEnumerable<Paycheck> GetAllPaychecks()
    {
        using var conn = _db.GetConnection();
        return conn.Query<Paycheck>("SELECT * FROM Paychecks");
    }
    
    public void UpsertPaycheck(Paycheck paycheck)
    {
        using var conn = _db.GetConnection();
        var param = new
        {
            paycheck.Id,
            paycheck.Name,
            paycheck.ExpectedAmount,
            paycheck.Frequency,
            StartDate = paycheck.StartDate.ToString("yyyy-MM-dd"),
            EndDate = paycheck.EndDate?.ToString("yyyy-MM-dd"),
            paycheck.AccountId,
            paycheck.IsBalanced
        };
        if (paycheck.Id == 0)
        {
            conn.Execute(@"INSERT INTO Paychecks (Name, ExpectedAmount, Frequency, StartDate, EndDate, AccountId, IsBalanced) 
                           VALUES (@Name, @ExpectedAmount, @Frequency, @StartDate, @EndDate, @AccountId, @IsBalanced)", param);
        }
        else
        {
            conn.Execute(@"UPDATE Paychecks SET Name=@Name, ExpectedAmount=@ExpectedAmount, Frequency=@Frequency, 
                           StartDate=@StartDate, EndDate=@EndDate, AccountId=@AccountId, IsBalanced=@IsBalanced WHERE Id=@Id", param);
        }
    }  
    
    public void DeletePaycheck(int id)
    {
        using var conn = _db.GetConnection();
        conn.Execute("UPDATE Transactions SET PaycheckId=null WHERE PaycheckId = @id", new { id }); //Disassociate the transaction from the paycheck
        conn.Execute("DELETE FROM Paychecks WHERE Id = @id", new { id });
    }
}