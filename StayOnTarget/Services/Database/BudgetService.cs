using StayOnTarget.Data;

namespace StayOnTarget.Services;

public partial class BudgetService
{
    private readonly DatabaseContext _db;
    private readonly string _password;

    public BudgetService(string password) : this(DatabaseContext.GetDefaultDbPath(), password)
    {
    }

    public BudgetService(string dbPath, string password)
    {
        _password = password;
        _db = new DatabaseContext(dbPath, password);
    }
    
    public string BackupDatabase()
    {
        return _db.BackupDatabase(_password);
    }
}
