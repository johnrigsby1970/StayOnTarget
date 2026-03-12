using Dapper;
using StayOnTarget.Models;

namespace StayOnTarget.Services;

public partial class BudgetService
{
    // Bucket Operations
    public IEnumerable<BudgetBucket> GetAllBuckets()
    {
        using var conn = _db.GetConnection();
        return conn.Query<BudgetBucket>("SELECT * FROM Buckets");
    }

    public void UpsertBucket(BudgetBucket bucket)
    {
        using var conn = _db.GetConnection();
        if (bucket.Id == 0)
        {
            conn.Execute(@"INSERT INTO Buckets (Name, ExpectedAmount, AccountId, PaycheckId) 
                           VALUES (@Name, @ExpectedAmount, @AccountId, @PaycheckId)", bucket);
        }
        else
        {
            conn.Execute(@"UPDATE Buckets SET Name=@Name, ExpectedAmount=@ExpectedAmount, AccountId=@AccountId, PaycheckId=@PaycheckId WHERE Id=@Id", bucket);
        }
    }

    public void DeleteBucket(int id)
    {
        using var conn = _db.GetConnection();
        conn.Execute("UPDATE Transactions SET BucketId=null WHERE BucketId = @id", new { id }); //Disassociate the transaction from the bucket
        conn.Execute("DELETE FROM PeriodBuckets WHERE BucketId = @id", new { id });
        conn.Execute("DELETE FROM Buckets WHERE Id = @id", new { id });
    }  
}