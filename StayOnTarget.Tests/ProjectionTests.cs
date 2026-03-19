using Microsoft.VisualStudio.TestTools.UnitTesting;
using StayOnTarget.Models;
using StayOnTarget.Services;

namespace StayOnTarget.Tests
{
    [TestClass]
    public class ProjectionTests
    {
        private ProjectionEngine _engine = null!;

        [TestInitialize]
        public void Setup()
        {
            _engine = new ProjectionEngine();
        }

        [TestMethod]
        public void TestPaycheckAssociation_OverridesProjectedPaycheck()
        {
            // Arrange
            var accounts = new List<Account>
            {
                new Account { Id = 1, Name = "Checking", Balance = 1000, IncludeInTotal = true, BalanceAsOf = new DateTime(2026, 1, 1) }
            };
            var paychecks = new List<Paycheck>
            {
                new Paycheck { Id = 1, Name = "Salary", ExpectedAmount = 2000, Frequency = Frequency.BiWeekly, StartDate = new DateTime(2026, 2, 20), AccountId = 1 }
            };
            
            // Transaction associated with the paycheck on 2026-02-20
            var transactions = new List<Transaction>
            {
                new Transaction 
                { 
                    Id = 101, 
                    Description = "Actual Salary", 
                    Amount = 2100, 
                    Date = new DateTime(2026, 2, 20), 
                    PaycheckId = 1, 
                    PaycheckOccurrenceDate = new DateTime(2026, 2, 20),
                    ToAccountId = 1
                }
            };

            var startDate = new DateTime(2026, 2, 1);
            var endDate = new DateTime(2026, 3, 1);


            
            // Act
            var results = _engine.CalculateProjections(
                transactions.Where(x=>x.PaycheckId.HasValue).ToList(),
                transactions.Where(x=>x.BillId.HasValue).ToList(),
                transactions.Where(x=>x.BucketId.HasValue).ToList(),
                startDate, endDate, accounts, paychecks, new List<Bill>(), new List<BudgetBucket>(), new List<PeriodBill>(), new List<PeriodBucket>(), transactions).ToList();

            // Assert
            // We expect one paycheck entry on 2/20. Since we have an transaction override, the "Pay: Salary" should be missing and replaced by "Actual Salary".
            var salaryEntries = results.Where(r => r.Date == new DateTime(2026, 2, 20)).ToList();
            
            Assert.AreEqual(1, salaryEntries.Count, "Should only have one entry for the paycheck date");
            Assert.AreEqual("Actual Salary", salaryEntries[0].Description);
            Assert.AreEqual(2100, salaryEntries[0].Amount);
        }

        [TestMethod]
        public void TestPaycheckHeuristic_Removed()
        {
            // Arrange
            var accounts = new List<Account>
            {
                new Account { Id = 1, Name = "Checking", Balance = 1000, IncludeInTotal = true, BalanceAsOf = new DateTime(2026, 1, 1) }
            };
            var paychecks = new List<Paycheck>
            {
                new Paycheck { Id = 1, Name = "Salary", ExpectedAmount = 2000, Frequency = Frequency.BiWeekly, StartDate = new DateTime(2026, 2, 20), AccountId = 1 }
            };
            
            // Transaction NOT associated with the paycheck, but has same date and description-based "Pay: Salary"
            var transactions = new List<Transaction>
            {
                new Transaction 
                { 
                    Id = 101, 
                    Description = "Pay: Salary", 
                    Amount = 2100, 
                    Date = new DateTime(2026, 2, 20), 
                    ToAccountId = 1
                }
            };

            var startDate = new DateTime(2026, 2, 1);
            var endDate = new DateTime(2026, 3, 1);

            // Act
            var results = _engine.CalculateProjections(
                transactions.Where(x=>x.PaycheckId.HasValue).ToList(),
                transactions.Where(x=>x.BillId.HasValue).ToList(),
                transactions.Where(x=>x.BucketId.HasValue).ToList(),startDate, endDate, accounts, paychecks, new List<Bill>(), new List<BudgetBucket>(), new List<PeriodBill>(), new List<PeriodBucket>(), transactions).ToList();

            // Assert
            // Since the heuristic is removed, we expect BOTH the projected "Pay: Salary" and the transactioon "Pay: Salary".
            var salaryEntries = results.Where(r => r.Date == new DateTime(2026, 2, 20)).ToList();
            
            Assert.AreEqual(2, salaryEntries.Count, "Should have two entries for the paycheck date because heuristic was removed");
            Assert.IsTrue(salaryEntries.Any(r => r.Description == "Expected Pay: Salary" && r.Amount == 2000), "Missing projected paycheck");
            Assert.IsTrue(salaryEntries.Any(r => r.Description == "Pay: Salary" && r.Amount == 2100), "Missing transaction");
        }

