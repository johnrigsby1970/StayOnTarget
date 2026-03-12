using Dapper;
using StayOnTarget.Models;

namespace StayOnTarget.Services;

public partial class BudgetService
{
    public IEnumerable<PeriodBucket> GetPeriodBuckets(DateTime periodDate)
    {
        using var conn = _db.GetConnection();
        return conn.Query<PeriodBucket>(@"
            SELECT pb.*, b.Name as BucketName 
            FROM PeriodBuckets pb 
            JOIN Buckets b ON pb.BucketId = b.Id 
            WHERE pb.PeriodDate = @periodDate", new { periodDate = periodDate.ToString("yyyy-MM-dd") });
    }
    
    public IEnumerable<PeriodBucket> GetPeriodBucketsIncludingMonthly(DateTime periodDate)
    {
        using var conn = _db.GetConnection();
        var month = new DateTime(periodDate.Year, periodDate.Month, 1);
        return conn.Query<PeriodBucket>(@"
            SELECT pb.*, b.Name as BucketName 
            FROM PeriodBuckets pb 
            JOIN Buckets b ON pb.BucketId = b.Id 
            WHERE pb.PeriodDate = @periodDate OR pb.PeriodDate = @month", new { periodDate = periodDate.ToString("yyyy-MM-dd"), month = month.ToString("yyyy-MM-dd") });
    }

    public IEnumerable<PeriodBucket> GetAllPeriodBuckets()
    {
        using var conn = _db.GetConnection();
        return conn.Query<PeriodBucket>(@"
            SELECT pb.*, b.Name as BucketName 
            FROM PeriodBuckets pb 
            JOIN Buckets b ON pb.BucketId = b.Id");
    }

    public void UpsertPeriodBucket(PeriodBucket pb)
    {
        using var conn = _db.GetConnection();
        var param = new
        {
            pb.Id,
            pb.BucketId,
            PeriodDate = pb.PeriodDate.ToString("yyyy-MM-dd"),
            pb.ActualAmount,
            pb.IsPaid,
            FitId = pb.FitId.ToString()
        };
        if (pb.Id == 0)
        {
            conn.Execute(@"INSERT INTO PeriodBuckets (BucketId, PeriodDate, ActualAmount, IsPaid, FitId) 
                           VALUES (@BucketId, @PeriodDate, @ActualAmount, @IsPaid, @FitId)", param);
        }
        else
        {
            conn.Execute(@"UPDATE PeriodBuckets SET BucketId=@BucketId, PeriodDate=@PeriodDate, 
                           ActualAmount=@ActualAmount, IsPaid=@IsPaid WHERE Id=@Id", param);
        }
    }

    public void DeletePeriodBucket(int id)
    {
        using var conn = _db.GetConnection();
        conn.Execute("DELETE FROM PeriodBuckets WHERE Id = @id AND IsPaid = 0", new { id });
    }
}