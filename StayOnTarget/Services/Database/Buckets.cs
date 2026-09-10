using System.Data;
using System.Data.Common;
using Dapper;
using Microsoft.Data.Sqlite;
using StayOnTarget.Models;
using Serilog;

namespace StayOnTarget.Services;

public partial class BudgetService {
    // Bucket Operations
    public async Task<IEnumerable<BudgetBucket>> GetAllBucketsAsync(bool includeArchived = false) {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();

            return await conn.QueryAsync<BudgetBucket>(
                @"SELECT * 
                  FROM Buckets 
                  WHERE IsActive = 1 AND (IsArchived = 0 OR @includeArchived = 1) 
                  ORDER BY Name",
                new { includeArchived = includeArchived ? 1 : 0 });
        }
        catch (Exception ex) {
            Log.Error(ex, "Error getting all buckets[cite: 19].");
            return Enumerable.Empty<BudgetBucket>();
        }
    }

    public async Task UpsertBucketAsync(BudgetBucket bucket, List<int>? subCategoryIds) {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();
            using var tx = conn.BeginTransaction();

            try {
                if (bucket.Id == 0) {
                    bucket.Id = await conn.ExecuteScalarAsync<int>(@"
                        INSERT INTO Buckets (
                            Name, ExpectedAmount, AccountId, Type, 
                            TargetBalance, CurrentBalance, InitialBalance,
                            TargetFrequency, TargetAmount, NextDueDate, Overrides
                        ) 
                        VALUES (
                            @Name, @ExpectedAmount, @AccountId, @Type, 
                            @TargetBalance, @CurrentBalance, @InitialBalance,
                            @TargetFrequency, @TargetAmount, @NextDueDate, @Overrides
                        );
                        SELECT last_insert_rowid();",
                        new {
                            bucket.Name,
                            bucket.ExpectedAmount,
                            bucket.AccountId,
                            Type = (int)bucket.Type,
                            bucket.TargetBalance,
                            bucket.CurrentBalance,
                            bucket.InitialBalance,
                            TargetFrequency = bucket.TargetFrequency.HasValue
                                ? (int?)bucket.TargetFrequency.Value
                                : null,
                            bucket.TargetAmount,
                            NextDueDate = bucket.NextDueDate?.ToString("yyyy-MM-dd"),
                            Overrides = bucket.Overrides
                        }, tx);
                }
                else {
                    await conn.ExecuteAsync(@"
                        UPDATE Buckets 
                        SET Name = @Name, 
                            ExpectedAmount = @ExpectedAmount, 
                            AccountId = @AccountId, 
                            Type = @Type,
                            TargetBalance = @TargetBalance,
                            CurrentBalance = @CurrentBalance,
                            InitialBalance = @InitialBalance,
                            TargetFrequency = @TargetFrequency,
                            TargetAmount = @TargetAmount,
                            NextDueDate = @NextDueDate,
                            Overrides = @Overrides
                        WHERE Id = @Id",
                        new {
                            bucket.Id,
                            bucket.Name,
                            bucket.ExpectedAmount,
                            bucket.AccountId,
                            Type = (int)bucket.Type,
                            bucket.TargetBalance,
                            bucket.CurrentBalance,
                            bucket.InitialBalance,
                            TargetFrequency = bucket.TargetFrequency.HasValue
                                ? (int?)bucket.TargetFrequency.Value
                                : null,
                            bucket.TargetAmount,
                            NextDueDate = bucket.NextDueDate?.ToString("yyyy-MM-dd"),
                            Overrides = bucket.Overrides
                        }, tx);
                }


                if (subCategoryIds != null) {
                    await conn.ExecuteAsync(
                        "UPDATE SubCategories SET DefaultBucketId = NULL WHERE DefaultBucketId = @BucketId",
                        new { BucketId = bucket.Id }, tx);

                    if (subCategoryIds.Any()) {
                        await conn.ExecuteAsync(
                            "UPDATE SubCategories SET DefaultBucketId = @BucketId WHERE Id IN @Ids",
                            new { BucketId = bucket.Id, Ids = subCategoryIds }, tx);
                    }
                }

                tx.Commit();
            }
            catch {
                tx.Rollback();
                throw;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error upserting bucket with ID {BucketId}[cite: 19].", bucket.Id);
            throw;
        }
    }

    // Bucket Paycheck Allocation Operations
    public async Task SaveBucketPaycheckAllocationsAsync(int bucketId, BucketType bucketType,
        IEnumerable<BucketPaycheckAllocation> allocations) {
        try {
            if (bucketType == BucketType.UpfrontFloor) {
                throw new InvalidOperationException(
                    "UpfrontFloor buckets represent static reserve balances and cannot be linked to paycheck allocations[cite: 19].");
            }

            await using var conn = _db.GetConnection();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            try {
                await conn.ExecuteAsync("DELETE FROM BucketPaycheckAllocations WHERE BucketId = @bucketId",
                    new { bucketId }, tx);

                const string sql = @"
                    INSERT INTO BucketPaycheckAllocations (BucketId, PaycheckId, AllocationType, AllocationValue, SortOrder, IsActive)
                    VALUES (@BucketId, @PaycheckId, @AllocationType, @AllocationValue, @SortOrder, @IsActive);";

                foreach (var alloc in allocations.Where(x=>x.AllocationValue>0)) {
                    await conn.ExecuteAsync(sql, new {
                        BucketId = bucketId,
                        alloc.PaycheckId,
                        alloc.AllocationType,
                        alloc.AllocationValue,
                        alloc.SortOrder,
                        IsActive = alloc.IsActive ? 1 : 0
                    }, tx);
                }

                await tx.CommitAsync();
            }
            catch {
                await tx.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error saving bucket paycheck allocations for bucket ID {BucketId}[cite: 19].", bucketId);
            throw;
        }
    }

    public async Task<IEnumerable<BucketPaycheckAllocation>> GetAllAllocationsAsync() {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();

            return await conn.QueryAsync<BucketPaycheckAllocation>(@"
            SELECT AllocationId, BucketId, PaycheckId, AllocationType, AllocationValue, SortOrder, IsActive
            FROM BucketPaycheckAllocations
            WHERE IsActive = 1");
        }
        catch (Exception ex) {
            Log.Error(ex, "Error fetching all bucket allocations.");
            return Enumerable.Empty<BucketPaycheckAllocation>();
        }
    }

    public async Task<IEnumerable<BucketPaycheckAllocation>> GetAllocationsForBucketAsync(int bucketId) {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();

            return await conn.QueryAsync<BucketPaycheckAllocation>(@"
                SELECT AllocationId, BucketId, PaycheckId, AllocationType, AllocationValue, SortOrder, IsActive, CreatedDate
                     , Paychecks.Name as PaycheckName
                FROM BucketPaycheckAllocations
                LEFT OUTER JOIN Paychecks ON BucketPaycheckAllocations.PaycheckId = Paychecks.Id
                WHERE BucketId = @bucketId AND IsActive = 1
                ORDER BY SortOrder ASC", new { bucketId });
        }
        catch (Exception ex) {
            Log.Error(ex, "Error getting allocations for bucket ID {BucketId}[cite: 19].", bucketId);
            return Enumerable.Empty<BucketPaycheckAllocation>();
        }
    }

    public async Task ApplyDrawdownAsync(int bucketId, decimal amount, IDbTransaction? tx = null) {
        try {
            var conn = tx?.Connection ?? _db.GetConnection();
            bool isLocalConn = tx == null;

            try {
                if (isLocalConn && conn is DbConnection dbConn) {
                    if (dbConn.State != ConnectionState.Open) {
                        await dbConn.OpenAsync();
                    }
                }
                else if (isLocalConn && conn.State != ConnectionState.Open) {
                    conn.Open();
                }

                await conn.ExecuteAsync(@"
                UPDATE Buckets 
                SET CurrentBalance = CurrentBalance - @amount 
                WHERE Id = @bucketId AND Type = 2",
                    new { bucketId, amount }, tx);
            }
            finally {
                if (isLocalConn) {
                    if (conn is IAsyncDisposable asyncDisposable) {
                        await asyncDisposable.DisposeAsync();
                    }
                    else {
                        conn.Dispose();
                    }
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error applying drawdown for bucket ID {BucketId}[cite: 19].", bucketId);
        }
    }

    public async Task ArchiveBucketAsync(int id) {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();
            await conn.ExecuteAsync(@"UPDATE Buckets SET IsArchived = 1 WHERE Id = @id", new { id });
        }
        catch (Exception ex) {
            Log.Error(ex, "Error archiving bucket with ID {BucketId}[cite: 19].", id);
            throw;
        }
    }

    public async Task UnArchiveBucketAsync(int id) {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();
            await conn.ExecuteAsync(@"UPDATE Buckets SET IsArchived = 0 WHERE Id = @id", new { id });
        }
        catch (Exception ex) {
            Log.Error(ex, "Error unarchiving bucket with ID {BucketId}[cite: 19].", id);
            throw;
        }
    }

    public async Task SetBucketInactiveAsync(int id) {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();
            await conn.ExecuteAsync("UPDATE Buckets SET IsActive = 0 WHERE Id = @id", new { id });
        }
        catch (Exception ex) {
            Log.Error(ex, "Error setting bucket inactive for ID {BucketId}[cite: 19].", id);
            throw;
        }
    }

    public async Task DeleteBucketAsync(int id) {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();
            await using var tx = conn.BeginTransaction();

            try {
                if (await IsBucketInUseAsync(id, conn, tx)) {
                    await conn.ExecuteAsync("UPDATE Buckets SET IsArchived = 1 WHERE Id = @id", new { id }, tx);
                }
                else {
                    await conn.ExecuteAsync("DELETE FROM BucketPaycheckAllocations WHERE BucketId = @id", new { id },
                        tx);
                    await conn.ExecuteAsync("DELETE FROM Buckets WHERE Id = @id", new { id }, tx);
                }

                await tx.CommitAsync();
            }
            catch {
                await tx.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error deleting bucket with ID {BucketId}[cite: 19].", id);
            throw;
        }
    }

    public async Task<bool> IsBucketInUseAsync(int bucketId, SqliteConnection? cn = null, IDbTransaction? tx = null) {
        try {
            cn ??= tx?.Connection as SqliteConnection;
            bool isLocalConn = cn == null;
            var conn = cn ?? _db.GetConnection();

            try {
                if (isLocalConn && conn.State != ConnectionState.Open) {
                    await conn.OpenAsync();
                }

                var periodBuckets = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM PeriodBuckets WHERE BucketId = @bucketId",
                    new { bucketId }, tx);
                if (periodBuckets > 0) return true;

                var transactions = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM Transactions WHERE BucketId = @bucketId",
                    new { bucketId }, tx);
                if (transactions > 0) return true;

                return false;
            }
            finally {
                if (isLocalConn) {
                    await conn.DisposeAsync();
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error checking if bucket ID {BucketId} is in use[cite: 19].", bucketId);
            return false;
        }
    }

    public async Task FundPeriodBucketAsync(int bucketId, DateTime periodDate, decimal amount) {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            try {
                await conn.ExecuteAsync(@"
                INSERT INTO PeriodBuckets (BucketId, PeriodDate, ActualAmount, IsPaid)
                VALUES (@bucketId, @periodDate, @amount, 1)
                ON CONFLICT(BucketId, PeriodDate) DO UPDATE SET
                    ActualAmount = @amount,
                    IsPaid = 1",
                    new { bucketId, periodDate = periodDate.ToString("yyyy-MM-dd"), amount }, tx);

                await RecalculateBucketBalanceAsync(bucketId, tx);

                await tx.CommitAsync();
            }
            catch {
                await tx.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error funding period bucket for bucket ID {BucketId}[cite: 19].", bucketId);
            throw;
        }
    }
}