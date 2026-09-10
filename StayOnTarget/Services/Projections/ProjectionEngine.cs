using System.ComponentModel.DataAnnotations;
using StayOnTarget.Models;
using StayOnTarget.ViewModels;
using Serilog;

namespace StayOnTarget.Services.Projections;

public interface IProjectionEngine {
    IEnumerable<ProjectionItem> CalculateProjections(
        List<Transaction> allPaycheckTransactions,
        List<Transaction> allBillTransactions,
        List<Transaction> allBucketTransactions,
        List<Transaction> allTransactions,
        DateTime startDate,
        DateTime endDate,
        List<Account> accounts,
        List<Paycheck> paychecks,
        List<Bill> bills,
        List<BudgetBucket> buckets,
        List<BucketPaycheckAllocation> allocations,
        List<PeriodBill> periodBills,
        List<PeriodBucket> periodBuckets,
        List<Transaction> transactions,
        List<AccountReconciliation>? reconciliations = null,
        bool showReconciled = false,
        bool removeZeroBalanceEntries = false,
        bool useAutoSweep = false,
        SnowballStrategyOptions? snowballOptions = null,
        DateTime? referenceDate = null);

    decimal GetSpendableBalance(
        int accountId,
        IReadOnlyDictionary<int, decimal> balances,
        IReadOnlyDictionary<int, decimal> floors);
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
        Sweep,
        Final,
        Snowball,
        Roth,

