using StayOnTarget.ViewModels;

namespace StayOnTarget.Models;

public class Paycheck : ViewModelBase
{
    private string _name = "Regular Paycheck";
    private decimal _expectedAmount;
    private Frequency _frequency = Frequency.BiWeekly;
    private DateTime _startDate = DateTime.Today;
    private DateTime? _endDate;
    private int? _accountId;
    private bool _isBalanced;

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

    public DateTime StartDate
    {
        get => _startDate;
        set => SetProperty(ref _startDate, value);
    }

    public DateTime? EndDate
    {
        get => _endDate;
        set => SetProperty(ref _endDate, value);
    }

    public int? AccountId
    {
        get => _accountId;
        set => SetProperty(ref _accountId, value);
    }

    public bool IsBalanced
    {
        get => _isBalanced;
        set => SetProperty(ref _isBalanced, value);
    }
}