        [TestMethod]
        public void TestBillAccountRobustness_UsesAccountId()
        {
            // Arrange
            var accounts = new List<Account>
            {
                new Account { Id = 1, Name = "Checking", Balance = 1000, IncludeInTotal = true, BalanceAsOf = new DateTime(2026, 1, 1) },
                new Account { Id = 2, Name = "Savings", Balance = 5000, IncludeInTotal = false, BalanceAsOf = new DateTime(2026, 1, 1) }
            };
            var bills = new List<Bill>
            {
                new Bill { Id = 1, Name = "Rent", ExpectedAmount = 500, Frequency = Frequency.Monthly, DueDay = 5, AccountId = 1 }
            };

            var startDate = new DateTime(2026, 2, 1);
            var endDate = new DateTime(2026, 3, 1);

            // Act
            var results = _engine.CalculateProjections(
                new List<Transaction>(),
                new List<Transaction>(),
                new List<Transaction>(),startDate, endDate, accounts, new List<Paycheck>(), bills, new List<BudgetBucket>(), new List<PeriodBill>(), new List<PeriodBucket>(), new List<Transaction>()).ToList();

            // Assert
            var rentEntry = results.FirstOrDefault(r => r.Description.Contains("Rent"));
            Assert.IsNotNull(rentEntry);
            Assert.AreEqual(-500, rentEntry.Amount);
            // Balance should be 1000 - 500 = 500 (Savings is not included in total)
            Assert.AreEqual(500, rentEntry.Balance);
            Assert.AreEqual(500m, rentEntry.AccountBalances["Checking"]);
            Assert.AreEqual(5000m, rentEntry.AccountBalances["Savings"]);
        }

        [TestMethod]
        public void TestInterestAccrual_MortgageBalanceIncreases()
        {
            // Arrange
            var accounts = new List<Account>
            {
                new Account 
                { 
                    Id = 1, 
                    Name = "Mortgage", 
                    Balance = 200000, 
                    Type = AccountType.Mortgage, 
                    IncludeInTotal = true, 
                    BalanceAsOf = new DateTime(2026, 2, 1),
                    MortgageDetails = new MortgageDetails
                    {
                        InterestRate = 6.0m, // 0.5% monthly
                        PaymentDate = new DateTime(2026, 2, 1)
                    }
                }
            };

            var startDate = new DateTime(2026, 2, 1);
            var endDate = new DateTime(2026, 3, 10); // Should trigger one interest event

            // Act
            var results = _engine.CalculateProjections(new List<Transaction>(),new List<Transaction>(),new List<Transaction>(), startDate, endDate, accounts, new List<Paycheck>(), new List<Bill>(), new List<BudgetBucket>(), new List<PeriodBill>(), new List<PeriodBucket>(), new List<Transaction>()).ToList();

            // Assert
            var interestEntry = results.FirstOrDefault(r => r.Description.Contains("Interest: Mortgage"));
            Assert.IsNotNull(interestEntry, "Should have an interest entry");
            
            // 200,000 * 0.06 / 12 = 1000
            Assert.AreEqual(1000m, interestEntry.Amount);
            Assert.AreEqual(201000m, interestEntry.AccountBalances["Mortgage"]);
            // Debts are subtracted from the total balance
            Assert.AreEqual(-201000m, interestEntry.Balance);
        }

        [TestMethod]
        public void TestDailyGrowth_SavingsBalanceIncreases()
        {
            // Arrange
            var accounts = new List<Account>
            {
                new Account 
                { 
                    Id = 1, 
                    Name = "Savings", 
                    Balance = 10000, 
                    Type = AccountType.Savings, 
                    IncludeInTotal = true, 
                    BalanceAsOf = new DateTime(2026, 2, 1),
                    AnnualGrowthRate = 3.65m // 0.01% daily
                }
            };

            var startDate = new DateTime(2026, 2, 1);
            var endDate = new DateTime(2026, 2, 11); // 10 days of growth

            // Act
            var results = _engine.CalculateProjections(new List<Transaction>(),new List<Transaction>(),new List<Transaction>(), startDate, endDate, accounts, new List<Paycheck>(), new List<Bill>(), new List<BudgetBucket>(), new List<PeriodBill>(), new List<PeriodBucket>(), new List<Transaction>()).ToList();

            // Assert
            // After 10 days, 10,000 * 0.0001 * 10 = 10
            // The ProjectionEngine calculates growth between events or at the end of the projection period.
            // Since there are no events other than the end of the projection, we should see the balance in the last item.
            
            var lastItem = results.Last();
            Assert.AreEqual(10010m, lastItem.AccountBalances["Savings"]);
            Assert.AreEqual(10010m, lastItem.Balance);
        }

