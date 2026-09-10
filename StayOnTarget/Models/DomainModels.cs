using System.ComponentModel.DataAnnotations;

namespace StayOnTarget.Models;

public enum TransactionType
{
    Income,
    Expense,
    [Display(Name = "Loan Payment")]
    LoanPayment,
    Investment
}

public enum Frequency
{
    Monthly,
    Yearly,
    [Display(Name = "Bi-Weekly")]
    BiWeekly,
    Weekly,
    Once,
    [Display(Name = "Semi-Monthly")]
    SemiMonthly,
    Quarterly,
    Annual,
    [Display(Name = "Every Four Weeks")]
    EveryFourWeeks
}

public enum AccountType
{
    Checking,
    Savings,
    Investment,
    CD,
    [Display(Name = "Retirement 401k")]
    Retirement401k,
    Brokerage,
    Mortgage,
    [Display(Name = "Personal Loan")]
    PersonalLoan,
    [Display(Name = "Credit Card")]
    CreditCard,
    [Display(Name = "Real Estate")]
    RealEstate, 
    [Display(Name = "Appreciating Asset")]
    AppreciatingAsset,
    Auto,
    [Display(Name = "College Fund")]
    CollegeFund,// 529 / Coverdell
    Cash,
    IRA,
    [Display(Name = "Roth IRA")]
    RothIRA,
    [Display(Name = "Roth 401k")]
    Roth401k,
    HSA,
    FDA,
    Pension,
    [Display(Name = "Digital Asset")]
    DigitalAsset,
    [Display(Name = "Other Asset")]
    OtherAsset,
    [Display(Name = "Other Liability")]
    OtherLiability,
    HELOC,
    [Display(Name = "Student Loan")]
    StudentLoan,
    [Display(Name = "Rental Property")]
    RentalProperty,
    Business
}
