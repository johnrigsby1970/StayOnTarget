using Dapper;
using StayOnTarget.Models;

namespace StayOnTarget.Services;

public partial class BudgetService
{
    public IEnumerable<Account> GetAllAccounts()
    {
        using var conn = _db.GetConnection();
        var accounts = conn.Query<Account>("SELECT * FROM Accounts").ToList();
        foreach (var acc in accounts)
        {
            if (acc.Type == AccountType.Mortgage)
            {
                acc.MortgageDetails = conn.QueryFirstOrDefault<MortgageDetails>("SELECT * FROM MortgageDetails WHERE AccountId = @Id", new { acc.Id });
            }
            if (acc.Type == AccountType.CreditCard)
            {
                acc.CreditCardDetails = conn.QueryFirstOrDefault<CreditCardDetails>("SELECT * FROM CreditCardDetails WHERE AccountId = @Id", new { acc.Id });
            }
        }
        return accounts;
    }

    public void UpsertAccount(Account account)
    {
        using var conn = _db.GetConnection();
        var accountParam = new
        {
            account.Id,
            account.Name,
            account.BankName,
            account.Balance,
            BalanceAsOf = account.BalanceAsOf.ToString("yyyy-MM-dd"),
            account.AnnualGrowthRate,
            account.IncludeInTotal,
            account.Type,
            account.HexColor
        };

        if (account.Id == 0)
        {
            account.Id = conn.ExecuteScalar<int>(@"INSERT INTO Accounts (Name, BankName, Balance, BalanceAsOf, AnnualGrowthRate, IncludeInTotal, Type, HexColor) 
                           VALUES (@Name, @BankName, @Balance, @BalanceAsOf, @AnnualGrowthRate, @IncludeInTotal, @Type, @HexColor);
                           SELECT last_insert_rowid();", accountParam);
        }
        else
        {
            conn.Execute(@"UPDATE Accounts SET Name=@Name, BankName=@BankName, Balance=@Balance, BalanceAsOf=@BalanceAsOf,
                           AnnualGrowthRate=@AnnualGrowthRate, IncludeInTotal=@IncludeInTotal, Type=@Type, HexColor=@HexColor WHERE Id=@Id", accountParam);
        }

        if (account.Type == AccountType.Mortgage && account.MortgageDetails != null)
        {
            account.MortgageDetails.AccountId = account.Id;
            var mdParam = new
            {
                account.MortgageDetails.Id,
                account.MortgageDetails.AccountId,
                account.MortgageDetails.InterestRate,
                account.MortgageDetails.Escrow,
                account.MortgageDetails.MortgageInsurance,
                account.MortgageDetails.LoanPayment,
                PaymentDate = account.MortgageDetails.PaymentDate.ToString("yyyy-MM-dd")
            };
            if (account.MortgageDetails.Id == 0)
            {
                conn.Execute(@"INSERT INTO MortgageDetails (AccountId, InterestRate, Escrow, MortgageInsurance, LoanPayment, PaymentDate) 
                               VALUES (@AccountId, @InterestRate, @Escrow, @MortgageInsurance, @LoanPayment, @PaymentDate)", mdParam);
            }
            else
            {
                conn.Execute(@"UPDATE MortgageDetails SET InterestRate=@InterestRate, Escrow=@Escrow, 
                               MortgageInsurance=@MortgageInsurance, LoanPayment=@LoanPayment, PaymentDate=@PaymentDate WHERE Id=@Id", mdParam);
            }
        }

        if (account.Type == AccountType.CreditCard && account.CreditCardDetails != null)
        {
            account.CreditCardDetails.AccountId = account.Id;
            var ccdParam = new
            {
                account.CreditCardDetails.Id,
                account.CreditCardDetails.AccountId,
                account.CreditCardDetails.Apr,
                account.CreditCardDetails.StatementDay,
                account.CreditCardDetails.DueDay,
                PayPreviousMonthBalanceInFull = account.CreditCardDetails.PayPreviousMonthBalanceInFull ? 1 : 0
            };
            if (account.CreditCardDetails.Id == 0)
            {
                conn.Execute(@"INSERT INTO CreditCardDetails (AccountId, Apr, StatementDay, DueDay, PayPreviousMonthBalanceInFull) 
                               VALUES (@AccountId, @Apr, @StatementDay, @DueDay, @PayPreviousMonthBalanceInFull)", ccdParam);
            }
            else
            {
                conn.Execute(@"UPDATE CreditCardDetails SET Apr=@Apr, StatementDay=@StatementDay, DueDay=@DueDay, 
                               PayPreviousMonthBalanceInFull=@PayPreviousMonthBalanceInFull WHERE Id=@Id", ccdParam);
            }
        }
    }
    
    public void DeleteAccount(int id)
    {
        using var conn = _db.GetConnection();
        conn.Execute("DELETE FROM MortgageDetails WHERE AccountId = @id", new { id });
        conn.Execute("DELETE FROM CreditCardDetails WHERE AccountId = @id", new { id });
        conn.Execute("DELETE FROM Accounts WHERE Id = @id", new { id });
    }

    public bool IsAccountInUse(int accountId)
    {
        using var conn = _db.GetConnection();
        
        // Check Bills (AccountId or ToAccountId)
        var billCount = conn.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM Bills WHERE AccountId = @accountId OR ToAccountId = @accountId", 
            new { accountId });
        if (billCount > 0) return true;

        // Check Buckets (AccountId)
        var bucketCount = conn.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM Buckets WHERE AccountId = @accountId", 
            new { accountId });
        if (bucketCount > 0) return true;

        // Check Transactions (AccountId or ToAccountId)
        var transactionCount = conn.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM Transactions WHERE AccountId = @accountId OR ToAccountId = @accountId", 
            new { accountId });
        if (transactionCount > 0) return true;

        return false;
    }
}