        [TestMethod]
        public void TestCreditCard_InterestAccrual_AverageDailyBalance()
        {
            // Arrange
            var accounts = new List<Account>
            {
                new Account 
                { 
                    Id = 1, 
                    Name = "CreditCard", 
                    Balance = 1000, 
                    Type = AccountType.CreditCard, 
                    IncludeInTotal = true, 
                    BalanceAsOf = new DateTime(2026, 2, 1),
                    CreditCardDetails = new CreditCardDetails
                    {
                        StatementDay = 15,
                        DueDateOffset = 25,
                        MinPayFloor = 25,
                        GraceActive = true,
                        PayPreviousMonthBalanceInFull = false // No grace period
                    },
                    AccountAprHistory = new List<AccountAprHistory>() {
                        new AccountAprHistory() {
                            AccountId = 1, 
                            AnnualPercentageRate = 36.5m, 
                            BalanceTransferRate = 36.5m, 
                            CashAdvanceRate = 36.5m, 
                            AsOfDate = DateTime.MinValue
                        }
                    }
                }
            };

            // 1000 balance for 14 days (Feb 1 to Feb 14)
            // Statement on Feb 15
            var startDate = new DateTime(2026, 2, 1);
            var endDate = new DateTime(2026, 2, 16);

            // Act
            var results = _engine.CalculateProjections(new List<Transaction>(),new List<Transaction>(),new List<Transaction>(), startDate, endDate, accounts, new List<Paycheck>(), new List<Bill>(), new List<BudgetBucket>(), new List<PeriodBill>(), new List<PeriodBucket>(), new List<Transaction>()).ToList();

            // Assert
            var interestEntry = results.FirstOrDefault(r => r.Description.Contains("Credit Card Interest"));
            Assert.IsNotNull(interestEntry, "Should have a credit card interest entry");
            
            // ADB = 1000
            // DPR = 0.365 / 365 = 0.001
            // Days = 14 (Feb 1 to Feb 14)
            // Interest = 1000 * 0.001 * 14 = 14
            Assert.AreEqual(14m, interestEntry.Amount);
            Assert.AreEqual(1014m, interestEntry.AccountBalances["CreditCard"]);
            // Debt increases, so running balance decreases
            // Initial running balance was -1000. Now -1014.
            Assert.AreEqual(-1014m, interestEntry.Balance);
        }

        [TestMethod]
        public void TestCreditCard_UserScenario_InterestAfterGracePeriod()
        {
            // Arrange
            // 0 balance on 2/5/2026.
            // Transaction of $14.40 on 3/4/2026.
            // Statement day 5.
            // APR 17.74%.
            // PayPreviousMonthBalanceInFull = true.

            var accounts = new List<Account>
            {
                new Account 
                { 
                    Id = 1, 
                    Name = "CreditCard", 
                    Balance = 0, 
                    Type = AccountType.CreditCard, 
                    IncludeInTotal = true, 
                    BalanceAsOf = new DateTime(2026, 2, 5),
                    CreditCardDetails = new CreditCardDetails
                    {
                        StatementDay = 5,
                        DueDateOffset = 25,
                        MinPayFloor = 25,
                        GraceActive = true,
                        PayPreviousMonthBalanceInFull = true // grace period
                    },
                    AccountAprHistory = new List<AccountAprHistory>() {
                        new AccountAprHistory() {
                            AccountId = 1, 
                            AnnualPercentageRate = 17.74m, 
                            BalanceTransferRate = 17.74m, 
                            CashAdvanceRate = 17.74m, 
                            AsOfDate = DateTime.MinValue
                        }
                    }
                }
            };

            var transactions = new List<Transaction>
            {
                new Transaction { Date = new DateTime(2026, 3, 4), Amount = 14.40m, AccountId = 1, Description = "Small Purchase" }
            };

            var startDate = new DateTime(2026, 2, 5);
            var endDate = new DateTime(2027, 2, 5); // A full year

            // Act
            var results = _engine.CalculateProjections(
                transactions.Where(x=>x.PaycheckId.HasValue).ToList(),
                transactions.Where(x=>x.BillId.HasValue).ToList(),
                transactions.Where(x=>x.BucketId.HasValue).ToList(),startDate, endDate, accounts, new List<Paycheck>(), new List<Bill>(), new List<BudgetBucket>(), new List<PeriodBill>(), new List<PeriodBucket>(), transactions).ToList();

            // Assert
            // 2/5 statement: balance 0. PaidInFull = true.
            // 3/4: Transaction 14.40. Balance 14.40.
            // 3/5 statement: interest 0 because 2/5 balance was 0 (grace period).
            //               balance remains 14.40. PaidInFull = false.
            // 4/5 statement: interest SHOULD accrue on 14.40 (ADB = 14.40).
            
            var marchStatement = results.FirstOrDefault(r => r.Description.Contains("Credit Card Interest") && r.Date == new DateTime(2026, 3, 5));
            Assert.IsNotNull(marchStatement, "March statement (3/5) should exist");
            Assert.AreEqual(0m, marchStatement.Amount, "Interest on 3/5 should be 0 due to grace period");
            Assert.AreEqual(14.40m, marchStatement.AccountBalances["CreditCard"]);

            var aprilStatement = results.FirstOrDefault(r => r.Description.Contains("Credit Card Interest") && r.Date == new DateTime(2026, 4, 5));
            Assert.IsNotNull(aprilStatement, "April statement (4/5) should exist");
            
            // Expected interest for April:
            // 14.40 * (0.1774 / 365) * 31 days (March 5 to April 4) = 0.217... (roughly $0.22)
            Assert.IsTrue(aprilStatement.Amount > 0, $"April interest (4/5) should be > 0, but was {aprilStatement.Amount}");

            var mayStatement = results.FirstOrDefault(r => r.Description.Contains("Credit Card Interest") && r.Date == new DateTime(2026, 5, 5));
            Assert.IsNotNull(mayStatement, "May statement (5/5) should exist");
            Assert.IsTrue(mayStatement.Amount > 0, $"May interest (5/5) should be > 0, but was {mayStatement.Amount}");
            Assert.IsTrue(mayStatement.AccountBalances["CreditCard"] > aprilStatement.AccountBalances["CreditCard"], "Balance should increase due to interest");

            var octStatement = results.FirstOrDefault(r => r.Description.Contains("Credit Card Interest") && r.Date == new DateTime(2026, 10, 5));
            Assert.IsNotNull(octStatement, "October statement (10/5) should exist");

            var novStatement = results.FirstOrDefault(r => r.Description.Contains("Credit Card Interest") && r.Date == new DateTime(2026, 11, 5));
            Assert.IsNotNull(novStatement, "November statement (11/5) should exist");
            Assert.IsTrue(novStatement.AccountBalances["CreditCard"] > octStatement.AccountBalances["CreditCard"], "Balance should continue to increase month over month");
        }

