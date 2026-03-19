using Microsoft.VisualStudio.TestTools.UnitTesting;
using StayOnTarget.Models;
using StayOnTarget.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StayOnTarget.Tests
{
    [TestClass]
    public class ReconciliationBugTests
    {
        private ProjectionEngine _engine = null!;

        [TestInitialize]
        public void Setup()
        {
            _engine = new ProjectionEngine();
        }

        [TestMethod]
        public void TestMultipleReconciliations_ShouldResetBalanceEachTime()
        {
            // Arrange
            var accounts = new List<Account>
            {
                new Account { Id = 1, Name = "Checking", Balance = 1000, IncludeInTotal = true, BalanceAsOf = new DateTime(2026, 1, 1), Type = AccountType.Checking }
            };

            // Two reconciliations: 
            // 1. On Jan 15, balance is 1500
            // 2. On Feb 15, balance is 2500
            var reconciliations = new List<AccountReconciliation>
            {
                new AccountReconciliation { AccountId = 1, ReconciledAsOfDate = new DateTime(2026, 1, 15), ReconciledBalance = 1500 },
                new AccountReconciliation { AccountId = 1, ReconciledAsOfDate = new DateTime(2026, 2, 15), ReconciledBalance = 2500 }
            };

            // A transaction on Feb 1st that should be "overridden" by the Feb 15th reconciliation
            var transactions = new List<Transaction>
            {
                new Transaction { Id = 1, AccountId = 1, Amount = -100, Date = new DateTime(2026, 2, 1), Description = "Gas" }
            };

            var startDate = new DateTime(2026, 1, 1);
            var endDate = new DateTime(2026, 3, 1);

            // Act
            var results = _engine.CalculateProjections(
                new List<Transaction>(),
                new List<Transaction>(),
                new List<Transaction>(),
                startDate, endDate, accounts, new List<Paycheck>(), new List<Bill>(), new List<BudgetBucket>(), new List<PeriodBill>(), new List<PeriodBucket>(), 
                transactions, 
                reconciliations).ToList();

            // Assert
            // Find a date after Feb 15
            var afterFeb15 = results.FirstOrDefault(r => r.Date >= new DateTime(2026, 2, 15));
            Assert.IsNotNull(afterFeb15, "Should have results after Feb 15");
            
            // Expected behavior: 
            // At start (Jan 1): 1000
            // Jan 15: Reconciliation resets to 1500
            // Feb 1: Transaction -100 -> 1400
            // Feb 15: Reconciliation resets to 2500
            // After Feb 15: Balance should be based on 2500
            
            // Current (Buggy) behavior:
            // ONLY the Feb 15 reconciliation is used as starting point.
            // Effective Balance = 2500, Effective Date = Feb 15.
            // Since startDate (Jan 1) < Feb 15, it projects from Jan 1.
            // It might incorrectly apply Jan 1 - Feb 15 transactions to the 2500 balance if not careful, 
            // OR it just uses 2500 from the start.
            
            // Actually, in current code:
            // reconLookup[1] = Feb 15 (2500)
            // effectiveBalance = 2500, effectiveBalanceDate = Feb 15
            // accountBalances[1] = 2500
            // priorEvents = sortedEvents where Date >= Feb 15 and Date < Jan 1 (NONE)
            // Then it projects futureEvents where Date >= Jan 1.
            // Feb 1 transaction is applied to 2500 -> 2400.
            // So after Feb 15, balance will be 2400 (Incorrect, should be 2500 or more if there are later events).
            
            Assert.AreEqual(2500m, afterFeb15.AccountBalances["Checking"], "Balance after Feb 15 should be 2500 as per latest reconciliation");
        }

        [TestMethod]
        public void TestReconciliation_DebtAccount_ShouldResetBalance()
        {
            // Arrange
            var accounts = new List<Account>
            {
                new Account { Id = 1, Name = "CreditCard", Balance = 1000, IncludeInTotal = true, BalanceAsOf = new DateTime(2026, 1, 1), Type = AccountType.CreditCard }
            };

            // Reconciliation on Jan 15: balance is 500 (Debt decreased)
            var reconciliations = new List<AccountReconciliation>
            {
                new AccountReconciliation { AccountId = 1, ReconciledAsOfDate = new DateTime(2026, 1, 15), ReconciledBalance = 500 }
            };

            var startDate = new DateTime(2026, 1, 1);
            var endDate = new DateTime(2026, 3, 1);

            // Act
            var results = _engine.CalculateProjections(
                new List<Transaction>(),
                new List<Transaction>(),
                new List<Transaction>(),
                startDate, endDate, accounts, new List<Paycheck>(), new List<Bill>(), new List<BudgetBucket>(), new List<PeriodBill>(), new List<PeriodBucket>(), 
                new List<Transaction>(), 
                reconciliations).ToList();

            // Assert
            var afterJan15 = results.FirstOrDefault(r => r.Date >= new DateTime(2026, 1, 15));
            Assert.IsNotNull(afterJan15);
            Assert.AreEqual(500m, afterJan15.AccountBalances["CreditCard"]);
            // Debt of 500 should mean total balance is -500
            Assert.AreEqual(-500m, afterJan15.Balance);
        }
    }
}
