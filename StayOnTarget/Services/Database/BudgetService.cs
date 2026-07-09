using StayOnTarget.Data;

namespace StayOnTarget.Services;

public partial class BudgetService
{
    private readonly DatabaseContext _db;

    public BudgetService()
    {
        _db = new DatabaseContext();
    }

    public BudgetService(string dbPath)
    {
        _db = new DatabaseContext(dbPath);
    }
    
    public string BackupDatabase()
    {
        return _db.BackupDatabase();
    }
}