        [TestMethod]
        public void TestCreditCard_GracePeriod_NoInterestWhenPaidInFull()
        {
            // Arrange
            var accounts = new List<Account>
            {
                new Account 
                { 
                    Id = 1, 
                    Name = "CreditCard", 
                    Balance = 0, // Paid in full
                    Type = AccountType.CreditCard, 
                    IncludeInTotal = true, 
                    BalanceAsOf = new DateTime(2026, 2, 1),
                    CreditCardDetails = new CreditCardDetails
                    {
                        StatementDay = 15,
                        DueDateOffset = 25,
                        MinPayFloor = 25,
                        GraceActive = true,
                        PayPreviousMonthBalanceInFull = true // There is a grace period allowed for this account
                    },
                    AccountAprHistory = new List<AccountAprHistory>() {
                        new AccountAprHistory() {
                            AccountId = 1, 
                            AnnualPercentageRate = 36.5m, 
                            BalanceTransferRate = 36.5m, 
                            CashAdvanceRate = 36.5m, 
                            AsOfDate = DateTime.MinValue
                        }
                    }
                }
            };
            
            // New purchase on Feb 5
            var transactions = new List<Transaction>
            {
                new Transaction { Date = new DateTime(2026, 2, 5), Amount = 500, AccountId = 1, Description = "Purchase" }
            };

            var startDate = new DateTime(2026, 2, 1);
            var endDate = new DateTime(2026, 2, 16);

            // Act
            var results = _engine.CalculateProjections(
                transactions.Where(x=>x.PaycheckId.HasValue).ToList(),
                transactions.Where(x=>x.BillId.HasValue).ToList(),
                transactions.Where(x=>x.BucketId.HasValue).ToList(),startDate, endDate, accounts, new List<Paycheck>(), new List<Bill>(), new List<BudgetBucket>(), new List<PeriodBill>(), new List<PeriodBucket>(), transactions).ToList();

            // Assert
            var interestEntry = results.FirstOrDefault(r => r.Description.Contains("Credit Card Interest"));
            Assert.IsNotNull(interestEntry);
            Assert.AreEqual(0m, interestEntry.Amount, "Interest should be 0 due to grace period");
        }

        [TestMethod]
        public void TestCreditCard_GracePeriod_InterestWhenPaidInFullBecauseNoGracePeriodAllowed()
        {
            // Arrange
            var accounts = new List<Account>
            {
                new Account 
                { 
                    Id = 1, 
                    Name = "CreditCard", 
                    Balance = 0, // Paid in full
                    Type = AccountType.CreditCard, 
                    IncludeInTotal = true, 
                    BalanceAsOf = new DateTime(2026, 2, 1),
                    CreditCardDetails = new CreditCardDetails
                    {
                        StatementDay = 15,
                        DueDateOffset = 25,
                        MinPayFloor = 25,
                        GraceActive = true,
                        PayPreviousMonthBalanceInFull = false // No grace period
                    },
                    AccountAprHistory = new List<AccountAprHistory>() {
                        new AccountAprHistory() {
                            AccountId = 1, 
                            AnnualPercentageRate = 36.5m, 
                            BalanceTransferRate = 36.5m, 
                            CashAdvanceRate = 36.5m, 
                            AsOfDate = DateTime.MinValue
                        }
                    }
                }
            };
            
            // New purchase on Feb 5
            var transactions = new List<Transaction>
            {
                new Transaction { Date = new DateTime(2026, 2, 5), Amount = 500, AccountId = 1, Description = "Purchase" }
            };

            var startDate = new DateTime(2026, 2, 1);
            var endDate = new DateTime(2026, 2, 16);

            // Act
            var results = _engine.CalculateProjections(
                transactions.Where(x=>x.PaycheckId.HasValue).ToList(),
                transactions.Where(x=>x.BillId.HasValue).ToList(),
                transactions.Where(x=>x.BucketId.HasValue).ToList(),startDate, endDate, accounts, new List<Paycheck>(), new List<Bill>(), new List<BudgetBucket>(), new List<PeriodBill>(), new List<PeriodBucket>(), transactions).ToList();

            // Assert
            var interestEntry = results.FirstOrDefault(r => r.Description.Contains("Credit Card Interest"));
            Assert.IsNotNull(interestEntry);
            Assert.AreEqual(5m, interestEntry.Amount, "Interest should be 5 due to lack of grace period");
        }
        