        [Display(Name = "Accumulating Drawdown")]
        AccumulatingDrawdown
    }

    public IEnumerable<ProjectionItem> CalculateProjections(
        List<Transaction> allPaycheckTransactions,
        List<Transaction> allBillTransactions,
        List<Transaction> allBucketTransactions,
        List<Transaction> allTransactions,
        DateTime startDate,
        DateTime endDate,
        List<Account> accounts,
        List<Paycheck> paychecks,
        List<Bill> bills,
        List<BudgetBucket> buckets,
        List<BucketPaycheckAllocation> allocations,
        List<PeriodBill> periodBills,
        List<PeriodBucket> periodBuckets,
        List<Transaction> transactions,
        List<AccountReconciliation>? reconciliations = null,
        bool showReconciled = true,
        bool removeZeroBalanceEntries = false,
        bool useAutoSweep = false,
        SnowballStrategyOptions? snowballOptions = null,
        DateTime? referenceDate = null) {
        try {
            var today = referenceDate ?? DateTime.Today;
            var bucketBalances = buckets.ToDictionary(b => b.Id, b => b.CurrentBalance);

            var effectiveSnowballOptions = snowballOptions ?? new SnowballStrategyOptions();
            var rothContributionsByYear =
                new Dictionary<int, decimal>(effectiveSnowballOptions.CurrentYearRothContributions);
            var thresholdPct = effectiveSnowballOptions.CheckingSafetyThresholdPct;

            var list = new List<ProjectionItem>();
            var current = startDate;

            var accountBalances = accounts.ToList().ToDictionary(a => a.Id, a => a.Balance);

            var accountNames = accounts.ToDictionary(a => a.Id, a => a.Name);
            var moneyAccountIds = accounts.Where(x => x.Type == AccountType.Checking || x.Type == AccountType.Savings)
                .Select(x => x.Id).ToList();
            var includedTotalAccounts = new HashSet<int>(accounts.Where(a => a.IncludeInTotal).Select(a => a.Id));

            var unbalancedPaychecks = paychecks.Where(p => !p.IsBalanced).ToList();
            if (unbalancedPaychecks.Any()) {
                DateTime earliestUnbalanced = unbalancedPaychecks.Min(p => p.StartDate);
                if (earliestUnbalanced < current) {
                    current = earliestUnbalanced;
                }
            }

            var reconLookup = new Dictionary<int, AccountReconciliation>();
            var allValidReconciliations = new List<AccountReconciliation>();
            if (reconciliations != null) {
                foreach (var recon in reconciliations.Where(r => !r.IsInvalidated)) {
                    allValidReconciliations.Add(recon);
                    if (!reconLookup.ContainsKey(recon.AccountId) ||
                        recon.ReconciledAsOfDate > reconLookup[recon.AccountId].ReconciledAsOfDate) {
                        reconLookup[recon.AccountId] = recon;
                    }
                }
            }

            var events = new List<ProjectionGridItem>();

            #region Prepare events that show in projections

            events.AddPaycheckEvents(accounts, paychecks, allPaycheckTransactions, current, endDate);
            events.AddBillEvents(accounts, bills, allBillTransactions, periodBills, current, endDate, today);
            events.AddBucketEvents(accounts, paychecks, buckets, periodBuckets, allocations, bucketBalances, current,
                endDate, today);
            events.AddTransactionEvents(allTransactions);
            events.AddInterestEvents(accounts, allTransactions, startDate, endDate);
            events.AddReconciliationEvents(allValidReconciliations);

            #endregion

            var sortedEvents = events
                .OrderBy(e => e.Date)
                .ThenByDescending(e =>
                    e.Type == ProjectionEventType.Paycheck || (e.PaycheckId.HasValue && e.ToAccountId.HasValue))
                .ThenByDescending(e => e.Type == ProjectionEventType.Paycheck)
                .ToList();

            var accountBalanceDates = accounts.ToDictionary(a => a.Id, a => a.BalanceAsOf);
            var accumulatedGrowth = accounts.ToDictionary(a => a.Id, a => 0m);
            var ccDailyBalances = accounts.Where(a => a.Type == AccountType.CreditCard).ToDictionary(a => a.Id,
                a => new List<(DateTime Date, decimal Balance, decimal InterestAccruingBalance)>());

            var ccGraceActive = accounts.Where(a => a.Type == AccountType.CreditCard)
                .ToDictionary(a => a.Id, a => a.CreditCardDetails?.GraceActive ?? true);
            var ccUnpaidStatementBalance = accounts.Where(a => a.Type == AccountType.CreditCard)
                .ToDictionary(a => a.Id, a => a.Balance <= 0.01m ? a.Balance : 0m);
            var ccPaidThisCycle = accounts.Where(a => a.Type == AccountType.CreditCard)
                .ToDictionary(a => a.Id, a => 0m);

            var ccPreviousMonthPaidInFull = accounts.Where(a => a.Type == AccountType.CreditCard)
                .ToDictionary(a => a.Id, a => a.Balance <= 0.01m);
            var mortgagePaidOff = accounts.Where(a => a.IsLoanAccount).ToDictionary(a => a.Id, a => false);

            ProjectionEngineExtensions.AdjustForReconciliations(
                accountBalances,
                accountBalanceDates,
                ccPreviousMonthPaidInFull,
                ccGraceActive,
                ccUnpaidStatementBalance,
                ccPaidThisCycle,
                ccDailyBalances,
                accounts,
                allValidReconciliations,
                sortedEvents,
                startDate);

            current = startDate;

            var runningBalance = accounts.Where(a => includedTotalAccounts.Contains(a.Id))
                .Sum(a => accountBalances[a.Id]);

            var primaryCheckingId = accounts.FirstOrDefault(a => a.Type == AccountType.Checking && a.IsPrimary)?.Id;

            var accountFloors = buckets
                .Where(b => b.Type == BucketType.UpfrontFloor && b.TargetBalance > 0)
                .GroupBy(b => b.AccountId ?? primaryCheckingId)
                .Where(g => g.Key.HasValue)
                .ToDictionary(
                    g => g.Key!.Value,
                    g => g.Sum(b => b.TargetBalance)
                );

            var lastDate = current;
            var futureEvents = sortedEvents.Where(e => e.Date >= current).ToList();

            var paycheckDates = new List<DateTime>();
            foreach (var pay in paychecks) {
                var nextPay = pay.StartDate;
                if (nextPay > startDate) {
                    while (nextPay > startDate) {
                        nextPay = pay.Frequency switch {
                            Frequency.Weekly => nextPay.AddDays(-7),
                            Frequency.BiWeekly => nextPay.AddDays(-14),
                            Frequency.Monthly => nextPay.AddMonths(-1),
                            _ => nextPay
                        };
                    }
                }

                while (nextPay <= endDate) {
                    var nextPayDate = nextPay.Date;
                    if (!paycheckDates.Contains(nextPayDate))
                        paycheckDates.Add(nextPayDate);

                    nextPay = pay.Frequency switch {
                        Frequency.Weekly => nextPay.AddDays(7),
                        Frequency.BiWeekly => nextPay.AddDays(14),
                        Frequency.Monthly => nextPay.AddMonths(1),
                        _ => nextPay.AddYears(100)
                    };
                }
            }

            paycheckDates = paycheckDates.Distinct().OrderBy(d => d).ToList();

            if (!paycheckDates.Any() || paycheckDates[0] > current) {
                paycheckDates.Insert(0, current);
            }

            var bucketSpending = new Dictionary<(DateTime PeriodDate, int BucketId), decimal>();
            foreach (var transaction in allBucketTransactions) {
                if (transaction.BucketId.HasValue) {
                    var periodDate = paycheckDates.LastOrDefault(d => d <= transaction.TransactionDate);
                    if (periodDate != DateTime.MinValue) {
                        var key = (periodDate, transaction.BucketId.Value);
                        if (!bucketSpending.ContainsKey(key)) bucketSpending[key] = 0;
                        bucketSpending[key] += Math.Abs(transaction.Amount);
                    }
                }
            }

            var primaryChecking = accounts.FirstOrDefault(a => a.Type == AccountType.Checking && a.IsPrimary)?.Id;
            var creditCardAccountIds = accounts.Where(a => a.Type == AccountType.CreditCard).Select(a => a.Id).ToList();

            if (useAutoSweep) {
                var finalDate = endDate.Date;
                if (!paycheckDates.Contains(finalDate)) {
                    paycheckDates.Add(finalDate);
                    paycheckDates = paycheckDates.OrderBy(d => d).ToList();
                }

                futureEvents.Add(new ProjectionGridItem(
                    transactionDate: finalDate.AddSeconds(1),
                    amount: 0,
                    description: "Final Period Close",
                    fromAccountId: null,
                    toAccountId: null,
                    bucketId: null,
                    paycheckId: null, paycheckOccurrenceDate: null,
                    type: ProjectionEventType.Sweep,
                    isPrincipalOnly: false,
                    isRebalance: false,
                    isInterestAdjustment: false,
                    isReconciled: false,
                    transactionId: null,
                    isSynthetic: true));
            }

            futureEvents = futureEvents.OrderBy(e => e.Date).ToList();

            var nextPaycheckIndex = paycheckDates.Count > 1 ? 1 : 0;
            var nextPaycheckDate = paycheckDates.Count > nextPaycheckIndex
                ? paycheckDates[nextPaycheckIndex]
                : DateTime.MaxValue;

            foreach (var e in futureEvents) {
                if (useAutoSweep) {
                    while (e.Date >= nextPaycheckDate && nextPaycheckDate != DateTime.MaxValue) {
                        var sweepDate = nextPaycheckDate.AddDays(-1);

                        if (sweepDate >= today && sweepDate >= startDate.Date.AddDays(-1) &&
                            nextPaycheckDate >= startDate.Date) {
                            if (primaryChecking.HasValue) {
                                var ccPeriodNewDebt = new Dictionary<int, decimal>();

                                foreach (var ccId in creditCardAccountIds) {
                                    var currentDeficit = accountBalances[ccId] < 0 ? -accountBalances[ccId] : 0m;

                                    var cardTransactions = futureEvents.Where(ev =>
                                        ev.Date > paycheckDates[Math.Max(0, nextPaycheckIndex - 1)] &&
                                        ev.Date <= sweepDate &&
                                        (ev.FromAccountId == ccId || ev.ToAccountId == ccId));

                                    decimal netFlow = 0m;
                                    foreach (var tx in cardTransactions) {
                                        if (tx.ToAccountId == ccId) {
                                            netFlow -= tx.Amount;
                                        }

                                        if (tx.FromAccountId == ccId) {
                                            netFlow += tx.Amount;
                                        }
                                    }

                                    ccPeriodNewDebt[ccId] = Math.Max(0m, netFlow > 0 ? netFlow : currentDeficit);
                                }

                                foreach (var ccId in creditCardAccountIds) {
                                    var balance = accountBalances[ccId];
                                    var netNewDebt = Math.Max(0m, ccPeriodNewDebt[ccId]);

                                    var totalBalanceDeficit = accountBalances[ccId] < 0 ? -accountBalances[ccId] : 0m;
                                    var targetSweepAmount = Math.Min(netNewDebt, totalBalanceDeficit);

                                    if (targetSweepAmount > 0.01m) {
                                        decimal spendableChecking = GetSpendableBalance(primaryChecking.Value,
                                            accountBalances, accountFloors);
                                        decimal pctSafetyThreshold = Math.Max(0m,
                                            thresholdPct * accountBalances[primaryChecking.Value]);

                                        decimal availableToSweep = spendableChecking - pctSafetyThreshold;
                                        decimal actualSweepAmount = Math.Min(targetSweepAmount, availableToSweep);

                                        if (actualSweepAmount > 0.01m) {
                                            accountBalances[primaryChecking.Value] -= actualSweepAmount;
                                            accountBalances[ccId] += actualSweepAmount;

                                            runningBalance = accounts.Where(a => includedTotalAccounts.Contains(a.Id))
                                                .Sum(a => accountBalances[a.Id]);

                                            var sweepItem = new ProjectionItem {
                                                Type = ProjectionEngine.ProjectionEventType.Sweep,
                                                TransactionDate = sweepDate,
                                                Description = $"Auto-Sweep (New Period Debt): {accountNames[ccId]}",
                                                FromAccountId = primaryChecking,
                                                ToAccountId = ccId,
                                                Amount = Math.Abs(actualSweepAmount),
                                                Balance = runningBalance,
                                                IsSynthetic = true,
                                                AccountBalances = accountBalances.ToDictionary(
                                                    kv => accountNames[kv.Key],
                                                    kv => kv.Value),
                                                InOrOutOfMoneyAccount = true
                                            };

                                            list.Add(sweepItem);
                                        }
                                    }
                                }

                                if (effectiveSnowballOptions.EnableSnowball) {
                                    SnowballStrategyProcessor.ProcessSurplus(
                                        sweepDate,
                                        effectiveSnowballOptions,
                                        accounts,
                                        accountBalances,
                                        accountNames,
                                        primaryChecking.Value,
                                        ref runningBalance,
                                        includedTotalAccounts,
                                        rothContributionsByYear,
                                        list,
                                        accountFloors);
                                }
                            }
                        }

                        nextPaycheckIndex++;
                        nextPaycheckDate = nextPaycheckIndex < paycheckDates.Count
                            ? paycheckDates[nextPaycheckIndex]
                            : DateTime.MaxValue;
                    }
                }

                ProjectionEngineExtensions.AccountForGrowthInAccountsDuringProjectedEvents(
                    lastDate,
                    ref runningBalance,
                    e,
                    accounts,
                    accountBalances,
                    accountBalanceDates,
                    accumulatedGrowth,
                    ccGraceActive,
                    ccDailyBalances,
                    includedTotalAccounts);

                lastDate = e.Date;

                if (!ProjectionEngineExtensions.AddInterestProjection(
                        list,
                        ref runningBalance,
                        e,
                        accounts,
                        accountBalances,
                        accountNames,
                        ccGraceActive,
                        ccUnpaidStatementBalance,
                        ccPaidThisCycle,
                        ccDailyBalances,
                        includedTotalAccounts,
                        startDate)) continue;

                if (!ProjectionEngineExtensions.AdjustBalanceForReconciliationBalances(ref runningBalance, e,
                        accounts,
                        accountBalances,
                        ccPreviousMonthPaidInFull,
                        includedTotalAccounts)) continue;

                #region min payment sweep for credit cards
                if (e.Type == ProjectionEngine.ProjectionEventType.Sweep && e.Description.StartsWith("Min-Pay Check")) {
                    var acc = accounts.FirstOrDefault(a => a.Id == e.FromAccountId);
                    primaryChecking =
                        accounts.FirstOrDefault(a => a.Type == AccountType.Checking && a.IsPrimary)?.Id;

                    if (acc?.CreditCardDetails != null && primaryChecking.HasValue) {
                        var currentCcBalance = accountBalances[acc.Id];
                        var minPaymentAmount = acc.CreditCardDetails.MinPayFloor;
                        var paidSoFar =
                            ccPaidThisCycle
                                [acc.Id]; // Transactions from statement day to statement+graceperiod have already run and added to this!

                        if (currentCcBalance < -0.01m && minPaymentAmount > 0 && paidSoFar < minPaymentAmount) {
                            var remainingMin = Math.Min(-currentCcBalance, minPaymentAmount - paidSoFar);
                            decimal spendableChecking =
                                GetSpendableBalance(primaryChecking.Value, accountBalances, accountFloors);
                            decimal actualSweep = Math.Min(remainingMin, spendableChecking);

                            if (actualSweep > 0.01m) {
                                accountBalances[primaryChecking.Value] -= actualSweep;
                                accountBalances[acc.Id] += actualSweep;
                                ccPaidThisCycle[acc.Id] += actualSweep;

                                runningBalance = accounts.Where(a => includedTotalAccounts.Contains(a.Id))
                                    .Sum(a => accountBalances[a.Id]);

                                list.Add(new ProjectionItem {
                                    Type = ProjectionEngine.ProjectionEventType.Sweep,
                                    TransactionDate = e.Date, // Dated on the 27th!
                                    Description = $"Min-Pay Sweep: {acc.Name}",
                                    FromAccountId = primaryChecking,
                                    ToAccountId = acc.Id,
                                    Amount = actualSweep,
                                    Balance = runningBalance,
                                    IsSynthetic = true,
                                    AccountBalances =
                                        accountBalances.ToDictionary(kv => accountNames[kv.Key], kv => kv.Value),
                                    InOrOutOfMoneyAccount = true
                                });
                            }
                        }
                    }

                    continue; // Skip standard processing for this synthetic marker
                }
                #endregion

                var currentEventAmount = e.Amount;

                if (e.ToAccountId.HasValue && mortgagePaidOff.ContainsKey(e.ToAccountId.Value) &&
                    mortgagePaidOff[e.ToAccountId.Value]) {
                    var toAcc = accounts.FirstOrDefault(a => a.Id == e.ToAccountId.Value);
                    if (toAcc?.MortgageDetails != null) {
                        var escrowOnly = toAcc.MortgageDetails.Escrow + toAcc.MortgageDetails.MortgageInsurance;
                        currentEventAmount = -escrowOnly;
                    }
                }

                if (e is { Type: ProjectionEventType.Bucket, BucketId: not null } or
                    { Type: ProjectionEventType.AccumulatingDrawdown, BucketId: not null }) {
                    var bucket = buckets.FirstOrDefault(b => b.Id == e.BucketId);
                    if (bucket == null) continue;
                    var periodDate = paycheckDates.LastOrDefault(d => d <= e.Date);
                    if (periodDate != DateTime.MinValue) {
                        var key = (periodDate, e.BucketId.Value);
                        var spent = bucketSpending.ContainsKey(key) ? bucketSpending[key] : 0;
                        var projectedAmount = Math.Abs(e.Amount);
                        if (bucket.Type != BucketType.AccumulatingDrawdown) {
                            currentEventAmount = -Math.Max(0, projectedAmount - spent);
                        }
                        else if (bucket.Type == BucketType.AccumulatingDrawdown) {
                            currentEventAmount = -Math.Max(0, projectedAmount);
                        }
                    }
                }

                if (e.ToAccountId.HasValue && accountBalances.ContainsKey(e.ToAccountId.Value)) {
                    var toAcc = accounts.FirstOrDefault(a => a.Id == e.ToAccountId.Value);
                    if (toAcc != null) {
                        var amountChange = Math.Abs(currentEventAmount);
                        var isDebt = toAcc.IsLiability;
                        var isPrincipalOnly = e.IsPrincipalOnly;
                        var isRebalance = e.IsRebalance;
                        var isInterestAdjustment =
                            (e.Type == ProjectionEventType.Transaction && e.IsInterestAdjustment);
                        var isInterestOrRebalance = isDebt && (isRebalance || isInterestAdjustment);

                        if (isInterestOrRebalance) {
                            accountBalances[e.ToAccountId.Value] -= amountChange;
                        }
                        else if (toAcc.IsLoanAccount == true) {
                            var principal = amountChange;
                            if (!isPrincipalOnly && toAcc.MortgageDetails != null) {
                                var escrowAndInsurance =
                                    toAcc.MortgageDetails.Escrow + toAcc.MortgageDetails.MortgageInsurance;
                                principal = Math.Max(0, amountChange - escrowAndInsurance);

                                if (Math.Abs(accountBalances[e.ToAccountId.Value]) <= principal) {
                                    principal = Math.Abs(accountBalances[e.ToAccountId.Value]);
                                    mortgagePaidOff[e.ToAccountId.Value] = true;
                                    currentEventAmount = (principal + escrowAndInsurance);
                                }
                            }

                            accountBalances[e.ToAccountId.Value] += principal;
                        }
                        else if (isDebt) {
                            accountBalances[e.ToAccountId.Value] += amountChange;

                            if (toAcc.Type == AccountType.CreditCard &&
                                ccPaidThisCycle.ContainsKey(e.ToAccountId.Value)) {
                                ccPaidThisCycle[e.ToAccountId.Value] += amountChange;
                            }
                        }
                        else {
                            accountBalances[e.ToAccountId.Value] += amountChange;
                        }
                    }
                }

                var effectiveFromAccountId = e.FromAccountId ??
                                             ((e.Type == ProjectionEventType.Bill ||
                                               e.Type == ProjectionEventType.Bucket ||
                                               e.Type == ProjectionEventType.AccumulatingDrawdown ||
                                               e.Type == ProjectionEventType.Transfer)
                                                 ? primaryChecking
                                                 : null);

                if (effectiveFromAccountId.HasValue && accountBalances.ContainsKey(effectiveFromAccountId.Value)) {
                    var amountChange = Math.Abs(currentEventAmount);
                    accountBalances[effectiveFromAccountId.Value] -= amountChange;
                }

                runningBalance = accounts.Where(a => includedTotalAccounts.Contains(a.Id))
                    .Sum(a => accountBalances[a.Id]);

                var item = new ProjectionItem {
                    Type = e.Type,
                    ToAccountId = e.ToAccountId,
                    FromAccountId = e.FromAccountId,
                    TransactionDate = e.Date,
                    Description = e.Description,
                    PaycheckId = e.PaycheckId,
                    Amount = Math.Abs(currentEventAmount),
                    Balance = runningBalance,
                    AccountBalances = accountBalances.ToDictionary(kv => accountNames[kv.Key], kv => kv.Value),
                    BillId = e.BillId,
                    BucketId = e.BucketId,
                    SubCategoryId = e.SubCategoryId,
                };

                var effectiveFrom = e.FromAccountId ??
                                    ((e.Type == ProjectionEventType.Bill || e.Type == ProjectionEventType.Bucket ||
                                      e.Type == ProjectionEventType.AccumulatingDrawdown ||
                                      e.Type == ProjectionEventType.Transfer)
                                        ? primaryChecking
                                        : null);

                var targetAccountId = effectiveFrom ?? e.ToAccountId;

                if (targetAccountId.HasValue && accountBalances.ContainsKey(targetAccountId.Value)) {
                    var actualBalance = accountBalances[targetAccountId.Value];
                    var floorTarget = accountFloors.TryGetValue(targetAccountId.Value, out var fl) ? fl : 0m;

                    var rawSpendable = actualBalance - floorTarget;
                    item.SpendableBalance = Math.Max(0m, rawSpendable);

                    if (floorTarget > 0m && actualBalance < floorTarget) {
                        var breachAmount = floorTarget - actualBalance;
                        var accName = accountNames.TryGetValue(targetAccountId.Value, out var name) ? name : "Account";

                        item.IsBelowFloor = true;
                        item.IsWarning = true;
                        item.WarningMessage =
                            $"{accName} breached floor reserve by {breachAmount:C2} (Balance: {actualBalance:C2}, Floor Target: {floorTarget:C2})";
                    }
                }

                if (e.FromAccountId != null && moneyAccountIds.Contains(e.FromAccountId.Value) ||
                    (e.ToAccountId != null && moneyAccountIds.Contains(e.ToAccountId.Value))) {
                    item.InOrOutOfMoneyAccount = true;
                }

                if (e.FromAccountId != null && moneyAccountIds.Contains(e.FromAccountId.Value)) {
                    item.OutOfMoneyAccount = true;
                }

                if (e.ToAccountId != null && moneyAccountIds.Contains(e.ToAccountId.Value)) {
                    item.InMoneyAccount = true;
                }

                if (e.FromAccountId != null && moneyAccountIds.Contains(e.FromAccountId.Value) &&
                    (e.ToAccountId != null && moneyAccountIds.Contains(e.ToAccountId.Value))) {
                    item.InternalTransfer = true;
                }

                list.Add(item);
            }

            if (useAutoSweep) {
                while (nextPaycheckDate != DateTime.MaxValue && nextPaycheckDate <= endDate) {
                    var sweepDate = nextPaycheckDate.AddDays(-1);
                    if (sweepDate >= today) {
                        if (primaryChecking.HasValue) {
                            var ccPeriodNewDebt = new Dictionary<int, decimal>();

                            foreach (var ccId in creditCardAccountIds) {
                                var currentDeficit = accountBalances[ccId] < 0 ? -accountBalances[ccId] : 0m;

                                var cardTransactions = futureEvents.Where(ev =>
                                    ev.Date > paycheckDates[Math.Max(0, nextPaycheckIndex - 1)] &&
                                    ev.Date <= sweepDate &&
                                    (ev.FromAccountId == ccId || ev.ToAccountId == ccId));

                                decimal netFlow = 0m;
                                foreach (var tx in cardTransactions) {
                                    if (tx.ToAccountId == ccId) {
                                        netFlow -= tx.Amount;
                                    }

                                    if (tx.FromAccountId == ccId) {
                                        netFlow += tx.Amount;
                                    }
                                }

                                ccPeriodNewDebt[ccId] = Math.Max(0m, netFlow > 0 ? netFlow : currentDeficit);
                            }

                            foreach (var ccId in creditCardAccountIds) {
                                var netNewDebt = Math.Max(0m, ccPeriodNewDebt[ccId]);

                                var balance = accountBalances[ccId];
                                var totalBalanceDeficit = accountBalances[ccId] < 0 ? -accountBalances[ccId] : 0m;
                                var targetSweepAmount = Math.Min(netNewDebt, totalBalanceDeficit);

                                if (targetSweepAmount > 0.01m) {
                                    decimal spendableChecking = GetSpendableBalance(primaryChecking.Value,
                                        accountBalances,
                                        accountFloors);
                                    decimal pctSafetyThreshold = Math.Max(0m,
                                        thresholdPct * accountBalances[primaryChecking.Value]);

                                    decimal availableToSweep = spendableChecking - pctSafetyThreshold;
                                    decimal actualSweepAmount = Math.Min(targetSweepAmount, availableToSweep);

                                    if (actualSweepAmount > 0.01m) {
                                        accountBalances[primaryChecking.Value] -= actualSweepAmount;
                                        accountBalances[ccId] += actualSweepAmount;

                                        runningBalance = accounts.Where(a => includedTotalAccounts.Contains(a.Id))
                                            .Sum(a => accountBalances[a.Id]);

                                        var sweepItem = new ProjectionItem {
                                            Type = ProjectionEngine.ProjectionEventType.Sweep,
                                            TransactionDate = sweepDate,
                                            Description = $"Auto-Sweep (New Period Debt): {accountNames[ccId]}",
                                            FromAccountId = primaryChecking,
                                            ToAccountId = ccId,
                                            Amount = Math.Abs(actualSweepAmount),
                                            Balance = runningBalance,
                                            IsSynthetic = true,
                                            AccountBalances = accountBalances.ToDictionary(kv => accountNames[kv.Key],
                                                kv => kv.Value),
                                            InOrOutOfMoneyAccount = true
                                        };

                                        list.Add(sweepItem);
                                    }
                                }
                            }

                            if (effectiveSnowballOptions.EnableSnowball) {
                                SnowballStrategyProcessor.ProcessSurplus(
                                    sweepDate,
                                    effectiveSnowballOptions,
                                    accounts,
                                    accountBalances,
                                    accountNames,
                                    primaryChecking.Value,
                                    ref runningBalance,
                                    includedTotalAccounts,
                                    rothContributionsByYear,
                                    list,
                                    accountFloors);
                            }
                        }
                    }

                    nextPaycheckIndex++;
                    nextPaycheckDate = nextPaycheckIndex < paycheckDates.Count
                        ? paycheckDates[nextPaycheckIndex]
                        : DateTime.MaxValue;
                }
            }

            for (var i = 0; i < paycheckDates.Count; i++) {
                var start = paycheckDates[i];
                var next = (i + 1 < paycheckDates.Count) ? paycheckDates[i + 1] : endDate;
                var periodItems = list.Where(item =>
                    item.TransactionDate >= start && item.TransactionDate < next && item.InOrOutOfMoneyAccount &&
                    !item.InternalTransfer && !(item.IsSweep || item.IsSynthetic)).ToList();
                if (periodItems.Count != 0) {
                    periodItems.First().PeriodNet =
                        periodItems.Sum(item => item.OutOfMoneyAccount ? -item.Amount : item.Amount);
                }
            }

            if (useAutoSweep && endDate >= today) {
                foreach (var ccId in creditCardAccountIds) {
                    var balance = accountBalances[ccId];
                    if (balance < 0 && primaryChecking.HasValue) {
                        var sweepAmount = -balance;

                        decimal spendableChecking =
                            GetSpendableBalance(primaryChecking.Value, accountBalances, accountFloors);
                        decimal pctSafetyThreshold =
                            Math.Max(0m, thresholdPct * accountBalances[primaryChecking.Value]);

                        decimal availableToSweep = spendableChecking - pctSafetyThreshold;

                        decimal actualSweepAmount = Math.Min(sweepAmount, availableToSweep);

                        if (actualSweepAmount > 0) {
                            accountBalances[primaryChecking.Value] -= actualSweepAmount;
                            accountBalances[ccId] += actualSweepAmount;

                            runningBalance = accounts.Where(a => includedTotalAccounts.Contains(a.Id))
                                .Sum(a => accountBalances[a.Id]);

                            var sweepItem = new ProjectionItem {
                                Type = ProjectionEngine.ProjectionEventType.Sweep,
                                TransactionDate = endDate,
                                Description = $"Auto-Sweep: {accountNames[ccId]}",
                                FromAccountId = primaryChecking,
                                ToAccountId = ccId,
                                Amount = Math.Abs(actualSweepAmount),
                                Balance = runningBalance,
                                IsSynthetic = true,
                                AccountBalances =
                                    accountBalances.ToDictionary(kv => accountNames[kv.Key], kv => kv.Value),
                                InOrOutOfMoneyAccount = true
                            };

                            list.Add(sweepItem);
                        }
                    }
                }
            }

            if (lastDate < endDate) {
                ProjectionEngineExtensions.AccountForGrowthInRemainderOfProjection(
                    lastDate,
                    endDate,
                    ref runningBalance,
                    accounts,
                    accountBalances,
                    accountBalanceDates,
                    accumulatedGrowth,
                    includedTotalAccounts);

                list.Add(new ProjectionItem {
                    Type = ProjectionEngine.ProjectionEventType.Final,
                    TransactionDate = endDate,
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
        catch (Exception ex) {
            Log.Error(ex, "Error calculating projections in ProjectionEngine.");

            return new List<ProjectionItem>();
        }
    }

    public decimal GetSpendableBalance(
        int accountId,
        IReadOnlyDictionary<int, decimal> balances,
        IReadOnlyDictionary<int, decimal> floors) {
        try {
            var actual = balances.TryGetValue(accountId, out var bal) ? bal : 0m;
            var floor = floors.TryGetValue(accountId, out var fl) ? fl : 0m;

            return actual - floor;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error getting spendable balance in ProjectionEngine.");

            return 0m;
        }
    }
}