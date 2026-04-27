using Dapper;
using Microsoft.Data.Sqlite;
using System.IO;

namespace StayOnTarget.Data;

public class SqliteDecimalHandler : SqlMapper.TypeHandler<decimal>
{
    public override void SetValue(System.Data.IDbDataParameter parameter, decimal value)
    {
        parameter.Value = value;
    }

    public override decimal Parse(object value)
    {
        return Convert.ToDecimal(value);
    }
}

public class SqliteGuidHandler : SqlMapper.TypeHandler<Guid>
{
    public override void SetValue(System.Data.IDbDataParameter parameter, Guid value)
    {
        // Store as TEXT in SQLite
        parameter.Value = value.ToString();
    }

    public override Guid Parse(object value)
    {
        if (value is Guid g) return g;
        if (value is byte[] bytes && bytes.Length == 16) return new Guid(bytes);
        return Guid.Parse(value?.ToString() ?? string.Empty);
    }
}

public class SqliteNullableGuidHandler : SqlMapper.TypeHandler<Guid?>
{
    public override void SetValue(System.Data.IDbDataParameter parameter, Guid? value)
    {
        parameter.Value = value?.ToString();
    }

    public override Guid? Parse(object value)
    {
        if (value == null || value is DBNull) return null;
        if (value is Guid g) return g;
        if (value is byte[] bytes && bytes.Length == 16) return new Guid(bytes);
        var s = value.ToString();
        return string.IsNullOrWhiteSpace(s) ? null : Guid.Parse(s);
    }
}

public class DatabaseContext
{
    private readonly string _connectionString;
    
    private const string ProgramFolderName = "StayOnTarget";
    private const string DatabaseName = "budget.db";
    
    static DatabaseContext()
    {
        SqlMapper.AddTypeHandler(new SqliteDecimalHandler());
        SqlMapper.AddTypeHandler(new SqliteGuidHandler());
        SqlMapper.AddTypeHandler(new SqliteNullableGuidHandler());
    }

    public DatabaseContext()
    {
        var userProfileFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        //string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DatabaseName);
        var dbFolder = Path.Combine(userProfileFolder, ProgramFolderName);
        Directory.CreateDirectory(dbFolder);
        
        var dbPath = Path.Combine(dbFolder, DatabaseName);

        _connectionString = $"Data Source={dbPath}";
        InitializeDatabase();
    }

    public DatabaseContext(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        InitializeDatabase();
    }

    public SqliteConnection GetConnection() => new SqliteConnection(_connectionString);