        [TestMethod]
        public void TestCreditCard_InterestAdjustment_OverridesProjectedInterest()
        {
            // Arrange
            var accounts = new List<Account>
            {
                new Account 
                { 
                    Id = 1, 
                    Name = "CreditCard", 
                    Balance = 1000, 
                    Type = AccountType.CreditCard, 
                    IncludeInTotal = true, 
                    BalanceAsOf = new DateTime(2026, 2, 1),
                    CreditCardDetails = new CreditCardDetails
                    {
                        StatementDay = 15,
                        DueDateOffset = 25,
                        MinPayFloor = 25,
                        GraceActive = true,
                        PayPreviousMonthBalanceInFull = true // No grace period
                    },
                    AccountAprHistory = new List<AccountAprHistory>() {
                        new AccountAprHistory() {
                            AccountId = 1, 
                            AnnualPercentageRate = 36.5m, 
                            BalanceTransferRate = 36.5m, 
                            CashAdvanceRate = 36.5m, 
                            AsOfDate = DateTime.MinValue
                        }
                    }
                }
            };
            
            // Actual interest transaction
            var transactions = new List<Transaction>
            {
                new Transaction 
                { 
                    Date = new DateTime(2026, 2, 10), 
                    Amount = 25, 
                    AccountId = 1, 
                    IsInterestAdjustment = true,
                    Description = "Actual Interest" 
                }
            };

            var startDate = new DateTime(2026, 2, 1);
            var endDate = new DateTime(2026, 2, 16);

            // Act
            var results = _engine.CalculateProjections(
                transactions.Where(x=>x.PaycheckId.HasValue).ToList(),
                transactions.Where(x=>x.BillId.HasValue).ToList(),
                transactions.Where(x=>x.BucketId.HasValue).ToList(),startDate, endDate, accounts, new List<Paycheck>(), new List<Bill>(), new List<BudgetBucket>(), new List<PeriodBill>(), new List<PeriodBucket>(), transactions).ToList();

            // Assert
            var projectedInterest = results.FirstOrDefault(r => r.Description.Contains("Credit Card Interest"));
            Assert.IsNull(projectedInterest, "Projected interest should be suppressed by interest adjustment transaction");
            
            var actualInterest = results.FirstOrDefault(r => r.Description == "Actual Interest");
            Assert.IsNotNull(actualInterest);
            Assert.AreEqual(25m, actualInterest.Amount);
            Assert.AreEqual(1025m, actualInterest.AccountBalances["CreditCard"]);
        }

        [TestMethod]
        public void TestPeriodNet_CalculatedCorrected()
        {
            // Arrange
            var accounts = new List<Account>
            {
                new Account { Id = 1, Name = "Checking", Balance = 1000, IncludeInTotal = true, BalanceAsOf = new DateTime(2026, 2, 1) }
            };
            var paychecks = new List<Paycheck>
            {
                new Paycheck { Id = 1, Name = "Pay1", ExpectedAmount = 2000, Frequency = Frequency.BiWeekly, StartDate = new DateTime(2026, 2, 1), AccountId = 1 },
                new Paycheck { Id = 2, Name = "Pay2", ExpectedAmount = 2000, Frequency = Frequency.BiWeekly, StartDate = new DateTime(2026, 2, 15), AccountId = 1 }
            };
            var bills = new List<Bill>
            {
                new Bill { Id = 1, Name = "Bill1", ExpectedAmount = 500, Frequency = Frequency.Monthly, DueDay = 25, AccountId = 1 }
            };

            var startDate = new DateTime(2026, 2, 1);
            var endDate = new DateTime(2026, 3, 1);

            // Act
            var results = _engine.CalculateProjections(new List<Transaction>(),new List<Transaction>(),new List<Transaction>(), startDate, endDate, accounts, paychecks, bills, new List<BudgetBucket>(), new List<PeriodBill>(), new List<PeriodBucket>(), new List<Transaction>()).ToList();

            // Assert
            // Period 1: 2/1 to 2/14. Events: Pay1 (2000). Net = 2000.
            var pay1Entry = results.FirstOrDefault(r => r.Description == "Expected Pay: Pay1");
            Assert.IsNotNull(pay1Entry);
            Assert.AreEqual(2000m, pay1Entry.PeriodNet);

            // Period 2: 2/15 onwards. Events: Pay1 (2000), Pay2 (2000), Bill1 (-500). Total = 3500.
            // Since they are on the same day, Pay: Pay1 should be the first item and have the PeriodNet.
            var pay1SecondOccurrence = results.FirstOrDefault(r => r.Description == "Expected Pay: Pay1" && r.Date == new DateTime(2026, 2, 15));
            Assert.IsNotNull(pay1SecondOccurrence);
            Assert.AreEqual(3500m, pay1SecondOccurrence.PeriodNet);

            var pay2Entry = results.FirstOrDefault(r => r.Description == "Expected Pay: Pay2" && r.Date == new DateTime(2026, 2, 15));
            Assert.IsNotNull(pay2Entry);
            Assert.IsNull(pay2Entry.PeriodNet);
        }

