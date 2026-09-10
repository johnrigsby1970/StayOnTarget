using Dapper;
using Microsoft.Data.Sqlite;
using Serilog;
using System.Data;
using System.IO;
using System.Windows;

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
    private string _dbPath;

    //private const string ProgramFolderName = "StayOnTarget";
    //private const string ProgramFolderName = @"AppData\Local\StayOnTarget";
    // private const string DatabaseName = "budget.db";

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

        _dbPath = dbPath;
        _connectionString = BuildConnectionString(_dbPath, userPassword);

        //InitializeDatabase();
    }

    // Public helper to compute the default user profile path safely
    // public static string GetDefaultDbPath() {
    //     var userProfileFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    //     var dbFolder = Path.Combine(userProfileFolder, ProgramFolderName);
    //     return Path.Combine(dbFolder, DatabaseName);
    // }

    public string DbPath => _dbPath;

    public string BuildConnectionString(string dbPath, string? password) {
        if (string.IsNullOrEmpty(password)) {
            return $"Data Source={dbPath};";
        }

        // Convert Windows backslashes to forward slashes so the SQLite URI parser reads it cleanly
        var normalizedPath = dbPath.Replace('\\', '/');

        var builder = new SqliteConnectionStringBuilder {
            // Use URI syntax safely in the Data Source field
            DataSource = $"file:{normalizedPath}?cipher=sqlcipher&legacy=4",
            Password = password,
            Pooling = false
        };

        // Semicolons only separate built-in keywords (Data Source, Password, Pooling)
        // The cipher settings live seamlessly inside the Data Source string itself!
        return builder.ConnectionString;
    }

    public string BackupDatabase(string? password) {
        var userProfileFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        //var dbFolder = Path.Combine(userProfileFolder, ProgramFolderName);
        var oldPath = _dbPath; //Path.Combine(dbFolder, DatabaseName);
        if (string.IsNullOrWhiteSpace(oldPath)) {
            MessageBox.Show("No file found to backup.");
            return string.Empty;
        }

        string directory = Path.GetDirectoryName(oldPath)!;
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

    public string BackupDatabaseToPath(string destinationPath, string? password) {
        if (string.IsNullOrWhiteSpace(_dbPath)) return string.Empty;

        var sourceConnStr = BuildConnectionString(_dbPath, password);
        var destConnStr = BuildConnectionString(destinationPath, password);

        using (var source = new SqliteConnection(sourceConnStr))
        using (var destination = new SqliteConnection(destConnStr)) {
            source.Open();
            destination.Open();
            source.BackupDatabase(destination);
        }

        return destinationPath;
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

    public void InitializeDatabase() {
        Log.Information("Initializing database.");
        try {
            // Registering for non-nullable dictionary:
            SqlMapper.AddTypeHandler(new JsonObjectTypeHandler<Dictionary<string, decimal>>());

            using var connection = GetConnection();
            connection.Open();
            Log.Debug("Database connection opened for initialization.");

            // Turn off FKs globally for table setup and migrations
            connection.Execute("PRAGMA foreign_keys = OFF;");

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
            HexColor TEXT DEFAULT '#FF0000FF',
            IsPrimary INTEGER DEFAULT 0,
            IsArchived INTEGER DEFAULT 0
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
            IsPrincipalOnly INTEGER DEFAULT 0,
            IsActive INTEGER DEFAULT 1,
            BucketId INTEGER REFERENCES Buckets(Id) ON DELETE SET NULL, 
            SubCategoryId INTEGER REFERENCES Subcategories(Id) ON DELETE SET NULL,            
            Overrides TEXT NULL,
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
            FOREIGN KEY(BillId) REFERENCES Bills(Id),               
            UNIQUE(BillId, PeriodDate)
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
            NormalizedDescription TEXT,
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
            SubCategoryId INTEGER REFERENCES Subcategories(Id),
            FOREIGN KEY(AccountId) REFERENCES Accounts(Id),
            FOREIGN KEY(BucketId) REFERENCES Buckets(Id) ON DELETE SET NULL,
            FOREIGN KEY(SubCategoryId) REFERENCES Subcategories(Id) ON DELETE SET NULL,
            FOREIGN KEY(BillId) REFERENCES Bills(Id) ON DELETE SET NULL,
            FOREIGN KEY(PaycheckId) REFERENCES Paychecks(Id) ON DELETE SET NULL,
            FOREIGN KEY(ReconciliationId) REFERENCES AccountReconciliations(Id) ON DELETE SET NULL
        );

        CREATE TABLE IF NOT EXISTS Buckets (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL,
            ExpectedAmount DECIMAL NOT NULL,
            AccountId INTEGER,
            PaycheckId INTEGER, 
            IsArchived INTEGER DEFAULT 0, 
            IsActive INTEGER DEFAULT 1, 
            Type INTEGER NOT NULL DEFAULT 0, 
            TargetBalance NUMERIC NOT NULL DEFAULT 0, 
            CurrentBalance NUMERIC NOT NULL DEFAULT 0, 
            InitialBalance NUMERIC NOT NULL DEFAULT 0,
            Overrides TEXT NULL,
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
            FOREIGN KEY(BucketId) REFERENCES Buckets(Id),
            UNIQUE(BucketId, PeriodDate)
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

        CREATE TABLE IF NOT EXISTS AppSettings (
            SettingKey TEXT PRIMARY KEY,
            SettingValue TEXT
        );

        CREATE TABLE IF NOT EXISTS AccountSnapshots (
            SnapshotDate TEXT NOT NULL,
            AccountID INTEGER NOT NULL,
            Balance REAL NOT NULL,
            PRIMARY KEY (SnapshotDate, AccountID),
            FOREIGN KEY (AccountID) REFERENCES Accounts(ID)
        );

        CREATE TABLE IF NOT EXISTS Categories (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL,
            HexColor TEXT DEFAULT '#FF0000FF',
            SortOrder INTEGER DEFAULT 0,
            IsArchived INTEGER DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS Subcategories (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            CategoryId INTEGER NOT NULL,
            DefaultBucketId INTEGER,
            Name TEXT NOT NULL,
            SortOrder INTEGER DEFAULT 0,
            IsArchived INTEGER DEFAULT 0,
            FOREIGN KEY(CategoryId) REFERENCES Categories(Id) ON DELETE CASCADE,
            FOREIGN KEY(DefaultBucketId) REFERENCES Buckets(Id) ON DELETE SET NULL
        );

        CREATE TABLE IF NOT EXISTS BucketPaycheckAllocations (
            AllocationId INTEGER PRIMARY KEY AUTOINCREMENT,
            BucketId INTEGER NOT NULL,
            PaycheckId INTEGER NOT NULL,
            AllocationType TEXT NOT NULL DEFAULT 'FixedAmount' CHECK(AllocationType IN ('FixedAmount', 'Percentage')),
            AllocationValue DECIMAL NOT NULL,
            SortOrder INTEGER NOT NULL DEFAULT 0,
            IsActive INTEGER NOT NULL DEFAULT 1 CHECK(IsActive IN (0, 1)),
            CreatedDate TEXT NOT NULL DEFAULT (datetime('now')),
            FOREIGN KEY (BucketId) REFERENCES Buckets (Id) ON DELETE CASCADE,
            FOREIGN KEY (PaycheckId) REFERENCES PayChecks (Id) ON DELETE CASCADE,
            CONSTRAINT UQ_Bucket_Paycheck UNIQUE (BucketId, PaycheckId)
        );

        CREATE INDEX IF NOT EXISTS IX_BucketPaycheckAllocations_PaycheckId ON BucketPaycheckAllocations (PaycheckId);
        CREATE INDEX IF NOT EXISTS IX_BucketPaycheckAllocations_BucketId ON BucketPaycheckAllocations (BucketId);

        -- TRIGGERS
        CREATE TRIGGER IF NOT EXISTS trig_Transactions_AfterInsert
        AFTER INSERT ON Transactions
        BEGIN
            INSERT INTO AccountSnapshots (SnapshotDate, AccountID, Balance)
            VALUES (
                NEW.TransactionDate, 
                NEW.AccountID, 
                COALESCE((SELECT SUM(Amount) FROM Transactions WHERE AccountID = NEW.AccountID AND TransactionDate <= NEW.TransactionDate), 0.00)
            )
            ON CONFLICT(SnapshotDate, AccountID) DO UPDATE SET Balance = EXCLUDED.Balance;
        END;

        CREATE TRIGGER IF NOT EXISTS trig_Transactions_AfterUpdate
        AFTER UPDATE ON Transactions
        BEGIN
            INSERT INTO AccountSnapshots (SnapshotDate, AccountID, Balance)
            VALUES (
                OLD.TransactionDate, 
                OLD.AccountID, 
                COALESCE((SELECT SUM(Amount) FROM Transactions WHERE AccountID = OLD.AccountID AND TransactionDate <= OLD.TransactionDate), 0.00)
            )
            ON CONFLICT(SnapshotDate, AccountID) DO UPDATE SET Balance = EXCLUDED.Balance;

            INSERT INTO AccountSnapshots (SnapshotDate, AccountID, Balance)
            VALUES (
                NEW.TransactionDate, 
                NEW.AccountID, 
                COALESCE((SELECT SUM(Amount) FROM Transactions WHERE AccountID = NEW.AccountID AND TransactionDate <= NEW.TransactionDate), 0.00)
            )
            ON CONFLICT(SnapshotDate, AccountID) DO UPDATE SET Balance = EXCLUDED.Balance;
        END;

        CREATE TRIGGER IF NOT EXISTS trig_Transactions_AfterDelete
        AFTER DELETE ON Transactions
        BEGIN
            INSERT INTO AccountSnapshots (SnapshotDate, AccountID, Balance)
            VALUES (
                OLD.TransactionDate, 
                OLD.AccountID, 
                COALESCE((SELECT SUM(Amount) FROM Transactions WHERE AccountID = OLD.AccountID AND TransactionDate <= OLD.TransactionDate), 0.00)
            )
            ON CONFLICT(SnapshotDate, AccountID) DO UPDATE SET Balance = EXCLUDED.Balance;
        END;
        ");

            // Execute incremental migrations & column checks
            MigrateBucketsTable(connection);

            var subcategoryColumnExists = connection.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM pragma_table_info('Transactions') WHERE name='SubCategoryId'");

            if (subcategoryColumnExists == 0) {
                connection.Execute(
                    "ALTER TABLE Transactions ADD COLUMN SubCategoryId INTEGER REFERENCES Subcategories(Id)");

                var existingBuckets = connection.Query<(long Id, string Name)>("SELECT Id, Name FROM Buckets");
                foreach (var bucket in existingBuckets) {
                    var categoryId = connection.QuerySingle<long>(@"
                    INSERT INTO Categories (Name) VALUES (@Name);
                    SELECT last_insert_rowid();", new { Name = bucket.Name });

                    var subCategoryId = connection.QuerySingle<long>(@"
                    INSERT INTO Subcategories (CategoryId, DefaultBucketId, Name) 
                    VALUES (@CategoryId, @DefaultBucketId, @Name);
                    SELECT last_insert_rowid();",
                        new { CategoryId = categoryId, DefaultBucketId = bucket.Id, Name = $"General {bucket.Name}" });

                    connection.Execute(
                        "UPDATE Transactions SET SubCategoryId = @subCategoryId WHERE BucketId = @bucketId",
                        new { subCategoryId, bucketId = bucket.Id });
                }
            }

            EnsureColumnExists(connection, "Transactions", "NormalizedDescription", "TEXT");
            EnsureColumnExists(connection, "Transactions", "IsCleared", "INTEGER DEFAULT 0");
            EnsureColumnExists(connection, "Transactions", "IsInterestOnly", "INTEGER DEFAULT 0");
            EnsureColumnExists(connection, "Bills", "IsPrincipalOnly", "INTEGER DEFAULT 0");
            EnsureColumnExists(connection, "Accounts", "IsArchived", "INTEGER DEFAULT 0");
            EnsureColumnExists(connection, "Bills", "IsArchived", "INTEGER DEFAULT 0");
            EnsureColumnExists(connection, "Buckets", "IsArchived", "INTEGER DEFAULT 0");
            EnsureColumnExists(connection, "Buckets", "IsActive", "INTEGER DEFAULT 1");
            EnsureColumnExists(connection, "Accounts", "IsPrimary", "INTEGER DEFAULT 0");
            EnsureColumnExists(connection, "Accounts", "HexColor", "TEXT DEFAULT '#FF0000FF'");
            EnsureColumnExists(connection, "MortgageDetails", "StatementDay", "INTEGER NOT NULL DEFAULT 1");
            EnsureColumnExists(connection, "Bills", "BucketId", "INTEGER REFERENCES Buckets(Id) ON DELETE SET NULL");
            EnsureColumnExists(connection, "Bills", "SubCategoryId",
                "INTEGER REFERENCES Subcategories(Id) ON DELETE SET NULL");

            EnsureColumnExists(connection, "Bills", "Overrides", "TEXT NULL");
            EnsureColumnExists(connection, "Buckets", "Overrides", "TEXT NULL");

            // Composite Unique Indexes
            connection.Execute(@"
            CREATE UNIQUE INDEX IF NOT EXISTS UX_PeriodBills_BillId_PeriodDate ON PeriodBills(BillId, PeriodDate);
            CREATE UNIQUE INDEX IF NOT EXISTS UX_PeriodBuckets_BucketId_PeriodDate ON PeriodBuckets(BucketId, PeriodDate);
        ");

            // BUCKETS MIGRATION FOR DECOUPLED PAYCHECKS & TARGET FREQUENCY
            var newBucketFieldExists = connection.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM pragma_table_info('Buckets') WHERE name='TargetFrequency'");

            if (newBucketFieldExists == 0) {
                // Guarantee FKs are OFF during table swap
                connection.Execute("PRAGMA foreign_keys = OFF;");

                // Seed Junction Table from existing Bucket -> Paycheck relationships
                connection.Execute(@"
                INSERT INTO BucketPaycheckAllocations (BucketId, PaycheckId, AllocationType, AllocationValue, SortOrder, IsActive)
                SELECT 
                    Id AS BucketId,
                    PaycheckId,
                    'FixedAmount' AS AllocationType,
                    ExpectedAmount AS AllocationValue,
                    0 AS SortOrder,
                    1 AS IsActive
                FROM Buckets
                WHERE PaycheckId IS NOT NULL 
                  AND Type <> 1;");

                // Create updated Buckets table without PaycheckId
                connection.Execute(@"
                CREATE TABLE Buckets_New (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    ExpectedAmount DECIMAL NOT NULL,
                    AccountId INTEGER,
                    Type INTEGER NOT NULL DEFAULT 0,
                    TargetBalance NUMERIC NOT NULL DEFAULT 0,
                    CurrentBalance NUMERIC NOT NULL DEFAULT 0,
                    InitialBalance NUMERIC NOT NULL DEFAULT 0,
                    IsArchived INTEGER DEFAULT 0,
                    IsActive INTEGER DEFAULT 1,
                    TargetFrequency INTEGER NULL,
                    TargetAmount DECIMAL NOT NULL DEFAULT 0.0,
                    NextDueDate TEXT NULL,
                    FOREIGN KEY(AccountId) REFERENCES Accounts(Id)
                );");

                // Copy data over
                connection.Execute(@"
                INSERT INTO Buckets_New (
                    Id, Name, ExpectedAmount, AccountId, Type, TargetBalance, 
                    CurrentBalance, InitialBalance, IsArchived, IsActive, 
                    TargetFrequency, TargetAmount
                )
                SELECT 
                    Id, Name, ExpectedAmount, AccountId, Type, TargetBalance, 
                    CurrentBalance, InitialBalance, IsArchived, IsActive, 
                    CASE WHEN PaycheckId IS NOT NULL AND Type <> 1 THEN 0 ELSE NULL END AS TargetFrequency,
                    ExpectedAmount AS TargetAmount
                FROM Buckets;");

                // Swap tables
                connection.Execute("DROP TABLE Buckets;");
                connection.Execute("ALTER TABLE Buckets_New RENAME TO Buckets;");
            }

            // Seed Default Categories
            CategorySeeder.SeedDefaultCategories(connection);

//             connection.Execute(@"
//                 CREATE INDEX IF NOT EXISTS IX_Transactions_AccountId ON Transactions(AccountId);
//                 CREATE INDEX IF NOT EXISTS IX_Transactions_TransactionDate ON Transactions(TransactionDate);
//                 CREATE INDEX IF NOT EXISTS IX_Transactions_BucketId ON Transactions(BucketId);
//                 CREATE INDEX IF NOT EXISTS IX_Transactions_SubCategoryId ON Transactions(SubCategoryId);
//                 CREATE INDEX IF NOT EXISTS IX_Transactions_BillId ON Transactions(BillId);
//                 CREATE INDEX IF NOT EXISTS IX_Bills_AccountId ON Bills(AccountId);
//                 CREATE INDEX IF NOT EXISTS IX_Bills_BucketId ON Bills(BucketId);
//                 CREATE INDEX IF NOT EXISTS IX_Subcategories_CategoryId ON Subcategories(CategoryId);
//                 CREATE INDEX IF NOT EXISTS IX_Buckets_AccountId ON Buckets(AccountId);
//
//                 CREATE UNIQUE INDEX IF NOT EXISTS UX_AccountReconciliations_AccountId_AsOfDate 
//                 ON AccountReconciliations(AccountId, ReconciledAsOfDate) 
//                 WHERE IsInvalidated = 0;
//
//                 CREATE INDEX IF NOT EXISTS IX_Transactions_NormalizedDescription 
//                 ON Transactions(NormalizedDescription);
//
// CREATE INDEX IF NOT EXISTS IX_Transactions_Analytics_Payee 
// ON Transactions(NormalizedDescription, TransactionDate) 
// WHERE Amount < 0;
//
// CREATE INDEX IF NOT EXISTS IX_Transactions_Analytics_SubCategory 
// ON Transactions(SubCategoryId, TransactionDate) 
// WHERE Amount < 0;
//
// CREATE INDEX IF NOT EXISTS IX_Transactions_Analytics_Bucket 
// ON Transactions(BucketId, TransactionDate) 
// WHERE Amount < 0;
//
//             ");


//             connection.Execute(@"
//     CREATE TABLE IF NOT EXISTS BucketTargetHistory (
//         Id INTEGER PRIMARY KEY AUTOINCREMENT,
//         BucketId INTEGER NOT NULL,
//         EffectiveStartDate TEXT NOT NULL,
//         ExpectedAmount DECIMAL NOT NULL,
//         FOREIGN KEY(BucketId) REFERENCES Buckets(Id) ON DELETE CASCADE
//     );
//
//     CREATE INDEX IF NOT EXISTS IX_BucketTargetHistory_BucketId_Date 
//     ON BucketTargetHistory(BucketId, EffectiveStartDate);
//
//
// CREATE TABLE IF NOT EXISTS BillTargetHistory (
//     Id INTEGER PRIMARY KEY AUTOINCREMENT,
//     BillId INTEGER NOT NULL,
//     PeriodDate TEXT NOT NULL, -- e.g., '2026-09-01' representing the monthly period
//     ExpectedAmount DECIMAL NOT NULL,
//     HasOverride INTEGER NOT NULL DEFAULT 0,
//     FOREIGN KEY(BillId) REFERENCES Bills(Id) ON DELETE CASCADE,
//     CONSTRAINT UX_BillTargetHistory_Bill_Period UNIQUE (BillId, PeriodDate)
// );
//
// CREATE INDEX IF NOT EXISTS IX_BillTargetHistory_BillId_Date 
// ON BillTargetHistory(BillId, PeriodDate);
//
// ");
//             
            
            // Turn foreign key enforcement back ON
            connection.Execute("PRAGMA foreign_keys = ON;");

            Log.Information("Database initialization and schema updates completed successfully.");
        }
        catch (Exception ex) {
            Log.Fatal(ex, "Database initialization failed.");
            throw;
        }
    }

    // Usage when restoring/initializing SQLite database
    public static void MigrateBucketsTable(IDbConnection connection) {
        EnsureColumnExists(connection, "Buckets", "Type", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumnExists(connection, "Buckets", "TargetBalance", "NUMERIC NOT NULL DEFAULT 0");
        EnsureColumnExists(connection, "Buckets", "CurrentBalance", "NUMERIC NOT NULL DEFAULT 0");
        EnsureColumnExists(connection, "Buckets", "InitialBalance", "NUMERIC NOT NULL DEFAULT 0");
        EnsureColumnExists(connection, "Buckets", "EffectiveStartDate", "TEXT NOT NULL DEFAULT '2000-01-01'");
    }

    private static void EnsureColumnExists(IDbConnection connection, string tableName, string columnName,
        string columnDefinition) {
        var exists = connection.ExecuteScalar<int>($@"
        SELECT COUNT(*) 
        FROM pragma_table_info('{tableName}') 
        WHERE name = @columnName", new { columnName });

        if (exists == 0) {
            connection.Execute($"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};");
        }
    }
}