    private void InitializeDatabase()
    {
        using var connection = GetConnection();
        connection.Open();
        
        connection.Execute(@"
            CREATE TABLE IF NOT EXISTS Accounts (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                BankName TEXT,
                Balance DECIMAL NOT NULL,
                BalanceAsOf TEXT DEFAULT '2000-01-01', 
                AnnualGrowthRate DECIMAL DEFAULT 0,
                IncludeInTotal INTEGER DEFAULT 1,
                Type INTEGER NOT NULL,
                HexColor TEXT DEFAULT '#FF0000FF'
            );

            CREATE TABLE IF NOT EXISTS MortgageDetails (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AccountId INTEGER NOT NULL,
                InterestRate DECIMAL NOT NULL,
                Escrow DECIMAL NOT NULL,
                MortgageInsurance DECIMAL NOT NULL,
                LoanPayment DECIMAL NOT NULL,
                PaymentDate TEXT,
                FOREIGN KEY(AccountId) REFERENCES Accounts(Id)
            );

            CREATE TABLE IF NOT EXISTS CreditCardDetails (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AccountId INTEGER NOT NULL,
                StatementDay INTEGER NOT NULL,
                DueDateOffset INTEGER NOT NULL DEFAULT 21,
                MinPayFloor DECIMAL NOT NULL DEFAULT 25,
                PayPreviousMonthBalanceInFull INTEGER NOT NULL,
                GraceActive INTEGER DEFAULT 0,
                FOREIGN KEY(AccountId) REFERENCES Accounts(Id)
            );

            CREATE TABLE IF NOT EXISTS Bills (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                ExpectedAmount DECIMAL NOT NULL,
                Frequency INTEGER NOT NULL,
                DueDay INTEGER NOT NULL,
                AccountId INTEGER,
                ToAccountId INTEGER,
                NextDueDate TEXT,
                Category TEXT,
                IsActive INTEGER DEFAULT 1,
                FOREIGN KEY(AccountId) REFERENCES Accounts(Id),
                FOREIGN KEY(ToAccountId) REFERENCES Accounts(Id)
            );

            CREATE TABLE IF NOT EXISTS PeriodBills (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                BillId INTEGER NOT NULL,
                PeriodDate TEXT NOT NULL,
                DueDate TEXT NOT NULL,
                ActualAmount DECIMAL DEFAULT 0,
                IsPaid INTEGER DEFAULT 0,
                FitId TEXT NOT NULL,
                FOREIGN KEY(BillId) REFERENCES Bills(Id)
            );

            CREATE TABLE IF NOT EXISTS Paychecks (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                ExpectedAmount DECIMAL NOT NULL,
                Frequency INTEGER NOT NULL,
                StartDate TEXT NOT NULL,
                EndDate TEXT,
                AccountId INTEGER REFERENCES Accounts(Id),
                IsBalanced INTEGER DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS Transactions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Description TEXT,
                Amount DECIMAL NOT NULL,
                Date TEXT NOT NULL,
                AccountId INTEGER,
                ToAccountId INTEGER,
                BucketId INTEGER REFERENCES Buckets(Id),
                PeriodDate TEXT NOT NULL,
                IsPrincipalOnly INTEGER DEFAULT 0,
                FitId TEXT NOT NULL,
                PaycheckId INTEGER REFERENCES Paychecks(Id),
                PaycheckOccurrenceDate TEXT,
                BillId INTEGER REFERENCES Bills(Id),
                FOREIGN KEY(AccountId) REFERENCES Accounts(Id),
                FOREIGN KEY(ToAccountId) REFERENCES Accounts(Id)
            );

            CREATE TABLE IF NOT EXISTS Buckets (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                ExpectedAmount DECIMAL NOT NULL,
                AccountId INTEGER,
                PaycheckId INTEGER,
                FOREIGN KEY(AccountId) REFERENCES Accounts(Id),
                FOREIGN KEY(PaycheckId) REFERENCES PayChecks(Id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS PeriodBuckets (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                BucketId INTEGER NOT NULL,
                PeriodDate TEXT NOT NULL,
                ActualAmount DECIMAL DEFAULT 0,
                IsPaid INTEGER DEFAULT 0,
                FitId TEXT NOT NULL,
                FOREIGN KEY(BucketId) REFERENCES Buckets(Id)
            );

            CREATE TABLE IF NOT EXISTS AccountReconciliations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AccountId INTEGER NOT NULL,
                ReconciledAsOfDate TEXT NOT NULL,
                ReconciledBalance DECIMAL NOT NULL,
                ReconciledOnDate TEXT NOT NULL,
                IsInvalidated INTEGER DEFAULT 0,
                FOREIGN KEY(AccountId) REFERENCES Accounts(Id)
            );

            CREATE TABLE IF NOT EXISTS AccountAprHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AccountId INTEGER NOT NULL,
                AsOfDate TEXT NOT NULL,
                AnnualPercentageRate DECIMAL NOT NULL,
                CashAdvanceRate DECIMAL NOT NULL,
                BalanceTransferRate DECIMAL NOT NULL,
                FOREIGN KEY(AccountId) REFERENCES Accounts(Id)
            );
        ");
  
        // var columnExists = connection.ExecuteScalar<int>(@"
        //     SELECT COUNT(*) FROM pragma_table_info('Transactions') WHERE name='BillId'");
        //
        // if (columnExists == 0)
        // {
        //     // If the table exists but the column doesn't, add it. 
        //     // We check if table exists first.
        //     var tableExists = connection.ExecuteScalar<int>(@"
        //         SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Transactions'");
        //     
        //     if (tableExists > 0)
        //     {
        //         connection.Execute("ALTER TABLE Transactions ADD COLUMN BillId INTEGER REFERENCES Bills(Id)");
        //     }
        // }
        
        // // Check if BalanceAsOf exists in Accounts table
        // var balanceAsOfExists = connection.ExecuteScalar<int>(@"
        //     SELECT COUNT(*) FROM pragma_table_info('Accounts') WHERE name='BalanceAsOf'");
        //
        // if (balanceAsOfExists == 0)
        // {
        //     var tableExists = connection.ExecuteScalar<int>(@"
        //         SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Accounts'");
        //     
        //     if (tableExists > 0)
        //     {
        //         connection.Execute("ALTER TABLE Accounts ADD COLUMN BalanceAsOf TEXT DEFAULT '2026-02-19'");
        //     }
        // }
        //
        // // Check if IsBalanced exists in Paychecks table
        // var isBalancedExists = connection.ExecuteScalar<int>(@"
        //     SELECT COUNT(*) FROM pragma_table_info('Paychecks') WHERE name='IsBalanced'");
        //
        // if (isBalancedExists == 0)
        // {
        //     var tableExists = connection.ExecuteScalar<int>(@"
        //         SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Paychecks'");
        //     
        //     if (tableExists > 0)
        //     {
        //         connection.Execute("ALTER TABLE Paychecks ADD COLUMN IsBalanced INTEGER DEFAULT 0");
        //     }
        // }
        //
        // Check if ToAccountId exists in Bills table
        // var columnExists = connection.ExecuteScalar<int>(@"
        //     SELECT COUNT(*) FROM pragma_table_info('CreditCardDetails') WHERE name='DueDay'");
        //
        // if (columnExists == 0)
        // {
        //     // If the table exists but the column doesn't, add it. 
        //     // We check if table exists first.
        //     var tableExists = connection.ExecuteScalar<int>(@"
        //         SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CreditCardDetails'");
        //     
        //     if (tableExists > 0)
        //     {
        //         connection.Execute("ALTER TABLE CreditCardDetails ADD COLUMN DueDay INTEGER");
        //     }
        // }
        //
        // // Check if IncludeInTotal exists in Accounts table
        // var includeInTotalExists = connection.ExecuteScalar<int>(@"
        //     SELECT COUNT(*) FROM pragma_table_info('Accounts') WHERE name='IncludeInTotal'");
        //
        // if (includeInTotalExists == 0)
        // {
        //     var tableExists = connection.ExecuteScalar<int>(@"
        //         SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Accounts'");
        //     
        //     if (tableExists > 0)
        //     {
        //         connection.Execute("ALTER TABLE Accounts ADD COLUMN IncludeInTotal INTEGER DEFAULT 1");
        //     }
        // }
        //
        // // Check if PaymentDate exists in MortgageDetails table
        // var paymentDateExists = connection.ExecuteScalar<int>(@"
        //     SELECT COUNT(*) FROM pragma_table_info('MortgageDetails') WHERE name='PaymentDate'");
        //
        // if (paymentDateExists == 0)
        // {
        //     var tableExists = connection.ExecuteScalar<int>(@"
        //         SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='MortgageDetails'");
        //     
        //     if (tableExists > 0)
        //     {
        //         connection.Execute("ALTER TABLE MortgageDetails ADD COLUMN PaymentDate TEXT");
        //     }
        // }
        //
        // // Check if IsPrincipalOnly exists in Transactions table
        // var isPrincipalOnlyExists = connection.ExecuteScalar<int>(@"
        //     SELECT COUNT(*) FROM pragma_table_info('Transactions') WHERE name='IsPrincipalOnly'");
        //
        // if (isPrincipalOnlyExists == 0)
        // {
        //     var tableExists = connection.ExecuteScalar<int>(@"
        //         SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Transactions'");
        //     
        //     if (tableExists > 0)
        //     {
        //         connection.Execute("ALTER TABLE Transactions ADD COLUMN IsPrincipalOnly INTEGER DEFAULT 0");
        //     }
        // }
        //
        // // Check if BucketId exists in Transactions table
        // var bucketIdExists = connection.ExecuteScalar<int>(@"
        //     SELECT COUNT(*) FROM pragma_table_info('Transactions') WHERE name='BucketId'");
        //
        // if (bucketIdExists == 0)
        // {
        //     var tableExists = connection.ExecuteScalar<int>(@"
        //         SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Transactions'");
        //     
        //     if (tableExists > 0)
        //     {
        //         connection.Execute("ALTER TABLE Transactions ADD COLUMN BucketId INTEGER REFERENCES Buckets(Id)");
        //     }
        // }
        //
        // // Check if EndDate exists in Paychecks table
        // var endDateExists = connection.ExecuteScalar<int>(@"
        //     SELECT COUNT(*) FROM pragma_table_info('Paychecks') WHERE name='EndDate'");
        //
        // if (endDateExists == 0)
        // {
        //     var tableExists = connection.ExecuteScalar<int>(@"
        //         SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Paychecks'");
        //     
        //     if (tableExists > 0)
        //     {
        //         connection.Execute("ALTER TABLE Paychecks ADD COLUMN EndDate TEXT");
        //     }
        // }
        //
        // // Check if AccountId exists in Paychecks table
        // var paycheckAccountIdExists = connection.ExecuteScalar<int>(@"
        //     SELECT COUNT(*) FROM pragma_table_info('Paychecks') WHERE name='AccountId'");
        //
        // if (paycheckAccountIdExists == 0)
        // {
        //     var tableExists = connection.ExecuteScalar<int>(@"
        //         SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Paychecks'");
        //     
        //     if (tableExists > 0)
        //     {
        //         connection.Execute("ALTER TABLE Paychecks ADD COLUMN AccountId INTEGER REFERENCES Accounts(Id)");
        //     }
        // }
        //
        // // Ensure FITID columns exist in Transactions, PeriodBills, PeriodBuckets
        // var transactionFitIdExists = connection.ExecuteScalar<int>(@"
        //     SELECT COUNT(*) FROM pragma_table_info('Transactions') WHERE name='FitId'");
        // if (transactionFitIdExists == 0)
        // {
        //     var tableExists = connection.ExecuteScalar<int>(@"
        //         SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Transactions'");
        //     if (tableExists > 0)
        //     {
        //         connection.Execute("ALTER TABLE Transactions ADD COLUMN FitId TEXT");
        //     }
        // }
        //
        // var periodBillFitIdExists = connection.ExecuteScalar<int>(@"
        //     SELECT COUNT(*) FROM pragma_table_info('PeriodBills') WHERE name='FitId'");
        // if (periodBillFitIdExists == 0)
        // {
        //     var tableExists = connection.ExecuteScalar<int>(@"
        //         SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='PeriodBills'");
        //     if (tableExists > 0)
        //     {
        //         connection.Execute("ALTER TABLE PeriodBills ADD COLUMN FitId TEXT");
        //     }
        // }
        //
        // var periodBucketFitIdExists = connection.ExecuteScalar<int>(@"
        //     SELECT COUNT(*) FROM pragma_table_info('PeriodBuckets') WHERE name='FitId'");
        // if (periodBucketFitIdExists == 0)
        // {
        //     var tableExists = connection.ExecuteScalar<int>(@"
        //         SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='PeriodBuckets'");
        //     if (tableExists > 0)
        //     {
        //         connection.Execute("ALTER TABLE PeriodBuckets ADD COLUMN FitId TEXT");
        //     }
        // }
        //
        // // Populate missing FITIDs for existing rows
        // var transactionIds = connection.Query<int>("SELECT Id FROM Transactions WHERE FitId IS NULL OR FitId = ''");
        // foreach (var id in transactionIds)
        // {
        //     connection.Execute("UPDATE Transactions SET FitId = @fitId WHERE Id = @id", new { id, fitId = Guid.NewGuid().ToString() });
        // }
        // var periodBillIds = connection.Query<int>("SELECT Id FROM PeriodBills WHERE FitId IS NULL OR FitId = ''");
        // foreach (var id in periodBillIds)
        // {
        //     connection.Execute("UPDATE PeriodBills SET FitId = @fitId WHERE Id = @id", new { id, fitId = Guid.NewGuid().ToString() });
        // }
        // var periodBucketIds = connection.Query<int>("SELECT Id FROM PeriodBuckets WHERE FitId IS NULL OR FitId = ''");
        // foreach (var id in periodBucketIds)
        // {
        //     connection.Execute("UPDATE PeriodBuckets SET FitId = @fitId WHERE Id = @id", new { id, fitId = Guid.NewGuid().ToString() });
        // }
        //
        // // Check if PaycheckId exists in Transactions table
        // var paycheckIdExists = connection.ExecuteScalar<int>(@"
        //     SELECT COUNT(*) FROM pragma_table_info('Transactions') WHERE name='PaycheckId'");
        //
        // if (paycheckIdExists == 0)
        // {
        //     var tableExists = connection.ExecuteScalar<int>(@"
        //         SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Transactions'");
        //     
        //     if (tableExists > 0)
        //     {
        //         connection.Execute("ALTER TABLE Transactions ADD COLUMN PaycheckId INTEGER REFERENCES Paychecks(Id)");
        //         connection.Execute("ALTER TABLE Transactions ADD COLUMN PaycheckOccurrenceDate TEXT");
        //     }
        // }

        // Check if CreditCardDetails table exists
        var ccDetailsTableExists = connection.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM sqlite_master WHERE TYPE='table' AND name='CreditCardDetails'");
        
        if (ccDetailsTableExists == 0)
        {
            connection.Execute(@"
                CREATE TABLE CreditCardDetails (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    AccountId INTEGER NOT NULL,
                    Apr DECIMAL NOT NULL,
                    StatementDay INTEGER NOT NULL,
                    DueDateOffset INTEGER NOT NULL DEFAULT 21,
                    PayPreviousMonthBalanceInFull INTEGER NOT NULL,
                    GraceActive INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY(AccountId) REFERENCES Accounts(Id)
                )");
        }

        // Check if HexColor exists in Accounts table
        var hexColorExists = connection.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM pragma_table_info('Accounts') WHERE name='HexColor'");

        if (hexColorExists == 0)
        {
            connection.Execute("ALTER TABLE Accounts ADD COLUMN HexColor TEXT DEFAULT '#FF0000FF'");
        }

        // Check if FromAccountReconciledId exists in Transactions table
        var fromAccountReconciledIdExists = connection.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM pragma_table_info('Transactions') WHERE name='FromAccountReconciledId'");

        if (fromAccountReconciledIdExists == 0)
        {
            var tableExists = connection.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Transactions'");

            if (tableExists > 0)
            {
                connection.Execute("ALTER TABLE Transactions ADD COLUMN FromAccountReconciledId INTEGER REFERENCES AccountReconciliations(Id)");
            }
        }

        // Check if ToAccountReconciledId exists in Transactions table
        var toAccountReconciledIdExists = connection.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM pragma_table_info('Transactions') WHERE name='ToAccountReconciledId'");

        if (toAccountReconciledIdExists == 0)
        {
            var tableExists = connection.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Transactions'");

            if (tableExists > 0)
            {
                connection.Execute("ALTER TABLE Transactions ADD COLUMN ToAccountReconciledId INTEGER REFERENCES AccountReconciliations(Id)");
            }
        }
    }
}
