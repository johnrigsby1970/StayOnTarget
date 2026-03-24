using StayOnTarget.Models;
using StayOnTarget.ViewModels;

namespace StayOnTarget.Services.Projections;

public interface IProjectionEngine {
    IEnumerable<ProjectionItem> CalculateProjections(
        List<Transaction> allPaycheckTransactions,
        List<Transaction> allBillTransactions,
        List<Transaction> allBucketTransactions,
        DateTime startDate,
        DateTime endDate,
        List<Account> accounts,
        List<Paycheck> paychecks,
        List<Bill> bills,
        List<BudgetBucket> buckets,
        List<PeriodBill> periodBills,
        List<PeriodBucket> periodBuckets,
        List<Transaction> transactions,
        List<AccountReconciliation>? reconciliations = null,
        bool showReconciled = false,
        bool removeZeroBalanceEntries = false);
}

public class ProjectionEngine : IProjectionEngine {
    public enum ProjectionEventType {
        Paycheck,
        Bill,
        Transfer,
        Bucket,
        Transaction,
        Interest,
        Growth,
        Reconciliation,
        Final
    }

    public IEnumerable<ProjectionItem> CalculateProjections(
        List<Transaction> allPaycheckTransactions,
        List<Transaction> allBillTransactions,
        List<Transaction> allBucketTransactions,
        DateTime startDate,
        DateTime endDate,
        List<Account> accounts,
        List<Paycheck> paychecks,
        List<Bill> bills,
        List<BudgetBucket> buckets,
        List<PeriodBill> periodBills,
        List<PeriodBucket> periodBuckets,
        List<Transaction> transactions,
        List<AccountReconciliation>? reconciliations = null, bool showReconciled = false,
        bool removeZeroBalanceEntries = false) {
        var list = new List<ProjectionItem>();
        var current = startDate;

        var accountBalances = accounts.ToList().ToDictionary(a => a.Id, a => a.Balance);
        var accountNames = accounts.ToDictionary(a => a.Id, a => a.Name);

        var includedTotalAccounts = new HashSet<int>(accounts.Where(a => a.IncludeInTotal).Select(a => a.Id));

        // Build reconciliation lookup: latest valid reconciliation per account
        var reconLookup = new Dictionary<int, AccountReconciliation>();
        var allValidReconciliations = new List<AccountReconciliation>();
        if (reconciliations != null && !showReconciled) {
            foreach (var recon in reconciliations.Where(r => !r.IsInvalidated)) {
                allValidReconciliations.Add(recon);
                if (!reconLookup.ContainsKey(recon.AccountId) ||
                    recon.ReconciledAsOfDate > reconLookup[recon.AccountId].ReconciledAsOfDate) {
                    reconLookup[recon.AccountId] = recon;
                }
            }
        }

        if (showReconciled) {
            var unbalancedPaychecks = paychecks.Where(p => !p.IsBalanced).ToList();
            if (unbalancedPaychecks.Any()) {
                DateTime earliestUnbalanced = unbalancedPaychecks.Min(p => p.StartDate);
                if (earliestUnbalanced < current) {
                    current = earliestUnbalanced;
                }
            }
        }
        else {
            var oldestUnreconciledAccount = accounts.Where(a => !reconLookup.ContainsKey(a.Id))
                .OrderBy(a => a.BalanceAsOf).FirstOrDefault();
            var oldestReconciledAccount = accounts.Where(a => reconLookup.ContainsKey(a.Id))
                .OrderBy(a => reconLookup[a.Id].ReconciledAsOfDate).FirstOrDefault();

            if (oldestUnreconciledAccount != null && (oldestReconciledAccount == null ||
                                                      oldestUnreconciledAccount.BalanceAsOf <
                                                      reconLookup[oldestReconciledAccount.Id].ReconciledAsOfDate)) {
                if (oldestUnreconciledAccount.BalanceAsOf < current) {
                    current = oldestUnreconciledAccount.BalanceAsOf;
                }
            }
            else if (oldestReconciledAccount != null) {
                var latestRecon = reconLookup[oldestReconciledAccount.Id];
                if (latestRecon.ReconciledAsOfDate < current) {
                    current = latestRecon.ReconciledAsOfDate;
                }
            }
        }

        var events =
            new List<ProjectionGridItem>();

        #region Prepare events that show in projections

        //Events will be manipulated and adjusted later
        // 1. Create events for Paychecks
        events.AddPaycheckEvents(accounts, paychecks, allPaycheckTransactions, current, endDate);

        // 2. Create events for Bills & Transfers
        events.AddBillEvents(accounts, bills, allBillTransactions, periodBills, current, endDate);

        // 3. Create events for Buckets
        events.AddBucketEvents(accounts, paychecks, buckets, periodBuckets, current, endDate);

        // 4. Create events for Transactions
        events.AddTransactionEvents(transactions, showReconciled);

        // 5. Create events or Interest & Growth Setup
        events.AddInterestEvents(accounts, transactions, startDate, endDate);

        // 6. Create events for reconciliations (points when an account balance is reset and verified so that balances do not have to run from the very beginning)
        events.AddReconciliationEvents(allValidReconciliations);

        #endregion

        var sortedEvents = events
            .OrderBy(e => e.Date)
            .ThenByDescending(e =>
                e.Type == ProjectionEventType.Paycheck || (e.PaycheckId.HasValue && e.ToAccountId.HasValue))
            .ThenByDescending(e => e.Type == ProjectionEventType.Paycheck)
            .ToList();

        // 6. Tracking variables
        var accountBalanceDates = accounts.ToDictionary(a => a.Id, a => a.BalanceAsOf);
        var accumulatedGrowth = accounts.ToDictionary(a => a.Id, a => 0m);
        var ccDailyBalances = accounts.Where(a => a.Type == AccountType.CreditCard).ToDictionary(a => a.Id,
            a => new List<(DateTime Date, decimal Balance, decimal InterestAccruingBalance)>());
        var ccPreviousMonthPaidInFull = accounts.Where(a => a.Type == AccountType.CreditCard)
            .ToDictionary(a => a.Id, a => a.Balance <= 0);
        var mortgagePaidOff = accounts.Where(a => a.Type == AccountType.Mortgage).ToDictionary(a => a.Id, a => false);

        // Recalculate accountBalances based on BalanceAsOf (or reconciliation) and events before 'current'
        //Account balance to start projection is either the balanceAsOf date of the account or the reconciledAsOf date of the account.
        //The balance could require determining past events impact on balance up to the point the projection starts.
        //Reconciliations help by setting the balance at a point in time so that the projection can start from a known balance.
        ProjectionEngineExtensions.AdjustForReconciliations(
            accountBalances,
            accountBalanceDates,
            ccPreviousMonthPaidInFull,
            accounts,
            allValidReconciliations,
            sortedEvents, current);

        var runningBalance = accounts.Where(a => includedTotalAccounts.Contains(a.Id)).Sum(a => {
            var bal = accountBalances[a.Id];
            return (a.Type == AccountType.Mortgage || a.Type == AccountType.PersonalLoan ||
                    a.Type == AccountType.CreditCard)
                ? -bal
                : bal;
        });

        var lastDate = current;
        var futureEvents = sortedEvents.Where(e => e.Date >= current).ToList();
        var paycheckDates = futureEvents
            .Where(e => e.Type == ProjectionEventType.Paycheck ||
                        (e.Type == ProjectionEventType.Transaction && e.PaycheckId.HasValue)).Select(e => e.Date)
            .Distinct()
            .OrderBy(d => d).ToList();

        if (!paycheckDates.Any() || paycheckDates[0] > current) {
            paycheckDates.Insert(0, current);
        }

        //How much has been spent associated with a bucket up to this point.
        var bucketSpending = new Dictionary<(DateTime PeriodDate, int BucketId), decimal>();
        foreach (var transaction in allBucketTransactions) {
            if (transaction.BucketId.HasValue) {
                var periodDate = paycheckDates.LastOrDefault(d => d <= transaction.Date);
                if (periodDate != DateTime.MinValue) {
                    var key = (periodDate, transaction.BucketId.Value);
                    if (!bucketSpending.ContainsKey(key)) bucketSpending[key] = 0;
                    bucketSpending[key] += Math.Abs(transaction.Amount);
                }
            }
        }

        foreach (var e in futureEvents) {
            var days = (e.Date - lastDate).Days;
            if (days > 0) {
                for (var d = 0; d < days; d++) {
                    var dayDate = lastDate.AddDays(d);
                    foreach (var acc in accounts.Where(a =>
                                 a.AnnualGrowthRate > 0 && a.Type != AccountType.Mortgage &&
                                 a.Type != AccountType.CreditCard)) {
                        if (dayDate < accountBalanceDates[acc.Id]) continue;
                        var dailyRate = acc.AnnualGrowthRate / 100m / 365m;
                        var growth = accountBalances[acc.Id] * dailyRate;
                        accumulatedGrowth[acc.Id] += growth;
                        if (accumulatedGrowth[acc.Id] >= 0.01m || accumulatedGrowth[acc.Id] <= -0.01m) {
                            decimal toAdd = Math.Round(accumulatedGrowth[acc.Id], 2);
                            accountBalances[acc.Id] += toAdd;
                            if (includedTotalAccounts.Contains(acc.Id)) {
                                if (acc.Type == AccountType.Mortgage || acc.Type == AccountType.PersonalLoan) {
                                    runningBalance -= toAdd;
                                }
                                else {
                                    runningBalance += toAdd;
                                }
                            }

                            accumulatedGrowth[acc.Id] -= toAdd;
                        }
                    }

                    foreach (var acc in accounts.Where(a => a.Type == AccountType.CreditCard)) {
                        ccDailyBalances[acc.Id].Add((dayDate, accountBalances[acc.Id], accountBalances[acc.Id]));
                    }
                }
            }

            lastDate = e.Date;

            // Apply Interest, add in interest events
            if (!ProjectionEngineExtensions.AddInterestProjection(
                    list,
                    ref runningBalance,
                    e,
                    accounts,
                    accountBalances,
                    accountNames,
                    ccPreviousMonthPaidInFull,
                    ccDailyBalances,
                    includedTotalAccounts)) continue;

            // Apply Reconciliation, be sure to get the latest reconciliation balance as each event is handled
            //Don't add reconciliation events to final projection. We want to make sure balances account for out-of-band changes
            //to accounts in other systems. There could be fixes made to account balances from other systems not stringlty
            //reflected in transactions in this system. Keep things aligned.
            if (!ProjectionEngineExtensions.AdjustBalanceForReconciliationBalances(ref runningBalance, e,
                    accounts,
                    accountBalances,
                    ccPreviousMonthPaidInFull,
                    includedTotalAccounts)) continue;

            // Apply balances
            var currentEventAmount = e.Amount;

            // Handle Mortgage Payoff Adjustment
            if (e.ToAccountId.HasValue && mortgagePaidOff.ContainsKey(e.ToAccountId.Value) &&
                mortgagePaidOff[e.ToAccountId.Value]) {
                var toAcc = accounts.FirstOrDefault(a => a.Id == e.ToAccountId.Value);
                if (toAcc?.MortgageDetails != null) {
                    var escrowOnly = toAcc.MortgageDetails.Escrow + toAcc.MortgageDetails.MortgageInsurance;
                    currentEventAmount = -escrowOnly;
                }
            }

            if (e.Type == ProjectionEventType.Bucket && e.BucketId.HasValue) {
                var periodDate = paycheckDates.LastOrDefault(d => d <= e.Date);
                if (periodDate != DateTime.MinValue) {
                    var key = (periodDate, e.BucketId.Value);
                    var spent = bucketSpending.ContainsKey(key) ? bucketSpending[key] : 0;
                    var projectedAmount = Math.Abs(e.Amount);
                    currentEventAmount = -Math.Max(0, projectedAmount - spent);
                }
            }

            if (e.ToAccountId.HasValue && accountBalances.ContainsKey(e.ToAccountId.Value)) {
                var toAcc = accounts.FirstOrDefault(a => a.Id == e.ToAccountId.Value);
                var amountChange = Math.Abs(currentEventAmount);
                var isMortgagePayment = toAcc != null && toAcc.Type == AccountType.Mortgage;
                var isPersonalLoanPayment = toAcc != null && toAcc.Type == AccountType.PersonalLoan;
                var isCreditCardPayment = toAcc != null && toAcc.Type == AccountType.CreditCard;
                var isPrincipalOnly = e.IsPrincipalOnly;
                var isRebalance = e.IsRebalance;
                var isInterestAdjustment = (e.Type == ProjectionEventType.Transaction && e.IsInterestAdjustment);
                var isInterestOrRebalance =
                    (isMortgagePayment || isCreditCardPayment) && (isRebalance || isInterestAdjustment);

                if (isInterestOrRebalance) {
                    accountBalances[e.ToAccountId.Value] += amountChange;
                    if (includedTotalAccounts.Contains(e.ToAccountId.Value)) {
                        runningBalance -= amountChange;
                    }
                }
                else if (isMortgagePayment) {
                    decimal principal = amountChange;
                    if (!isPrincipalOnly && toAcc!.MortgageDetails != null) {
                        decimal escrowAndInsurance =
                            toAcc.MortgageDetails.Escrow + toAcc.MortgageDetails.MortgageInsurance;
                        principal = amountChange - escrowAndInsurance;
                        if (principal < 0) principal = 0;

                        // Check if this payment pays off the mortgage
                        if (accountBalances[e.ToAccountId.Value] <= principal) {
                            // Capping principal to remaining balance
                            principal = accountBalances[e.ToAccountId.Value];
                            mortgagePaidOff[e.ToAccountId.Value] = true;
                            // If it's a bill/transfer, we might want to adjust the amount taken from FromAccount too,
                            // but the requirement says: "any remainder simply not being taken from the from account"
                            // This means currentEventAmount should be adjusted.
                            currentEventAmount = -(principal + escrowAndInsurance);
                        }
                    }

                    accountBalances[e.ToAccountId.Value] -= principal;
                    if (includedTotalAccounts.Contains(e.ToAccountId.Value)) {
                        runningBalance += principal;
                    }
                }
                else if (isPersonalLoanPayment && (e.Type == ProjectionEventType.Transaction && isPrincipalOnly)) {
                    accountBalances[e.ToAccountId.Value] -= amountChange;
                    if (includedTotalAccounts.Contains(e.ToAccountId.Value)) {
                        runningBalance += amountChange;
                    }
                }
                else if (isPersonalLoanPayment && (e.Type == ProjectionEventType.Transaction && isRebalance)) {
                    accountBalances[e.ToAccountId.Value] += amountChange;
                    if (includedTotalAccounts.Contains(e.ToAccountId.Value)) {
                        runningBalance -= amountChange;
                    }
                }
                else if (isCreditCardPayment) {
                    accountBalances[e.ToAccountId.Value] -= amountChange;
                    if (includedTotalAccounts.Contains(e.ToAccountId.Value)) {
                        runningBalance += amountChange;
                    }
                }
                else if (toAcc != null &&
                         (toAcc.Type == AccountType.Mortgage || toAcc.Type == AccountType.PersonalLoan)) {
                    accountBalances[e.ToAccountId.Value] += amountChange;
                    if (includedTotalAccounts.Contains(e.ToAccountId.Value)) {
                        runningBalance -= amountChange;
                    }
                }
                else {
                    accountBalances[e.ToAccountId.Value] += amountChange;
                    if (includedTotalAccounts.Contains(e.ToAccountId.Value)) {
                        runningBalance += amountChange;
                    }
                }
            }

            if (e.FromAccountId.HasValue && accountBalances.ContainsKey(e.FromAccountId.Value)) {
                var fromAcc = accounts.FirstOrDefault(a => a.Id == e.FromAccountId.Value);
                var amountChange = Math.Abs(currentEventAmount);
                if (fromAcc != null && (fromAcc.Type == AccountType.Mortgage ||
                                        fromAcc.Type == AccountType.PersonalLoan ||
                                        fromAcc.Type == AccountType.CreditCard)) {
                    accountBalances[e.FromAccountId.Value] += amountChange;
                    if (includedTotalAccounts.Contains(e.FromAccountId.Value)) {
                        runningBalance -= amountChange;
                    }
                }
                else {
                    accountBalances[e.FromAccountId.Value] -= amountChange;
                    if (includedTotalAccounts.Contains(e.FromAccountId.Value)) {
                        runningBalance -= amountChange;
                    }
                }
            }

            var item = new ProjectionItem {
                Date = e.Date,
                Description = e.Description,
                PaycheckId = e.PaycheckId,
                Amount = currentEventAmount,
                Balance = runningBalance,
                AccountBalances = accountBalances.ToDictionary(kv => accountNames[kv.Key], kv => kv.Value)
            };
            list.Add(item);
        }

        for (var i = 0; i < paycheckDates.Count; i++) {
            var start = paycheckDates[i];
            var next = (i + 1 < paycheckDates.Count) ? paycheckDates[i + 1] : endDate;
            var periodItems = list.Where(item => item.Date >= start && item.Date < next).ToList();
            if (periodItems.Count != 0) {
                periodItems.First().PeriodNet = periodItems.Sum(item => item.Amount);
            }
        }

        if (lastDate < endDate) {
            var remainingDays = (endDate - lastDate).Days;
            if (remainingDays > 0) {
                for (var d = 0; d < remainingDays; d++) {
                    var dayDate = lastDate.AddDays(d);
                    foreach (var acc in accounts.Where(a =>
                                 a.AnnualGrowthRate > 0 && a.Type != AccountType.Mortgage &&
                                 a.Type != AccountType.CreditCard)) {
                        if (dayDate < accountBalanceDates[acc.Id]) continue;
                        var dailyRate = acc.AnnualGrowthRate / 100m / 365m;
                        var growth = accountBalances[acc.Id] * dailyRate;
                        accumulatedGrowth[acc.Id] += growth;
                        if (accumulatedGrowth[acc.Id] >= 0.01m || accumulatedGrowth[acc.Id] <= -0.01m) {
                            var toAdd = Math.Round(accumulatedGrowth[acc.Id], 2);
                            accountBalances[acc.Id] += toAdd;
                            if (includedTotalAccounts.Contains(acc.Id)) {
                                if (acc.Type == AccountType.Mortgage || acc.Type == AccountType.PersonalLoan) {
                                    runningBalance -= toAdd;
                                }
                                else {
                                    runningBalance += toAdd;
                                }
                            }

                            accumulatedGrowth[acc.Id] -= toAdd;
                        }
                    }
                }
            }

            list.Add(new ProjectionItem {
                Date = endDate,
                Description = "End of Projection",
                Amount = 0,
                Balance = runningBalance,
                AccountBalances = accountBalances.ToDictionary(kv => accountNames[kv.Key], kv => kv.Value)
            });
        }

        if (removeZeroBalanceEntries) {
            list = list.Where(x => x.Amount != 0).ToList();
        }

        return list;
    }
}