        [TestMethod]
        public void TestTransactionInterest_OverridesProjectedInterest()
        {
            // Arrange
            var accounts = new List<Account>
            {
                new Account 
                { 
                    Id = 1, 
                    Name = "Mortgage", 
                    Balance = 200000, 
                    Type = AccountType.Mortgage, 
                    IncludeInTotal = true, 
                    BalanceAsOf = new DateTime(2026, 2, 1),
                    MortgageDetails = new MortgageDetails
                    {
                        InterestRate = 6.0m,
                        PaymentDate = new DateTime(2026, 2, 15)
                    }
                }
            };

            // Transaction on the same date as projected interest (2/15)
            // It has ToAccountId = 1, so it should be treated as interest/rebalance
            var transactions = new List<Transaction>
            {
                new Transaction
                {
                    Id = 101,
                    Description = "Actual Interest",
                    Amount = 950,
                    Date = new DateTime(2026, 2, 15),
                    ToAccountId = 1,
                    IsRebalance = true
                }
            };

            var startDate = new DateTime(2026, 2, 1);
            var endDate = new DateTime(2026, 3, 1);

            // Act
            var results = _engine.CalculateProjections(
                transactions.Where(x=>x.PaycheckId.HasValue).ToList(),
                transactions.Where(x=>x.BillId.HasValue).ToList(),
                transactions.Where(x=>x.BucketId.HasValue).ToList(),startDate, endDate, accounts, new List<Paycheck>(), new List<Bill>(), new List<BudgetBucket>(), new List<PeriodBill>(), new List<PeriodBucket>(), transactions).ToList();

            // Assert
            // We expect "Actual Interest" to exist and "Interest: Mortgage" to be missing.
            var interestEntries = results.Where(r => r.Date == new DateTime(2026, 2, 15)).ToList();
            
            Assert.AreEqual(1, interestEntries.Count, "Should only have one entry on the interest date");
            Assert.AreEqual("Actual Interest", interestEntries[0].Description);
            Assert.AreEqual(950, interestEntries[0].Amount);
            Assert.AreEqual(200950m, interestEntries[0].AccountBalances["Mortgage"]);
            // Debts are subtracted from total balance
            Assert.AreEqual(-200950m, interestEntries[0].Balance);
        }

        [TestMethod]
        public void TestTransactionInterestAdjustment_Mortgage_IncreasesBalance()
        {
            // Arrange
            var accounts = new List<Account>
            {
                new Account 
                { 
                    Id = 1, 
                    Name = "Mortgage", 
                    Balance = 200000, 
                    Type = AccountType.Mortgage, 
                    IncludeInTotal = true, 
                    BalanceAsOf = new DateTime(2026, 2, 1),
                    MortgageDetails = new MortgageDetails
                    {
                        InterestRate = 6.0m,
                        PaymentDate = new DateTime(2026, 2, 15)
                    }
                }
            };

            // Transaction with IsInterestAdjustment = true
            var transactions = new List<Transaction>
            {
                new Transaction
                {
                    Id = 101,
                    Description = "Manual Interest",
                    Amount = 1000,
                    Date = new DateTime(2026, 2, 10),
                    ToAccountId = 1,
                    IsInterestAdjustment = true
                }
            };

            var startDate = new DateTime(2026, 2, 1);
            var endDate = new DateTime(2026, 3, 1);

            // Act
            var results = _engine.CalculateProjections(
                transactions.Where(x=>x.PaycheckId.HasValue).ToList(),
                transactions.Where(x=>x.BillId.HasValue).ToList(),
                transactions.Where(x=>x.BucketId.HasValue).ToList(),startDate, endDate, accounts, new List<Paycheck>(), new List<Bill>(), new List<BudgetBucket>(), new List<PeriodBill>(), new List<PeriodBucket>(), transactions).ToList();

            // Assert
            var manualInterest = results.FirstOrDefault(r => r.Description == "Manual Interest");
            Assert.IsNotNull(manualInterest);
            Assert.AreEqual(1000m, manualInterest.Amount);
            // It should INCREASE the debt balance
            Assert.AreEqual(201000m, manualInterest.AccountBalances["Mortgage"]);
            Assert.AreEqual(-201000m, manualInterest.Balance);
        }

        [TestMethod]
        public void TestBucketReduction_TransactionReducesProjectedBucket()
        {
            // Arrange
            var accounts = new List<Account>
            {
                new Account { Id = 1, Name = "Checking", Balance = 1000, IncludeInTotal = true, BalanceAsOf = new DateTime(2026, 2, 1) }
            };
            var paychecks = new List<Paycheck>
            {
                new Paycheck { Id = 1, Name = "Pay1", ExpectedAmount = 2000, Frequency = Frequency.BiWeekly, StartDate = new DateTime(2026, 2, 1), AccountId = 1 }
            };
            var buckets = new List<BudgetBucket>
            {
                new BudgetBucket { Id = 1, Name = "Groceries", ExpectedAmount = 500, AccountId = 1 }
            };

            // Transaction for this bucket in this period
            var transactions = new List<Transaction>
            {
                new Transaction
                {
                    Id = 101,
                    Description = "Store Purchase",
                    Amount = 200,
                    Date = new DateTime(2026, 2, 5),
                    AccountId = 1,
                    BucketId = 1
                }
            };

            var startDate = new DateTime(2026, 2, 1);
            var endDate = new DateTime(2026, 2, 14); // One period

            // Act
            var results = _engine.CalculateProjections(
                transactions.Where(x=>x.PaycheckId.HasValue).ToList(),
                transactions.Where(x=>x.BillId.HasValue).ToList(),
                transactions.Where(x=>x.BucketId.HasValue).ToList(),startDate, endDate, accounts, paychecks, new List<Bill>(), buckets, new List<PeriodBill>(), new List<PeriodBucket>(), transactions).ToList();

            // Assert
            // Bucket Groceries should be reduced by 200. Original 500 - 200 = 300.
            var bucketEntry = results.FirstOrDefault(r => r.Description.Contains("Bucket: Groceries"));
            Assert.IsNotNull(bucketEntry, "Should have a bucket entry");
            Assert.AreEqual(-300m, bucketEntry.Amount, "Bucket amount should be reduced by transaction spending");

            // Total balance impact should be:
            // 1000 (starting) + 2000 (paycheck) - 200 (transaction) - 300 (remaining bucket) = 2500
            var lastEntry = results.Last();
            Assert.AreEqual(2500m, lastEntry.Balance);
        }

