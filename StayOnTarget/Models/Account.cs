using StayOnTarget.ViewModels;

namespace StayOnTarget.Models;

public class Account : ViewModelBase
{
    private string _name = string.Empty;
    private string _bankName = string.Empty;
    private decimal _balance;
    private DateTime _balanceAsOf = new DateTime(2026, 2, 19);
    private decimal _annualGrowthRate;
    private bool _includeInTotal = true;
    private AccountType _type = AccountType.Checking;
    private string _hexColor = "#FF0000FF"; // Default to Blue
    private MortgageDetails? _mortgageDetails;
    private CreditCardDetails? _creditCardDetails;

    public int Id { get; set; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string BankName
    {
        get => _bankName;
        set => SetProperty(ref _bankName, value);
    }

    public decimal Balance
    {
        get => _balance;
        set => SetProperty(ref _balance, value);
    }

    public DateTime BalanceAsOf
    {
        get => _balanceAsOf;
        set => SetProperty(ref _balanceAsOf, value);
    }

    public decimal AnnualGrowthRate
    {
        get => _annualGrowthRate;
        set => SetProperty(ref _annualGrowthRate, value);
    }

    public bool IncludeInTotal
    {
        get => _includeInTotal;
        set => SetProperty(ref _includeInTotal, value);
    }

    public AccountType Type
    {
        get => _type;
        set => SetProperty(ref _type, value);
    }

    public string HexColor
    {
        get => _hexColor;
        set => SetProperty(ref _hexColor, value);
    }

    public MortgageDetails? MortgageDetails
    {
        get => _mortgageDetails;
        set => SetProperty(ref _mortgageDetails, value);
    }

    public CreditCardDetails? CreditCardDetails
    {
        get => _creditCardDetails;
        set => SetProperty(ref _creditCardDetails, value);
    }
}