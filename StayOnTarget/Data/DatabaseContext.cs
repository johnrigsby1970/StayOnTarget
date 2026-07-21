using Dapper;
using Microsoft.Data.Sqlite;
using Serilog;
using System;
using System.IO;

namespace StayOnTarget.Data;

public class SqliteDecimalHandler : SqlMapper.TypeHandler<decimal> {
    public override void SetValue(System.Data.IDbDataParameter parameter, decimal value) {
        parameter.Value = value;
    }

    public override decimal Parse(object value) {
        return Convert.ToDecimal(value);
    }
}

public class SqliteGuidHandler : SqlMapper.TypeHandler<Guid> {
    public override void SetValue(System.Data.IDbDataParameter parameter, Guid value) {
        // Store as TEXT in SQLite
        parameter.Value = value.ToString();
    }

    public override Guid Parse(object value) {
        if (value is Guid g) return g;
        if (value is byte[] bytes && bytes.Length == 16) return new Guid(bytes);
        return Guid.Parse(value?.ToString() ?? string.Empty);
    }
}

public class SqliteNullableGuidHandler : SqlMapper.TypeHandler<Guid?> {
    public override void SetValue(System.Data.IDbDataParameter parameter, Guid? value) {
        parameter.Value = value?.ToString();
    }

    public override Guid? Parse(object value) {
        if (value == null || value is DBNull) return null;
        if (value is Guid g) return g;
        if (value is byte[] bytes && bytes.Length == 16) return new Guid(bytes);
        var s = value.ToString();
        return string.IsNullOrWhiteSpace(s) ? null : Guid.Parse(s);
    }
}

public class DatabaseContext {
    private string _connectionString;

    private const string ProgramFolderName = "StayOnTarget";
    private const string DatabaseName = "budget.db";

    static DatabaseContext() {
        // // Call this once at application startup to register the encryption provider
        // SQLitePCL.Batteries_V2.Init();
        SqlMapper.AddTypeHandler(new SqliteDecimalHandler());
        SqlMapper.AddTypeHandler(new SqliteGuidHandler());
        SqlMapper.AddTypeHandler(new SqliteNullableGuidHandler());
    }