        [TestMethod]
        public void TestBucketReduction_TransactionExceedsProjectedBucket()
        {
            // Arrange
            var accounts = new List<Account>
            {
                new Account { Id = 1, Name = "Checking", Balance = 1000, IncludeInTotal = true, BalanceAsOf = new DateTime(2026, 2, 1) }
            };
            var paychecks = new List<Paycheck>
            {
                new Paycheck { Id = 1, Name = "Pay1", ExpectedAmount = 2000, Frequency = Frequency.BiWeekly, StartDate = new DateTime(2026, 2, 1), AccountId = 1 }
            };
            var buckets = new List<BudgetBucket>
            {
                new BudgetBucket { Id = 1, Name = "Groceries", ExpectedAmount = 500, AccountId = 1 }
            };

            // Transaction exceeding this bucket
            var transactions = new List<Transaction>
            {
                new Transaction
                {
                    Id = 101,
                    Description = "Big Grocery Run",
                    Amount = 600,
                    Date = new DateTime(2026, 2, 5),
                    AccountId = 1,
                    BucketId = 1
                }
            };

            var startDate = new DateTime(2026, 2, 1);
            var endDate = new DateTime(2026, 2, 14);

            // Act
            var results = _engine.CalculateProjections(
                transactions.Where(x=>x.PaycheckId.HasValue).ToList(),
                transactions.Where(x=>x.BillId.HasValue).ToList(),
                transactions.Where(x=>x.BucketId.HasValue).ToList(),startDate, endDate, accounts, paychecks, new List<Bill>(), buckets, new List<PeriodBill>(), new List<PeriodBucket>(), transactions).ToList();

            // Assert
            // Bucket Groceries should be reduced to 0 because spending (600) >= projected (500).
            var bucketEntry = results.FirstOrDefault(r => r.Description.Contains("Bucket: Groceries"));
            Assert.IsNotNull(bucketEntry, "Should have a bucket entry");
            Assert.AreEqual(0m, bucketEntry.Amount, "Bucket amount should be reduced to 0 when spending exceeds budget");

            // Total balance impact should be:
            // 1000 (starting) + 2000 (paycheck) - 600 (transaction) - 0 (remaining bucket) = 2400
            var lastEntry = results.Last();
            Assert.AreEqual(2400m, lastEntry.Balance);
        }
        [TestMethod]
        public void TestBucketReduction_UserScenario()
        {
            // Arrange
            // Bucket "Grayson" with $50 allotted.
            // Paychecks on 2/19/2026 and 3/5/2026.
            // Transaction for $500 on 2/20/2026 associated with "Grayson" bucket.
            
            var accounts = new List<Account>
            {
                new Account { Id = 1, Name = "Checking", Balance = 1000, IncludeInTotal = true, BalanceAsOf = new DateTime(2026, 1, 1) }
            };
            var paychecks = new List<Paycheck>
            {
                new Paycheck { Id = 1, Name = "Pay1", ExpectedAmount = 2000, Frequency = Frequency.BiWeekly, StartDate = new DateTime(2026, 2, 19), AccountId = 1 }
            };
            var buckets = new List<BudgetBucket>
            {
                new BudgetBucket { Id = 1, Name = "Grayson", ExpectedAmount = 50, AccountId = 1 }
            };
            var transactions = new List<Transaction>
            {
                new Transaction { Id = 101, Description = "Grayson Transaction", Amount = 500, Date = new DateTime(2026, 2, 20), BucketId = 1, AccountId = 1 }
            };

            var startDate = new DateTime(2026, 2, 19);
            var endDate = new DateTime(2026, 3, 10);

            // Act
            var results = _engine.CalculateProjections(
                transactions.Where(x=>x.PaycheckId.HasValue).ToList(),
                transactions.Where(x=>x.BillId.HasValue).ToList(),
                transactions.Where(x=>x.BucketId.HasValue).ToList(),startDate, endDate, accounts, paychecks, new List<Bill>(), buckets, new List<PeriodBill>(), new List<PeriodBucket>(), transactions).ToList();

            // Assert
            // Paycheck on 2/19. Next on 3/5.
            // Bucket Grayson on 2/19 should be reduced by transaction on 2/20 (same period).
            // Since 500 > 50, Grayson bucket on 2/19 should be 0.
            
            // The bucket event is projected at the END of the period.
            // Bi-weekly from 2/19 means period end is 2/19 + 14 days - 1 day = 3/4.
            var graysonBucketEntry = results.FirstOrDefault(r => r.Description.Contains("Bucket: Grayson") && r.Date == new DateTime(2026, 3, 4));
            Assert.IsNotNull(graysonBucketEntry, "Grayson bucket entry should exist");
            Assert.AreEqual(0m, graysonBucketEntry.Amount, "Grayson bucket amount should be reduced to 0 because transaction exceeds it");
        }

