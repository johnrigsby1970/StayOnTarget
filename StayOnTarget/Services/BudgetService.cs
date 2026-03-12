using StayOnTarget.Data;

namespace StayOnTarget.Services;

public partial class BudgetService
{
    private readonly DatabaseContext _db;

    public BudgetService()
    {
        _db = new DatabaseContext();
    }
}
