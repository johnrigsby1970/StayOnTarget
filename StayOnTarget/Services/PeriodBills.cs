using Dapper;
using StayOnTarget.Models;

namespace StayOnTarget.Services;

public partial class BudgetService{
    public IEnumerable<PeriodBill> GetPeriodBills(DateTime periodDate)
    {
        using var conn = _db.GetConnection();
        return conn.Query<PeriodBill>(@"
            SELECT pb.*, b.Name as BillName 
            FROM PeriodBills pb 
            JOIN Bills b ON pb.BillId = b.Id 
            WHERE pb.PeriodDate = @periodDate", new { periodDate = periodDate.ToString("yyyy-MM-dd") });
    }

    public IEnumerable<PeriodBill> GetAllPeriodBills()
    {
        using var conn = _db.GetConnection();
        return conn.Query<PeriodBill>(@"
            SELECT pb.*, b.Name as BillName 
            FROM PeriodBills pb 
            JOIN Bills b ON pb.BillId = b.Id");
    }

    public void UpsertPeriodBill(PeriodBill pb)
    {
        using var conn = _db.GetConnection();
        var param = new
        {
            pb.Id,
            pb.BillId,
            PeriodDate = pb.PeriodDate.ToString("yyyy-MM-dd"),
            DueDate = pb.DueDate.ToString("yyyy-MM-dd"),
            pb.ActualAmount,
            pb.IsPaid,
            FitId = pb.FitId.ToString()
        };
        if (pb.Id == 0)
        {
            conn.Execute(@"INSERT INTO PeriodBills (BillId, PeriodDate, DueDate, ActualAmount, IsPaid, FitId) 
                           VALUES (@BillId, @PeriodDate, @DueDate, @ActualAmount, @IsPaid, @FitId)", param);
        }
        else
        {
            conn.Execute(@"UPDATE PeriodBills SET BillId=@BillId, PeriodDate=@PeriodDate, DueDate=@DueDate, 
                           ActualAmount=@ActualAmount, IsPaid=@IsPaid WHERE Id=@Id", param);
        }
    }
    
    public void DeletePeriodBill(int id)
    {
        using var conn = _db.GetConnection();
        conn.Execute("DELETE FROM PeriodBills WHERE Id = @id AND IsPaid = 0", new { id });
    }

}