        [TestMethod]
        public void TestTransactionMortgagePayment_ReducesDebt()
        {
            // Arrange
            var accounts = new List<Account>
            {
                new Account 
                { 
                    Id = 1, 
                    Name = "Mortgage", 
                    Balance = 200000, 
                    Type = AccountType.Mortgage, 
                    IncludeInTotal = true, 
                    BalanceAsOf = new DateTime(2026, 2, 1),
                    MortgageDetails = new MortgageDetails
                    {
                        InterestRate = 0, // No interest for this test to keep it simple
                        Escrow = 400,
                        MortgageInsurance = 100
                    }
                },
                new Account
                {
                    Id = 2,
                    Name = "Checking",
                    Balance = 10000,
                    Type = AccountType.Checking,
                    IncludeInTotal = true,
                    BalanceAsOf = new DateTime(2026, 2, 1)
                }
            };

            // Transaction: Payment of 1500 to the mortgage account.
            // Expected principal reduction = 1500 - 400 (escrow) - 100 (insurance) = 1000.
            var transactions = new List<Transaction>
            {
                new Transaction
                {
                    Id = 101,
                    Description = "Mortgage Payment",
                    Amount = 1500,
                    Date = new DateTime(2026, 2, 15),
                    AccountId = 2, // From Checking
                    ToAccountId = 1 // To Mortgage
                }
            };

            var startDate = new DateTime(2026, 2, 1);
            var endDate = new DateTime(2026, 3, 1);

            // Act
            var results = _engine.CalculateProjections(
                transactions.Where(x=>x.PaycheckId.HasValue).ToList(),
                transactions.Where(x=>x.BillId.HasValue).ToList(),
                transactions.Where(x=>x.BucketId.HasValue).ToList(),startDate, endDate, accounts, new List<Paycheck>(), new List<Bill>(), new List<BudgetBucket>(), new List<PeriodBill>(), new List<PeriodBucket>(), transactions).ToList();

            // Assert
            var paymentEntry = results.FirstOrDefault(r => r.Description == "Mortgage Payment");
            Assert.IsNotNull(paymentEntry, "Should have a mortgage payment entry");
            
            // Checking should decrease by 1500
            Assert.AreEqual(8500m, paymentEntry.AccountBalances["Checking"], "Checking balance should decrease by full payment amount");
            
            // Mortgage should decrease by 1000 (principal)
            Assert.AreEqual(199000m, paymentEntry.AccountBalances["Mortgage"], "Mortgage balance should decrease by principal amount");
        }

        [TestMethod]
        public void TestTransactionMortgageRebalance_IncreasesDebt()
        {
            // Arrange
            var accounts = new List<Account>
            {
                new Account 
                { 
                    Id = 1, 
                    Name = "Mortgage", 
                    Balance = 200000, 
                    Type = AccountType.Mortgage, 
                    IncludeInTotal = true, 
                    BalanceAsOf = new DateTime(2026, 2, 1),
                },
                new Account
                {
                    Id = 2,
                    Name = "Checking",
                    Balance = 10000,
                    Type = AccountType.Checking,
                    IncludeInTotal = true,
                    BalanceAsOf = new DateTime(2026, 2, 1)
                }
            };

            // Transaction: Rebalance of 1500 to the mortgage account.
            // Expected debt increase = 1500.
            var transactions = new List<Transaction>
            {
                new Transaction
                {
                    Id = 101,
                    Description = "Mortgage Rebalance",
                    Amount = 1500,
                    Date = new DateTime(2026, 2, 15),
                    ToAccountId = 1, // To Mortgage
                    IsRebalance = true
                }
            };

            var startDate = new DateTime(2026, 2, 1);
            var endDate = new DateTime(2026, 3, 1);

            // Act
            var results = _engine.CalculateProjections(
                transactions.Where(x=>x.PaycheckId.HasValue).ToList(),
                transactions.Where(x=>x.BillId.HasValue).ToList(),
                transactions.Where(x=>x.BucketId.HasValue).ToList(),startDate, endDate, accounts, new List<Paycheck>(), new List<Bill>(), new List<BudgetBucket>(), new List<PeriodBill>(), new List<PeriodBucket>(), transactions).ToList();

            // Assert
            var rebalanceEntry = results.FirstOrDefault(r => r.Description == "Mortgage Rebalance");
            Assert.IsNotNull(rebalanceEntry, "Should have a mortgage rebalance entry");
            
            // Mortgage should increase by 1500
            Assert.AreEqual(201500m, rebalanceEntry.AccountBalances["Mortgage"], "Mortgage balance should increase by rebalance amount");
            
            // Total balance: Checking (10000) - Mortgage (201500) = -191500
            Assert.AreEqual(-191500m, rebalanceEntry.Balance, "Total balance should reflect the rebalance");
        }
    }
}
