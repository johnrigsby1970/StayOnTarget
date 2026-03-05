namespace MyBudget.Models;

public enum TransactionType
{
    Income,
    Expense,
    LoanPayment,
    Investment
}

public enum Frequency
{
    Monthly,
    Yearly,
    BiWeekly,
    Weekly,
    Once
}

public enum AccountType
{
    Checking,
    Savings,
    Investment,
    CD,
    Retirement401k,
    Brokerage,
    Mortgage,
    PersonalLoan,
    CreditCard
}
