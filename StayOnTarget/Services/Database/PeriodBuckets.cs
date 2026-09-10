using Dapper;
using StayOnTarget.Models;
using Serilog;

namespace StayOnTarget.Services;

public partial class BudgetService
{
    public async Task<IEnumerable<PeriodBucket>> GetPeriodBucketsAsync(DateTime periodDate)
    {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();
    
            return await conn.QueryAsync<PeriodBucket>(@"
            SELECT 
                pb.*, 
                b.Name AS BucketName, 
                b.Type AS BucketType 
            FROM PeriodBuckets pb 
            JOIN Buckets b ON pb.BucketId = b.Id 
            WHERE DATE(pb.PeriodDate) = DATE(@periodDate)", 
                new { periodDate = periodDate.ToString("yyyy-MM-dd") });
        }
        catch (Exception ex) {
            Log.Error(ex, "Error getting period buckets for date {PeriodDate}.", periodDate);
            return Enumerable.Empty<PeriodBucket>();
        }
    }
    
    
    public async Task<IEnumerable<PeriodBucket>> GetPeriodBucketsIncludingMonthlyAsync(DateTime periodDate)
    {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();
    
            var month = new DateTime(periodDate.Year, periodDate.Month, 1);
            return await conn.QueryAsync<PeriodBucket>(@"
                SELECT pb.*, b.Name as BucketName , 
                b.Type AS BucketType 
                FROM PeriodBuckets pb 
                JOIN Buckets b ON pb.BucketId = b.Id 
                WHERE pb.PeriodDate = @periodDate OR pb.PeriodDate = @month", 
                new { periodDate = periodDate.ToString("yyyy-MM-dd"), month = month.ToString("yyyy-MM-dd") });
        }
        catch (Exception ex) {
            Log.Error(ex, "Error getting period buckets including monthly for date {PeriodDate}.", periodDate);
            return Enumerable.Empty<PeriodBucket>();
        }
    }
    
    
    public async Task<IEnumerable<PeriodBucket>> GetAllPeriodBucketsAsync()
    {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();
    
            return await conn.QueryAsync<PeriodBucket>(@"
                SELECT pb.*, b.Name as BucketName , 
                b.Type AS BucketType 
                FROM PeriodBuckets pb 
                JOIN Buckets b ON pb.BucketId = b.Id");
        }
        catch (Exception ex) {
            Log.Error(ex, "Error getting all period buckets.");
            return Enumerable.Empty<PeriodBucket>();
        }
    }
    
    // public async Task<IEnumerable<PeriodBucket>> GetAllPeriodBucketsAsync()
    // {
    //     try {
    //         await using var conn = _db.GetConnection();
    //         await conn.OpenAsync();
    //
    //         const string sql = @"
    //         SELECT 
    //             pb.Id,
    //             pb.BucketId,
    //             b.Name AS BucketName,
    //             b.Type AS BucketType,
    //             pb.PeriodDate,
    //             COALESCE(
    //                 pb.ActualAmount,
    //                 (
    //                     SELECT h.ExpectedAmount 
    //                     FROM BucketTargetHistory h 
    //                     WHERE h.BucketId = b.Id 
    //                       AND h.EffectiveStartDate <= date(pb.PeriodDate, '+14 days') 
    //                     ORDER BY h.EffectiveStartDate DESC 
    //                     LIMIT 1
    //                 ),
    //                 b.ExpectedAmount
    //             ) AS ActualAmount,
    //             pb.IsPaid,
    //             pb.FitId
    //         FROM PeriodBuckets pb 
    //         JOIN Buckets b ON pb.BucketId = b.Id;";
    //
    //         return await conn.QueryAsync<PeriodBucket>(sql);
    //     }
    //     catch (Exception ex) {
    //         Log.Error(ex, "Error getting all period buckets.");
    //         return Enumerable.Empty<PeriodBucket>();
    //     }
    // }


    public async Task UpsertPeriodBucketAsync(PeriodBucket pb)
    {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();

            var param = new
            {
                pb.Id,
                pb.BucketId,
                PeriodDate = pb.PeriodDate.ToString("yyyy-MM-dd"),
                pb.ActualAmount,
                IsPaid = pb.IsPaid ? 1 : 0,
                FitId = pb.FitId.ToString()
            };

            if (pb.Id == 0)
            {
                pb.Id = await conn.ExecuteScalarAsync<int>(@"
                    INSERT INTO PeriodBuckets (BucketId, PeriodDate, ActualAmount, IsPaid, FitId) 
                    VALUES (@BucketId, @PeriodDate, @ActualAmount, @IsPaid, @FitId);
                SELECT last_insert_rowid();", param);
            }
            else
            {
                await conn.ExecuteAsync(@"
                    UPDATE PeriodBuckets 
                    SET BucketId = @BucketId, 
                        PeriodDate = @PeriodDate, 
                        ActualAmount = @ActualAmount, 
                        IsPaid = @IsPaid,
                        FitId = @FitId 
                    WHERE Id = @Id", param);
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error upserting period bucket with ID {PeriodBucketId}.", pb.Id);
            throw;
        }
    }

    public async Task DeletePeriodBucketAsync(int id)
    {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();

            await conn.ExecuteAsync("DELETE FROM PeriodBuckets WHERE Id = @id", new { id });
        }
        catch (Exception ex) {
            Log.Error(ex, "Error deleting period bucket with ID {PeriodBucketId}.", id);
            throw;
        }
    }
}