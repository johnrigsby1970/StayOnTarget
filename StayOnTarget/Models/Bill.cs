using StayOnTarget.ViewModels;

namespace StayOnTarget.Models;

public class Bill : ViewModelBase
{
    private string _name = string.Empty;
    private decimal _expectedAmount;
    private Frequency _frequency = Frequency.Monthly;
    private int _dueDay;
    private int? _accountId;
    private int? _toAccountId;
    private DateTime? _nextDueDate;
    private string _category = string.Empty;
    private bool _isActive = true;

    public int Id { get; set; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public decimal ExpectedAmount
    {
        get => _expectedAmount;
        set => SetProperty(ref _expectedAmount, value);
    }

    public Frequency Frequency
    {
        get => _frequency;
        set => SetProperty(ref _frequency, value);
    }

    public int DueDay
    {
        get => _dueDay;
        set => SetProperty(ref _dueDay, value);
    }

    public int? AccountId
    {
        get => _accountId;
        set => SetProperty(ref _accountId, value);
    }

    public int? ToAccountId
    {
        get => _toAccountId;
        set => SetProperty(ref _toAccountId, value);
    }

    public DateTime? NextDueDate
    {
        get => _nextDueDate;
        set => SetProperty(ref _nextDueDate, value);
    }

    public string Category
    {
        get => _category;
        set => SetProperty(ref _category, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }
}