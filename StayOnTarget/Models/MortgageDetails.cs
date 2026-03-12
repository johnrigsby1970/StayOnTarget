using StayOnTarget.ViewModels;

namespace StayOnTarget.Models;

public class MortgageDetails : ViewModelBase
{
    private decimal _interestRate;
    private decimal _escrow;
    private decimal _mortgageInsurance;
    private decimal _loanPayment;
    private DateTime _paymentDate = DateTime.Today;

    public int Id { get; set; }
    public int AccountId { get; set; }

    public decimal InterestRate
    {
        get => _interestRate;
        set => SetProperty(ref _interestRate, value);
    }

    public decimal Escrow
    {
        get => _escrow;
        set => SetProperty(ref _escrow, value);
    }

    public decimal MortgageInsurance
    {
        get => _mortgageInsurance;
        set => SetProperty(ref _mortgageInsurance, value);
    }

    public decimal LoanPayment
    {
        get => _loanPayment;
        set => SetProperty(ref _loanPayment, value);
    }

    public DateTime PaymentDate
    {
        get => _paymentDate;
        set => SetProperty(ref _paymentDate, value);
    }
}