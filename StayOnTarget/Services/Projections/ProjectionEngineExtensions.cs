using StayOnTarget.Models;
using StayOnTarget.ViewModels;
using Serilog;

namespace StayOnTarget.Services.Projections;

public static class ProjectionEngineExtensions {
    public static void AccountForGrowthInAccountsDuringProjectedEvents(
        DateTime lastDate,
        ref decimal runningBalance,
        ProjectionGridItem e,
        List<Account> accounts,
        Dictionary<int, decimal> accountBalances,
        Dictionary<int, DateTime> accountBalanceDates,
        Dictionary<int, decimal> accumulatedGrowth,
        Dictionary<int, bool> ccGraceActive,
        Dictionary<int, List<(DateTime TransactionDate, decimal Balance, decimal InterestAccruingBalance)>>
            ccDailyBalances,
        HashSet<int> includedTotalAccounts) {
        try {
            var days = (e.Date - lastDate).Days;
            if (days > 0) {
                for (var d = 0; d < days; d++) {
                    var dayDate = lastDate.AddDays(d);
                    foreach (var acc in accounts.Where(a =>
                                 a.AnnualGrowthRate > 0 && !a.IsLiability)) {
                        if (dayDate < accountBalanceDates[acc.Id]) continue;
                        var dailyRate = acc.AnnualGrowthRate / 100m / 365m;
                        var growth = accountBalances[acc.Id] * dailyRate;
                        accumulatedGrowth[acc.Id] += growth;
                        if (accumulatedGrowth[acc.Id] >= 0.01m || accumulatedGrowth[acc.Id] <= -0.01m) {
                            decimal toAdd = Math.Round(accumulatedGrowth[acc.Id], 2);
                            accountBalances[acc.Id] += toAdd;
                            if (includedTotalAccounts.Contains(acc.Id)) {
                                if (acc.IsLoanAccount) {
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
                        decimal accruingBalance = accountBalances[acc.Id];
                        if (ccGraceActive.ContainsKey(acc.Id) && ccGraceActive[acc.Id]) {
                            accruingBalance = 0;
                        }

                        ccDailyBalances[acc.Id].Add((dayDate, accountBalances[acc.Id], accruingBalance));
                    }
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in AccountForGrowthInAccountsDuringProjectedEvents[cite: 23].");
        }
    }

    public static void AccountForGrowthInRemainderOfProjection(
        DateTime lastDate,
        DateTime endDate,
        ref decimal runningBalance,
        List<Account> accounts,
        Dictionary<int, decimal> accountBalances,
        Dictionary<int, DateTime> accountBalanceDates,
        Dictionary<int, decimal> accumulatedGrowth,
        HashSet<int> includedTotalAccounts) {
        try {
            var remainingDays = (endDate - lastDate).Days;
            if (remainingDays > 0) {
                for (var d = 0; d < remainingDays; d++) {
                    var dayDate = lastDate.AddDays(d);
                    foreach (var acc in accounts.Where(a =>
                                 a.AnnualGrowthRate > 0 && !a.IsLiability)) {
                        if (dayDate < accountBalanceDates[acc.Id]) continue;
                        var dailyRate = acc.AnnualGrowthRate / 100m / 365m;
                        var growth = accountBalances[acc.Id] * dailyRate;
                        accumulatedGrowth[acc.Id] += growth;
                        if (accumulatedGrowth[acc.Id] >= 0.01m || accumulatedGrowth[acc.Id] <= -0.01m) {
                            var toAdd = Math.Round(accumulatedGrowth[acc.Id], 2);
                            accountBalances[acc.Id] += toAdd;
                            if (includedTotalAccounts.Contains(acc.Id)) {
                                if (acc.IsLoanAccount) {
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
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in AccountForGrowthInRemainderOfProjection[cite: 23].");
        }
    }

    public static void AdjustForReconciliations(
        Dictionary<int, decimal> accountBalances,
        Dictionary<int, DateTime> accountBalanceDates,
        Dictionary<int, bool> ccPreviousMonthPaidInFull,
        Dictionary<int, bool> ccGraceActive,
        Dictionary<int, decimal> ccUnpaidStatementBalance,
        Dictionary<int, decimal> ccPaidThisCycle,
        Dictionary<int, List<(DateTime Date, decimal Balance, decimal InterestAccruingBalance)>> ccDailyBalances,
        List<Account> accounts,
        List<AccountReconciliation> allValidReconciliations,
        List<ProjectionGridItem> sortedEvents,
        DateTime current
    ) {
        try {
            if (allValidReconciliations.Count == 0) return;
            foreach (var acc in accounts) {
                var effectiveBalanceDate = acc.BalanceAsOf;
                var effectiveBalance = acc.Balance;

                var latestReconBeforeStart = allValidReconciliations
                    .Where(r => r.AccountId == acc.Id && r.ReconciledAsOfDate < current)
                    .OrderByDescending(r => r.ReconciledAsOfDate)
                    .FirstOrDefault();

                if (latestReconBeforeStart != null) {
                    if (latestReconBeforeStart.ReconciledAsOfDate >= acc.BalanceAsOf) {
                        effectiveBalanceDate = latestReconBeforeStart.ReconciledAsOfDate;
                        effectiveBalance = latestReconBeforeStart.ReconciledBalance;
                        accountBalanceDates[acc.Id] = effectiveBalanceDate;

                        if (acc.Type == AccountType.CreditCard) {
                            ccPreviousMonthPaidInFull[acc.Id] = effectiveBalance <= 0.01m;
                        }
                    }
                }

                accountBalances[acc.Id] = effectiveBalance;
                var priorEvents = sortedEvents.Where(e => e.Date >= effectiveBalanceDate && e.Date < current).ToList();

                var lastTrackedDate = effectiveBalanceDate;

                foreach (var e in priorEvents.Where(x =>
                             x.Type != ProjectionEngine.ProjectionEventType.Reconciliation)) {
                    if (acc.Type == AccountType.CreditCard && e.Date > lastTrackedDate) {
                        var days = (e.Date - lastTrackedDate).Days;
                        for (int i = 0; i < days; i++) {
                            var dayDate = lastTrackedDate.AddDays(i);
                            decimal accruingBalance = accountBalances[acc.Id];
                            if (ccGraceActive.ContainsKey(acc.Id) && ccGraceActive[acc.Id]) {
                                accruingBalance = 0;
                            }

                            ccDailyBalances[acc.Id].Add((dayDate, accountBalances[acc.Id], accruingBalance));
                        }

                        lastTrackedDate = e.Date;
                    }

                    var amountChange = Math.Abs(e.Amount);
                    if (e.FromAccountId == acc.Id) {
                        if (acc.IsLiability) {
                            accountBalances[acc.Id] += amountChange;
                        }
                        else {
                            accountBalances[acc.Id] -= amountChange;
                        }
                    }

                    if (e.ToAccountId == acc.Id) {
                        var isMortgage = (acc.Type == AccountType.Mortgage || acc.Type == AccountType.HELOC);
                        var isPersonalLoan =
                            (acc.Type == AccountType.PersonalLoan || acc.Type == AccountType.StudentLoan);
                        var isCreditCard = acc.Type == AccountType.CreditCard;
                        var isPrincipalOnly = e.IsPrincipalOnly;
                        var isRebalance = e.IsRebalance;
                        var isInterestAdjustment = (e.Type == ProjectionEngine.ProjectionEventType.Transaction &&
                                                    e.IsInterestAdjustment);
                        var isInterestOrRebalance =
                            (isMortgage || isCreditCard) && (isRebalance || isInterestAdjustment);

                        if (isInterestOrRebalance) {
                            accountBalances[acc.Id] += amountChange;
                        }
                        else if (isMortgage) {
                            var principal = amountChange;
                            if (!isPrincipalOnly && acc.MortgageDetails != null) {
                                principal = amountChange - acc.MortgageDetails.Escrow -
                                            acc.MortgageDetails.MortgageInsurance;
                                if (principal < 0) principal = 0;
                            }

                            accountBalances[acc.Id] -= principal;
                        }
                        else if (isPersonalLoan &&
                                 (e.Type == ProjectionEngine.ProjectionEventType.Transaction && isPrincipalOnly)) {
                            accountBalances[acc.Id] -= amountChange;
                        }
                        else if (isPersonalLoan &&
                                 (e.Type == ProjectionEngine.ProjectionEventType.Transaction && isRebalance)) {
                            accountBalances[acc.Id] += amountChange;
                        }
                        else if (isPersonalLoan) {
                            accountBalances[acc.Id] += amountChange;
                        }
                        else if (isCreditCard) {
                            accountBalances[acc.Id] -= amountChange;
                            ccPaidThisCycle[acc.Id] += amountChange;
                        }
                        else {
                            accountBalances[acc.Id] += amountChange;
                        }
                    }

                    if (e.Type == ProjectionEngine.ProjectionEventType.Interest) {
                        var dummyList = new List<ProjectionItem>();
                        var dummyNames = accounts.ToDictionary(a => a.Id, a => a.Name);
                        var includedSet = accounts.Where(a => a.IncludeInTotal).Select(a => a.Id).ToHashSet();
                        var dummyRunningBalance = 0m;
                        AddInterestProjection(dummyList, ref dummyRunningBalance, e, accounts, accountBalances,
                            dummyNames, ccGraceActive, ccUnpaidStatementBalance, ccPaidThisCycle, ccDailyBalances,
                            includedSet, new DateTime(1900, 1, 1));
                    }
                }

                if (acc.Type == AccountType.CreditCard) {
                    var days = (current - lastTrackedDate).Days;
                    for (int i = 0; i < days; i++) {
                        var dayDate = lastTrackedDate.AddDays(i);
                        decimal accruingBalance = accountBalances[acc.Id];
                        if (ccGraceActive.ContainsKey(acc.Id) && ccGraceActive[acc.Id]) {
                            accruingBalance = 0;
                        }

                        ccDailyBalances[acc.Id].Add((dayDate, accountBalances[acc.Id], accruingBalance));
                    }
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in AdjustForReconciliations[cite: 23].");
        }
    }

    public static bool AdjustBalanceForReconciliationBalances(
        ref decimal runningBalance,
        ProjectionGridItem e,
        List<Account> accounts,
        Dictionary<int, decimal> accountBalances,
        Dictionary<int, bool> ccPreviousMonthPaidInFull,
        HashSet<int> includedTotalAccounts) {
        try {
            if (e.Type == ProjectionEngine.ProjectionEventType.Reconciliation && e.FromAccountId.HasValue) {
                var accId = e.FromAccountId.Value;
                if (accountBalances.ContainsKey(accId)) {
                    var acc = accounts.FirstOrDefault(a => a.Id == accId);
                    if (acc != null) {
                        var oldBalance = accountBalances[accId];
                        var newBalance = e.Amount;
                        accountBalances[accId] = newBalance;

                        if (acc.Type == AccountType.CreditCard) {
                            ccPreviousMonthPaidInFull[accId] = newBalance <= 0.01m;
                        }

                        if (includedTotalAccounts.Contains(accId)) {
                            var isDebt = (acc.IsLiability);
                            if (isDebt) {
                                runningBalance -= (newBalance - oldBalance);
                            }
                            else {
                                runningBalance += (newBalance - oldBalance);
                            }
                        }
                    }
                }

                return false;
            }

            return true;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in AdjustBalanceForReconciliationBalances[cite: 23].");

            return true;
        }
    }

    public static bool AddInterestProjection(
        List<ProjectionItem> list,
        ref decimal runningBalance,
        ProjectionGridItem e,
        List<Account> accounts,
        Dictionary<int, decimal> accountBalances,
        Dictionary<int, string> accountNames,
        Dictionary<int, bool> ccGraceActive,
        Dictionary<int, decimal> ccUnpaidStatementBalance,
        Dictionary<int, decimal> ccPaidThisCycle,
        Dictionary<int, List<(DateTime Date, decimal Balance, decimal InterestAccruingBalance)>> ccDailyBalances,
        HashSet<int> includedTotalAccounts,
        DateTime startDate,
        IReadOnlyDictionary<int, decimal>? accountFloors = null) {
        try {
            var moneyAccountIds = accounts.Where(x => x.Type == AccountType.Checking || x.Type == AccountType.Savings)
                .Select(x => x.Id).ToList();

            if (e is { Type: ProjectionEngine.ProjectionEventType.Interest, FromAccountId: not null }) {
                var acc = accounts.FirstOrDefault(a => a.Id == e.FromAccountId.Value);

                // -------------------------------------------------------------
                // Mortgage / Loan Account Interest
                // -------------------------------------------------------------
                if (acc is { IsLoanAccount: true, MortgageDetails: not null }) {
                    var monthlyRate = (acc.MortgageDetails.InterestRate / 100m) / 12m;
                    var interest = Math.Round(accountBalances[acc.Id] * monthlyRate, 2);
                    accountBalances[acc.Id] += interest;
                    if (includedTotalAccounts.Contains(acc.Id)) {
                        runningBalance += interest;
                    }

                    var item = new ProjectionItem {
                        Type = e.Type,
                        ToAccountId = e.ToAccountId,
                        FromAccountId = e.FromAccountId,
                        TransactionDate = e.Date,
                        Description = e.Description,
                        Amount = Math.Abs(interest),
                        Balance = runningBalance,
                        AccountBalances = accountBalances.ToDictionary(kv => accountNames[kv.Key], kv => kv.Value)
                    };

                    if (
                        e.FromAccountId != null && moneyAccountIds.Contains(e.FromAccountId.Value) ||
                        (e.ToAccountId != null && moneyAccountIds.Contains(e.ToAccountId.Value)
                        )) {
                        item.InOrOutOfMoneyAccount = true;
                    }

                    list.Add(item);

                    return false;
                }

                // -------------------------------------------------------------
                // Credit Card Account Interest
                // -------------------------------------------------------------
                if (acc is { Type: AccountType.CreditCard, CreditCardDetails: not null }) {
                    var dailyBalances = ccDailyBalances[acc.Id];
                    var aprHist = acc.AccountAprHistory?.OrderByDescending(x => x.AsOfDate)
                                      .FirstOrDefault(x => x.AsOfDate <= e.Date)
                                  ?? acc.AccountAprHistory?.FirstOrDefault()
                                  ?? new AccountAprHistory { AnnualPercentageRate = 0 };

                    var dailyPeriodicRate = (aprHist.AnnualPercentageRate / 100m) / 365m;

                    // 1. Check if grace was active going INTO this cycle
                    bool wasGraceActiveThisCycle = ccGraceActive.ContainsKey(acc.Id) && ccGraceActive[acc.Id];

// 2. Evaluate if payments made during this cycle satisfy balance for NEXT cycle
                    bool willMaintainGraceNextCycle = false;
                    if (acc.CreditCardDetails.PayPreviousMonthBalanceInFull) {
                        willMaintainGraceNextCycle = (ccPaidThisCycle[acc.Id] >=
                                                      Math.Abs(ccUnpaidStatementBalance[acc.Id]) - 0.01m);
                    }

// 3. Calculate interest
                    decimal totalInterest = 0;

                    if (dailyBalances.Count > 0) {
                        foreach (var db in dailyBalances) {
                            // If grace WAS active for this cycle, no interest accrues on these days.
                            // If grace WAS lost (wasGraceActiveThisCycle == false), calculate daily interest
                            // on all accumulated historical daily balances (including carried-over Month 1 days).
                            decimal effectiveAccruingBalance = (wasGraceActiveThisCycle || db.Balance >= 0m)
                                ? 0m
                                : -db.Balance;

                            totalInterest += effectiveAccruingBalance * dailyPeriodicRate;
                        }
                    }

                    totalInterest = Math.Round(totalInterest, 2);

// Apply interest to liability balance if > 0
                    if (totalInterest > 0) {
                        accountBalances[acc.Id] -= totalInterest;
                        if (includedTotalAccounts.Contains(acc.Id)) {
                            runningBalance -= totalInterest;
                        }
                    }

// 4. Update grace status for NEXT cycle
                    ccGraceActive[acc.Id] = willMaintainGraceNextCycle;

                    // // 5. Min-Pay Sweep Handling
                    // var primaryChecking =
                    //     accounts.FirstOrDefault(a => a.Type == AccountType.Checking && a.IsPrimary)?.Id;
                    // if (primaryChecking.HasValue && e.Date >= startDate) {
                    //     var currentCcBalance = accountBalances[acc.Id];
                    //     var minPaymentAmount = acc.CreditCardDetails.MinPayFloor;
                    //     var amountPaidSoFar = ccPaidThisCycle[acc.Id];
                    //
                    //     if (currentCcBalance < 0 && minPaymentAmount > 0 && amountPaidSoFar < minPaymentAmount) {
                    //         var remainingMinPayment = Math.Min(-currentCcBalance, minPaymentAmount - amountPaidSoFar);
                    //
                    //         if (remainingMinPayment > 0) {
                    //             decimal checkingBalance = accountBalances[primaryChecking.Value];
                    //
                    //             decimal checkingFloor = (accountFloors != null &&
                    //                                      accountFloors.TryGetValue(primaryChecking.Value, out var fl))
                    //                 ? fl
                    //                 : 0m;
                    //             decimal spendableChecking = Math.Max(0m, checkingBalance - checkingFloor);
                    //
                    //             decimal actualSweepAmount = Math.Min(remainingMinPayment, spendableChecking);
                    //
                    //             if (actualSweepAmount > 0) {
                    //                 accountBalances[primaryChecking.Value] -= actualSweepAmount;
                    //                 accountBalances[acc.Id] += actualSweepAmount;
                    //                 ccPaidThisCycle[acc.Id] += actualSweepAmount;
                    //
                    //                 if (includedTotalAccounts.Contains(acc.Id)) {
                    //                     runningBalance = accounts.Where(a => includedTotalAccounts.Contains(a.Id))
                    //                         .Sum(a => accountBalances[a.Id]);
                    //                 }
                    //
                    //                 var sweepItem = new ProjectionItem {
                    //                     Type = e.Type,
                    //                     TransactionDate = e.Date,
                    //                     Description = $"Min-Pay Sweep: {acc.Name}",
                    //                     FromAccountId = primaryChecking,
                    //                     ToAccountId = acc.Id,
                    //                     Amount = Math.Abs(actualSweepAmount),
                    //                     Balance = runningBalance,
                    //                     IsSynthetic = true,
                    //                     AccountBalances =
                    //                         accountBalances.ToDictionary(kv => accountNames[kv.Key], kv => kv.Value),
                    //                     InOrOutOfMoneyAccount = true
                    //                 };
                    //
                    //                 list.Add(sweepItem);
                    //             }
                    //         }
                    //     }
                    // }

                    // 6. Reset tracking state for next cycle
                    ccUnpaidStatementBalance[acc.Id] = accountBalances[acc.Id];
                    ccPaidThisCycle[acc.Id] = 0;
// CRITICAL FIX: Only clear dailyBalances if interest was actually processed 
// OR if grace remains active. 
// If grace was JUST lost on this statement (wasGraceActiveThisCycle == true but willMaintainGraceNextCycle == false),
// KEEP dailyBalances so Month 2 has Month 1's days available to calculate retroactive interest!
                    if (!wasGraceActiveThisCycle || willMaintainGraceNextCycle) {
                        dailyBalances.Clear();
                    }

                    var item = new ProjectionItem {
                        Type = e.Type,
                        ToAccountId = e.ToAccountId,
                        FromAccountId = e.FromAccountId,
                        TransactionDate = e.Date,
                        Description = e.Description,
                        Amount = Math.Abs(totalInterest),
                        Balance = runningBalance,
                        AccountBalances = accountBalances.ToDictionary(kv => accountNames[kv.Key], kv => kv.Value)
                    };

                    if (
                        e.FromAccountId != null && moneyAccountIds.Contains(e.FromAccountId.Value) ||
                        (e.ToAccountId != null && moneyAccountIds.Contains(e.ToAccountId.Value)
                        )) {
                        item.InOrOutOfMoneyAccount = true;
                    }

                    list.Add(item);

                    return false;
                }
            }

            return true;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in AddInterestProjection.");
            return true;
        }
    }

    public static void AddReconciliationEvents(this List<ProjectionGridItem> events,
        List<AccountReconciliation> allValidReconciliations) {
        try {
            foreach (var recon in allValidReconciliations) {
                events.Add(new ProjectionGridItem(recon.ReconciledAsOfDate, recon.ReconciledBalance, "Reconciliation",
                    recon.AccountId, null, null, null, null, ProjectionEngine.ProjectionEventType.Reconciliation, false,
                    false, false,
                    false));
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in AddReconciliationEvents[cite: 23].");
        }
    }

    public static void AddInterestEvents(this List<ProjectionGridItem> events,
        List<Account> accounts,
        List<Transaction> transactions,
        DateTime startDate,
        DateTime endDate) {
        try {
            foreach (var acc in accounts) {
                if ((acc.IsLoanAccount) && acc.MortgageDetails != null) {
                    var nextInterest = acc.MortgageDetails.PaymentDate;
                    if (nextInterest == DateTime.MinValue) nextInterest = startDate;
                    while (nextInterest < startDate) nextInterest = nextInterest.AddMonths(1);

                    while (nextInterest < endDate) {
                        var periodStart = nextInterest.AddMonths(-1);
                        var hasInterestTransaction = transactions.Any(t =>
                            (t.AccountId == acc.Id || t.ToAccountId == acc.Id) &&
                            t.TransactionDate > periodStart && t.TransactionDate <= nextInterest &&
                            (t.IsInterestOnly ||
                             t.Description.Contains("Interest", StringComparison.OrdinalIgnoreCase)));

                        if (!hasInterestTransaction) {
                            events.Add(new ProjectionGridItem(nextInterest, 0, $"Interest: {acc.Name}", acc.Id, null,
                                null,
                                null, null,
                                ProjectionEngine.ProjectionEventType.Interest, false, false, false, false));
                        }

                        nextInterest = nextInterest.AddMonths(1);
                    }
                }

                if (acc.Type == AccountType.CreditCard && acc.CreditCardDetails != null) {
                    var nextStatement = new DateTime(startDate.Year, startDate.Month,
                        Math.Min(acc.CreditCardDetails.StatementDay,
                            DateTime.DaysInMonth(startDate.Year, startDate.Month)));
                    if (nextStatement <= startDate) nextStatement = nextStatement.AddMonths(1);

                    while (nextStatement <= endDate) {
                        if (nextStatement.Day != acc.CreditCardDetails.StatementDay) {
                            nextStatement = new DateTime(nextStatement.Year, nextStatement.Month,
                                Math.Min(acc.CreditCardDetails.StatementDay,
                                    DateTime.DaysInMonth(nextStatement.Year, nextStatement.Month)));
                        }

                        var periodStart = nextStatement.AddMonths(-1);
                        var hasInterestAdjustment = transactions.Any(t =>
                            (t.AccountId == acc.Id) &&
                            t.TransactionDate > periodStart && t.TransactionDate <= nextStatement &&
                            (t.IsInterestOnly ||
                             t.Description.Contains("Interest", StringComparison.OrdinalIgnoreCase)));

                        if (!hasInterestAdjustment) {
                            // 1. Statement / Interest Event on StatementDay (e.g. 6th)
                            events.Add(new ProjectionGridItem(nextStatement, 0, $"Credit Card Interest: {acc.Name}",
                                acc.Id, null, null, null, null,
                                ProjectionEngine.ProjectionEventType.Interest, false, false, false, false));

                            // 2. Minimum Payment Due Event on Due Date (e.g. 6th + 21 days = 27th)
                            var dueDate = nextStatement.AddDays(acc.CreditCardDetails.DueDateOffset);
                            if (dueDate <= endDate) {
                                events.Add(new ProjectionGridItem(dueDate, 0, $"Min-Pay Check: {acc.Name}",
                                    acc.Id, null, null, null, null,
                                    ProjectionEngine.ProjectionEventType.Sweep, false, false, false, false));
                            }
                        }

                        nextStatement = nextStatement.AddMonths(1);
                    }
                }
                
                // if (acc.Type == AccountType.CreditCard && acc.CreditCardDetails != null) {
                //     var nextStatement = new DateTime(startDate.Year, startDate.Month,
                //         Math.Min(acc.CreditCardDetails.StatementDay,
                //             DateTime.DaysInMonth(startDate.Year, startDate.Month)));
                //     if (nextStatement <= startDate) nextStatement = nextStatement.AddMonths(1);
                //
                //     while (nextStatement <= endDate) {
                //         if (nextStatement.Day != acc.CreditCardDetails.StatementDay) {
                //             nextStatement = new DateTime(nextStatement.Year, nextStatement.Month,
                //                 Math.Min(acc.CreditCardDetails.StatementDay,
                //                     DateTime.DaysInMonth(nextStatement.Year, nextStatement.Month)));
                //         }
                //
                //         var periodStart = nextStatement.AddMonths(-1);
                //         var hasInterestAdjustment = transactions.Any(t =>
                //             (t.AccountId == acc.Id) &&
                //             t.TransactionDate > periodStart && t.TransactionDate <= nextStatement &&
                //             (t.IsInterestOnly ||
                //              t.Description.Contains("Interest", StringComparison.OrdinalIgnoreCase)));
                //
                //         if (!hasInterestAdjustment) {
                //             events.Add(new ProjectionGridItem(nextStatement, 0, $"Credit Card Interest: {acc.Name}",
                //                 acc.Id,
                //                 null, null, null, null,
                //                 ProjectionEngine.ProjectionEventType.Interest, false, false, false, false));
                //         }
                //
                //         nextStatement = nextStatement.AddMonths(1);
                //     }
                // }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in AddInterestEvents[cite: 23].");
        }
    }

    public static void AddTransactionEvents(this List<ProjectionGridItem> events, List<Transaction> transactions) {
        try {
            foreach (var transaction in transactions) {
                var isFromAccountReconciled =
                    !transaction.AccountId.HasValue || transaction.FromAccountReconciliationId.HasValue;
                var isToAccountReconciled =
                    !transaction.ToAccountId.HasValue || transaction.ToAccountReconciliationId.HasValue;
                var isFullyReconciled = isFromAccountReconciled && isToAccountReconciled;

                isFromAccountReconciled = false;
                isToAccountReconciled = false;
                isFullyReconciled = false;

                if (!isFullyReconciled) {
                    var accountId = transaction.AccountId;
                    var toAcountId = transaction.ToAccountId;

                    events.Add(new ProjectionGridItem(transaction.TransactionDate, transaction.Amount,
                        transaction.Description,
                        accountId, toAcountId, transaction.BucketId,
                        transaction.PaycheckId, transaction.PaycheckOccurrenceDate,
                        ProjectionEngine.ProjectionEventType.Transaction,
                        transaction.IsPrincipalOnly,
                        transaction.IsRebalance, transaction.IsInterestOnly, false, transaction.Id));
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in AddTransactionEvents[cite: 23].");
        }
    }

    public static void AddBucketEvents(this List<ProjectionGridItem> events,
        List<Account> accounts,
        List<Paycheck> paychecks,
        List<BudgetBucket> buckets,
        List<PeriodBucket> periodBuckets,
        List<BucketPaycheckAllocation> allAllocations,
        Dictionary<int, decimal> bucketBalances,
        DateTime current,
        DateTime endDate,
        DateTime? todayOverride = null) {
        try {
            var today = todayOverride ?? DateTime.Today;
            var primaryChecking = accounts.FirstOrDefault(a => a.Type == AccountType.Checking && a.IsPrimary)?.Id;

            foreach (var bucket in buckets) {
                if (bucket.Type == BucketType.UpfrontFloor) continue;

                var bucketAllocations = allAllocations.Where(a => a.BucketId == bucket.Id && a.IsActive).ToList();

                //The allocation setting is hidden when there is only one check
                //its also possible for things to change, for there now to only be one paycheck
                //or for bad data to have crept in
                if (bucket.Type == BucketType.Standard &&
                    paychecks.Count(x => x.EndDate == null || x.EndDate > today) == 1) {
                    //} && bucketAllocations.Count == 0) {
                    bucketAllocations.Clear();
                    bucketAllocations.Add(new BucketPaycheckAllocation() {
                        PaycheckId = paychecks.First().Id,
                        BucketId = bucket.Id,
                        AllocationType = "Percentage",
                        AllocationValue = 100
                    });
                }

                if (bucketAllocations.Any()) {
                    foreach (var alloc in bucketAllocations) {
                        var pay = paychecks.FirstOrDefault(p => p.Id == alloc.PaycheckId);
                        if (pay == null) continue;

                        var nextPay = pay.StartDate;
                        while (nextPay < endDate) {
                            var payPeriodEndDate = (pay.Frequency switch {
                                Frequency.Weekly => nextPay.AddDays(7),
                                Frequency.BiWeekly => nextPay.AddDays(14),
                                Frequency.Monthly => nextPay.AddMonths(1),
                                _ => nextPay.AddYears(100)
                            }).AddDays(-1);

                            if (payPeriodEndDate >= today && nextPay >= current &&
                                (pay.EndDate == null || nextPay <= pay.EndDate)) {
                                var pb = periodBuckets.FirstOrDefault(p =>
                                    p.BucketId == bucket.Id && (p.PeriodDate.Date == nextPay.Date));

                                decimal expectedAllocAmount = alloc.AllocationType == "Percentage"
                                    ? Math.Round(pay.ExpectedAmount * (alloc.AllocationValue / 100m), 2)
                                    : alloc.AllocationValue;

                                decimal amountToUse = GetBucketProjectedAmount(bucket, pb, expectedAllocAmount,
                                    bucketBalances, nextPay);
                                decimal actualAmount = amountToUse;

                                if (amountToUse > 0) {
                                    if (bucket.Type == BucketType.AccumulatingDrawdown && (pb == null || pb.Id == 0)) {
                                        bucketBalances[bucket.Id] += amountToUse;
                                    }

                                    var suffix = (bucket.Type == BucketType.AccumulatingDrawdown)
                                        ? "(FUNDED)"
                                        : "(PAID)";
                                    var paidSuffix = (pb != null && pb.IsPaid) ? $" {suffix}" : "";
                                    var fromAccId = bucket.AccountId ?? primaryChecking;

                                    var type = bucket.Type == BucketType.AccumulatingDrawdown
                                        ? ProjectionEngine.ProjectionEventType.AccumulatingDrawdown
                                        : ProjectionEngine.ProjectionEventType.Bucket;

                                    if (bucket.Type == BucketType.AccumulatingDrawdown && pb != null) {
                                        type = ProjectionEngine.ProjectionEventType.Bucket;
                                        actualAmount = pb.ActualAmount;
                                    }

                                    events.Add(new ProjectionGridItem(
                                        payPeriodEndDate,
                                        actualAmount,
                                        $"Bucket: {bucket.Name}{paidSuffix}",
                                        fromAccId,
                                        null,
                                        bucket.Id,
                                        pay.Id,
                                        nextPay,
                                        type,
                                        false,
                                        false,
                                        false,
                                        false));
                                }
                            }

                            nextPay = pay.Frequency switch {
                                Frequency.Weekly => nextPay.AddDays(7),
                                Frequency.BiWeekly => nextPay.AddDays(14),
                                Frequency.Monthly => nextPay.AddMonths(1),
                                _ => nextPay.AddYears(100)
                            };
                        }
                    }
                }
                else {
                    DateTime nextDue = bucket.NextDueDate ?? current;

                    if (bucket.TargetFrequency == TargetFrequencyType.Monthly && bucket.NextDueDate == null) {
                        nextDue = new DateTime(current.Year, current.Month,
                            DateTime.DaysInMonth(current.Year, current.Month));
                    }

                    if (nextDue < current) nextDue = current;

                    while (nextDue < endDate) {
                        if (nextDue >= today && nextDue >= current) {
                            var pb = periodBuckets.FirstOrDefault(p =>
                                p.BucketId == bucket.Id && (p.PeriodDate.Date == nextDue.Date));

                            decimal amountToUse = GetBucketProjectedAmount(bucket, pb, bucket.ExpectedAmount,
                                bucketBalances, nextDue);

                            if (amountToUse > 0) {
                                if (bucket.Type == BucketType.AccumulatingDrawdown && (pb == null || pb.Id == 0)) {
                                    bucketBalances[bucket.Id] += amountToUse;
                                }

                                var suffix = (bucket.Type == BucketType.AccumulatingDrawdown) ? "(FUNDED)" : "(PAID)";
                                var paidSuffix = (pb != null && pb.IsPaid) ? $" {suffix}" : "";
                                var fromAccId = bucket.AccountId ?? primaryChecking;

                                var type = bucket.Type == BucketType.AccumulatingDrawdown
                                    ? ProjectionEngine.ProjectionEventType.AccumulatingDrawdown
                                    : ProjectionEngine.ProjectionEventType.Bucket;

                                events.Add(new ProjectionGridItem(
                                    nextDue,
                                    amountToUse,
                                    $"Bucket: {bucket.Name}{paidSuffix}",
                                    fromAccId,
                                    null,
                                    bucket.Id,
                                    null,
                                    null,
                                    type,
                                    false,
                                    false,
                                    false,
                                    false));
                            }
                        }

                        if (bucket.TargetFrequency == TargetFrequencyType.Monthly && bucket.NextDueDate == null) {
                            // 1. Advance to the next month
                            var nextMonth = nextDue.AddMonths(1);

                            // 2. Snap to the last day of that new month
                            nextDue = new DateTime(
                                nextMonth.Year,
                                nextMonth.Month,
                                DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month)
                            );
                        }
                        else {
                            // Standard cadence progression
                            nextDue = bucket.TargetFrequency switch {
                                TargetFrequencyType.Weekly => nextDue.AddDays(7),
                                TargetFrequencyType.BiWeekly => nextDue.AddDays(14),
                                TargetFrequencyType.SemiMonthly => nextDue.AddDays(15),
                                TargetFrequencyType.Monthly => nextDue.AddMonths(1),
                                TargetFrequencyType.Quarterly => nextDue.AddMonths(3),
                                TargetFrequencyType.Annual => nextDue.AddYears(1),
                                _ => nextDue.AddMonths(1)
                            };
                        }
                    }
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in AddBucketEvents[cite: 23].");
        }
    }

    private static decimal GetBucketProjectedAmount(
        BudgetBucket bucket,
        PeriodBucket? pb,
        decimal defaultExpectedAmount,
        Dictionary<int, decimal> bucketBalances,
        DateTime targetDate) {
        try {
            if (pb != null) return pb.ActualAmount;

            decimal effectiveAmount = bucket.GetEffectiveAmount(targetDate);

            if (bucket.Type == BucketType.AccumulatingDrawdown) {
                decimal currentBal = bucketBalances.TryGetValue(bucket.Id, out var b) ? b : bucket.CurrentBalance;
                decimal shortfall = Math.Max(0, bucket.TargetBalance - currentBal);
                if (shortfall <= 0) return 0m;

                return Math.Min(effectiveAmount, shortfall);
            }

            return effectiveAmount;
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in GetBucketProjectedAmount[cite: 23].");

            return defaultExpectedAmount;
        }
    }

    public static void AddBillEvents(this List<ProjectionGridItem> events,
        List<Account> accounts,
        List<Bill> bills,
        List<Transaction> allBillTransactions,
        List<PeriodBill> periodBills,
        DateTime current,
        DateTime endDate,
        DateTime? todayOverride = null) {
        try {
            var today = todayOverride ?? DateTime.Today;
            var primaryChecking = accounts.FirstOrDefault(a => a.Type == AccountType.Checking && a.IsPrimary)?.Id;

            foreach (var bill in bills) {
                DateTime nextDue;
                if (bill.NextDueDate == null) {
                    var dueDay = Math.Max(1, bill.DueDay);
                    nextDue = new DateTime(current.Year, current.Month,
                        Math.Min(dueDay, DateTime.DaysInMonth(current.Year, current.Month)));
                    if (nextDue < current) nextDue = nextDue.AddMonths(1);
                }
                else {
                    nextDue = bill.NextDueDate.Value;
                    while (bill.Frequency == Frequency.Yearly && nextDue < current) {
                        nextDue = nextDue.AddYears(1);
                    }
                }

                while (nextDue < endDate) {
                    var pb = periodBills.FirstOrDefault(p => p.BillId == bill.Id && (p.DueDate.Date == nextDue.Date ||
                        (p.DueDate.Date >= new DateTime(nextDue.Year, nextDue.Month, 1) && p.DueDate.Date <=
                            new DateTime(nextDue.Year, nextDue.Month,
                                DateTime.DaysInMonth(nextDue.Year, nextDue.Month)))));
                    var isPaid = (pb != null && allBillTransactions.Any(t =>
                                     t.BillId == bill.Id &&
                                     (t.TransactionDate >= nextDue ||
                                      (Math.Abs((t.TransactionDate - nextDue).TotalDays) <= 14)) &&
                                     t.TransactionDate >= pb.PeriodDate &&
                                     t.TransactionDate <= pb.PeriodDate.AddDays(28)))
                                 || (pb == null && allBillTransactions.Any(t =>
                                     t.BillId == bill.Id &&
                                     ((Math.Abs((t.TransactionDate - nextDue).TotalDays) <=
                                       14))));

                    if (!isPaid) {
                        var amountToUse = (pb != null) ? pb.ActualAmount : bill.GetEffectiveAmount(nextDue);
                        var dueDate = (pb != null) ? pb.DueDate : nextDue;
                        if (dueDate >= today) {
                            var paidSuffix = (pb != null && pb.IsPaid) ? " (PAID)" : "";
                            var fromAccId = bill.AccountId ?? primaryChecking;
                            if (amountToUse != 0) {
                                if (bill.ToAccountId.HasValue) {
                                    events.Add(new ProjectionGridItem(dueDate, -amountToUse,
                                        $"Transfer: {bill.Name}{paidSuffix}", fromAccId,
                                        bill.ToAccountId.Value, null, null, null,
                                        ProjectionEngine.ProjectionEventType.Transfer,
                                        bill.IsPrincipalOnly, false,
                                        false, false));
                                }
                                else {
                                    events.Add(new ProjectionGridItem(transactionDate: dueDate, amount: -amountToUse,
                                        description: $"Bill: {bill.Name}{paidSuffix}",
                                        fromAccountId: fromAccId,
                                        toAccountId: null,
                                        bucketId: null,
                                        paycheckId: null,
                                        paycheckOccurrenceDate: null,
                                        type: ProjectionEngine.ProjectionEventType.Bill,
                                        isPrincipalOnly: bill.IsPrincipalOnly,
                                        isRebalance: false,
                                        isInterestAdjustment: false,
                                        isReconciled: false,
                                        billId: bill.Id));
                                }
                            }
                        }
                    }

                    nextDue = bill.Frequency switch {
                        Frequency.Monthly => nextDue.AddMonths(1),
                        Frequency.Yearly => nextDue.AddYears(1),
                        Frequency.Weekly => nextDue.AddDays(7),
                        Frequency.BiWeekly => nextDue.AddDays(14),
                        _ => nextDue.AddYears(100)
                    };
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in AddBillEvents[cite: 23].");
        }
    }

    public static void AddPaycheckEvents(this List<ProjectionGridItem> events,
        List<Account> accounts,
        List<Paycheck> paychecks,
        List<Transaction> allPaycheckTransactions,
        DateTime current,
        DateTime endDate) {
        try {
            var cashAccount = accounts.FirstOrDefault(a => a.Name == "Household Cash" && a.Type == AccountType.Cash);
            foreach (var pay in paychecks) {
                var nextPay = pay.StartDate;
                var endPay = pay.StartDate;
                endPay = pay.Frequency switch {
                    Frequency.Weekly => endPay.AddDays(7),
                    Frequency.BiWeekly => endPay.AddDays(14),
                    Frequency.Monthly => endPay.AddMonths(1),
                    _ => endPay.AddYears(100)
                };

                while (nextPay < endDate) {
                    if (nextPay >= current && (pay.EndDate == null || nextPay <= pay.EndDate)) {
                        var transactionOverride = allPaycheckTransactions.FirstOrDefault(a =>
                            a.PaycheckId == pay.Id &&
                            (a.PaycheckOccurrenceDate?.Date ==
                             nextPay.Date ||
                             (Math.Abs((nextPay - a.TransactionDate).TotalDays) <=
                              3)));

                        if (transactionOverride == null) {
                            var toAccountId = pay.AccountId ?? cashAccount?.Id;
                            events.Add(
                                new ProjectionGridItem(nextPay, pay.ExpectedAmount, $"Expected Pay: {pay.Name}", null,
                                    toAccountId, null,
                                    pay.Id, nextPay, ProjectionEngine.ProjectionEventType.Paycheck, false, false, false,
                                    false));
                        }
                    }

                    nextPay = pay.Frequency switch {
                        Frequency.Weekly => nextPay.AddDays(7),
                        Frequency.BiWeekly => nextPay.AddDays(14),
                        Frequency.Monthly => nextPay.AddMonths(1),
                        _ => nextPay.AddYears(100)
                    };
                    endPay = nextPay;
                    endPay = pay.Frequency switch {
                        Frequency.Weekly => endPay.AddDays(7),
                        Frequency.BiWeekly => endPay.AddDays(14),
                        Frequency.Monthly => endPay.AddMonths(1),
                        _ => endPay.AddYears(100)
                    };
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in AddPaycheckEvents[cite: 23].");
        }
    }
}