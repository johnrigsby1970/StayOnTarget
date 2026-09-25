using Dapper;
using StayOnTarget.Models;
using Serilog;

namespace StayOnTarget.Services;

public partial class BudgetService
{
    public async Task<IEnumerable<PeriodBill>> GetPeriodBillsAsync(DateTime periodDate, DateTime nextPeriodDate)
    {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();

            return await conn.QueryAsync<PeriodBill>(@"
                SELECT pb.*, b.Name as BillName 
                FROM PeriodBills pb 
                JOIN Bills b ON pb.BillId = b.Id 
                WHERE pb.PeriodDate = @periodDate OR (pb.DueDate>=@periodDate AND pb.DueDate<@nextPeriodDate)", new { periodDate = periodDate.ToString("yyyy-MM-dd") , nextPeriodDate = nextPeriodDate.ToString("yyyy-MM-dd")});
        }
        catch (Exception ex) {
            Log.Error(ex, "Error getting period bills for date {PeriodDate}.", periodDate);
            return Enumerable.Empty<PeriodBill>();
        }
    }

    public async Task<IEnumerable<PeriodBill>> GetAllPeriodBillsAsync()
    {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();

            return await conn.QueryAsync<PeriodBill>(@"
                SELECT pb.*, b.Name as BillName 
                FROM PeriodBills pb 
                JOIN Bills b ON pb.BillId = b.Id");
        }
        catch (Exception ex) {
            Log.Error(ex, "Error getting all period bills.");
            return Enumerable.Empty<PeriodBill>();
        }
    }

    public async Task UpsertPeriodBillAsync(PeriodBill pb)
    {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();

            var param = new
            {
                pb.Id,
                pb.BillId,
                PeriodDate = pb.PeriodDate.ToString("yyyy-MM-dd"),
                DueDate = pb.DueDate.ToString("yyyy-MM-dd"),
                pb.ActualAmount,
                IsPaid = pb.IsPaid ? 1 : 0,
                FitId = pb.FitId.ToString()
            };

            if (pb.Id == 0)
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO PeriodBills (BillId, PeriodDate, DueDate, ActualAmount, IsPaid, FitId) 
                    VALUES (@BillId, @PeriodDate, @DueDate, @ActualAmount, @IsPaid, @FitId)", param);
            }
            else
            {
                await conn.ExecuteAsync(@"
                    UPDATE PeriodBills 
                    SET BillId = @BillId, 
                        PeriodDate = @PeriodDate, 
                        DueDate = @DueDate, 
                        ActualAmount = @ActualAmount, 
                        IsPaid = @IsPaid,
                        FitId = @FitId
                    WHERE Id = @Id", param);
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error upserting period bill with ID {PeriodBillId}.", pb.Id);
            throw;
        }
    }
    
    public async Task DeletePeriodBillAsync(int id)
    {
        try {
            await using var conn = _db.GetConnection();
            await conn.OpenAsync();

            await conn.ExecuteAsync("DELETE FROM PeriodBills WHERE Id = @id AND IsPaid = 0", new { id });
        }
        catch (Exception ex) {
            Log.Error(ex, "Error deleting period bill with ID {PeriodBillId}.", id);
            throw;
        }
    }
}