    public DatabaseContext(string dbPath, string userPassword) {
        // Ensure the directory exists for whatever path is passed in
        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory)) {
            Directory.CreateDirectory(directory);
        }

        _connectionString = BuildConnectionString(dbPath, userPassword);

        InitializeDatabase();
    }

    // Public helper to compute the default user profile path safely
    public static string GetDefaultDbPath() {
        var userProfileFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dbFolder = Path.Combine(userProfileFolder, ProgramFolderName);
        return Path.Combine(dbFolder, DatabaseName);
    }

    public string BuildConnectionString(string dbPath, string? password) {
        if (string.IsNullOrEmpty(password)) {
            return $"Data Source={dbPath};";
        }

        // Convert Windows backslashes to forward slashes so the SQLite URI parser reads it cleanly
        var normalizedPath = dbPath.Replace('\\', '/');

        // Semicolons only separate built-in keywords (Data Source, Password, Pooling)
        // The cipher settings live seamlessly inside the Data Source string itself!
        return $"Data Source=file:{normalizedPath}?cipher=sqlcipher&legacy=4;Password={password};Pooling=False;";
    }

    public string BackupDatabase(string? password) {
        var userProfileFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dbFolder = Path.Combine(userProfileFolder, ProgramFolderName);
        var oldPath = Path.Combine(dbFolder, DatabaseName);
        string directory = Path.GetDirectoryName(oldPath);
        string filenameWithoutExt = Path.GetFileNameWithoutExtension(oldPath);
        string extension = Path.GetExtension(oldPath);

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        string newFilename = $"{filenameWithoutExt}_{timestamp}{extension}";
        string newPath = Path.Combine(directory, newFilename);

        // Force the encryption engine to use standard SQLCipher 4 formatting
        // Note the "file:" prefix and the "?cipher=sqlcipher&legacy=4" parameters
        var oldConnectionString = BuildConnectionString(oldPath, password);
        var newConnectionString = BuildConnectionString(newPath, password);

        using (var source = new SqliteConnection(oldConnectionString))
        using (var destination = new SqliteConnection(newConnectionString)) {
            source.Open();
            destination.Open();

            // This performs a full, online backup safely
            source.BackupDatabase(destination);
            return newPath;
        }
    }

    public void ChangePassword(string dbPath, string oldPassword, string newPassword) {
        string connectionString = BuildConnectionString(dbPath, oldPassword);

        using (var connection = new SqliteConnection(connectionString)) {
            connection.Open();

            using (var command = connection.CreateCommand()) {
                // Correct SQLCipher/SQLite3MC syntax: PRAGMA rekey('password')
                // Note: Single quotes wrap the password string inside the command
                command.CommandText = $"PRAGMA rekey('{newPassword}');";
                command.ExecuteNonQuery();
            }
        }

        _connectionString = BuildConnectionString(dbPath, newPassword);
    }

    public SqliteConnection GetConnection() {
        try {
            return new SqliteConnection(_connectionString);
        }
        catch (Exception ex) {
            Log.Error(ex, "Failed to create SqliteConnection.");
            throw;
        }
    }

    private void InitializeDatabase() {
        Log.Information("Initializing database.");
        try {
            using var connection = GetConnection();
            connection.Open();
            Log.Debug("Database connection opened for initialization.");

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
                StatementDay INTEGER NOT NULL DEFAULT 1,
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
                Memo TEXT,
                Amount DECIMAL NOT NULL,
                TransactionDate TEXT NOT NULL,
                AccountId INTEGER NOT NULL,
                BucketId INTEGER REFERENCES Buckets(Id),
                PeriodDate TEXT NOT NULL,
                IsPrincipalOnly INTEGER DEFAULT 0,
                IsInterestOnly INTEGER DEFAULT 0,
                TransactionId TEXT NOT NULL,
                FitId TEXT NOT NULL,
                PaycheckId INTEGER REFERENCES Paychecks(Id),
                PaycheckOccurrenceDate TEXT,
                BillId INTEGER REFERENCES Bills(Id),
                ReconciliationId INTEGER REFERENCES AccountReconciliations(Id),
                FOREIGN KEY(AccountId) REFERENCES Accounts(Id)
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
                ReconciledAsOfDate TEXT NOT NULL, --StatementEndingBalance
                ReconciledBalance DECIMAL NOT NULL, --StatementEndingBalance
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

            var columnExists = connection.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM pragma_table_info('Transactions') WHERE name='IsInterestOnly'");

            if (columnExists == 0) {
                // If the table exists but the column doesn't, add it. 
                // We check if table exists first.
                var tableExists = connection.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Transactions'");

                if (tableExists > 0) {
                    connection.Execute("ALTER TABLE Transactions ADD COLUMN IsInterestOnly INTEGER DEFAULT 0");
                }
            }

            // Check if CreditCardDetails table exists
            var ccDetailsTableExists = connection.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM sqlite_master WHERE TYPE='table' AND name='CreditCardDetails'");

            if (ccDetailsTableExists == 0) {
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

            if (hexColorExists == 0) {
                connection.Execute("ALTER TABLE Accounts ADD COLUMN HexColor TEXT DEFAULT '#FF0000FF'");
            }

            // // Check if FromAccountReconciledId exists in Transactions table
            // var fromAccountReconciledIdExists = connection.ExecuteScalar<int>(@"
            //     SELECT COUNT(*) FROM pragma_table_info('Transactions') WHERE name='FromAccountReconciledId'");
            //
            // if (fromAccountReconciledIdExists == 0) {
            //     var tableExists = connection.ExecuteScalar<int>(@"
            //         SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Transactions'");
            //
            //     if (tableExists > 0) {
            //         connection.Execute(
            //             "ALTER TABLE Transactions ADD COLUMN FromAccountReconciledId INTEGER REFERENCES AccountReconciliations(Id)");
            //     }
            // }
            //
            // // Check if ToAccountReconciledId exists in Transactions table
            // var toAccountReconciledIdExists = connection.ExecuteScalar<int>(@"
            //     SELECT COUNT(*) FROM pragma_table_info('Transactions') WHERE name='ToAccountReconciledId'");
            //
            // if (toAccountReconciledIdExists == 0) {
            //     var tableExists = connection.ExecuteScalar<int>(@"
            //         SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Transactions'");
            //
            //     if (tableExists > 0) {
            //         connection.Execute(
            //             "ALTER TABLE Transactions ADD COLUMN ToAccountReconciledId INTEGER REFERENCES AccountReconciliations(Id)");
            //     }
            // }

            if (!connection.Query<dynamic>("PRAGMA table_info(MortgageDetails)").Any(x => x.name == "StatementDay")) {
                connection.Execute("ALTER TABLE MortgageDetails ADD COLUMN StatementDay INTEGER NOT NULL DEFAULT 1;");
            }

            Log.Information("Database initialization and schema updates completed successfully.");
        }
        catch (Exception ex) {
            Log.Fatal(ex, "Database initialization failed.");
            throw;
        }
    }
}