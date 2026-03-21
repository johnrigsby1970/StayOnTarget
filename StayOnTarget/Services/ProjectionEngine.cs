using StayOnTarget.Models;
using StayOnTarget.ViewModels;

namespace StayOnTarget.Services;

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
        DateTime current = startDate;

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

        // 1. Paychecks
        var cashAccount = accounts.FirstOrDefault(a => a.Name == "Household Cash");
        foreach (var pay in paychecks) {
            DateTime nextPay = pay.StartDate;
            DateTime endPay = pay.StartDate;
            endPay = pay.Frequency switch {
                Frequency.Weekly => endPay.AddDays(7),
                Frequency.BiWeekly => endPay.AddDays(14),
                Frequency.Monthly => endPay.AddMonths(1),
                _ => endPay.AddYears(100)
            };

            while (nextPay < endDate) {
                if (nextPay >= current && (pay.EndDate == null || nextPay <= pay.EndDate)) {
                    // Association mechanism: check if a transaction overrides this paycheck occurrence
                    var transactionOverride = allPaycheckTransactions.FirstOrDefault(a =>
                        a.PaycheckId == pay.Id &&
                        a.PaycheckOccurrenceDate?.Date ==
                        nextPay.Date); // && a.Date >= nextPay && a.Date < endPay); //&& a.PaycheckOccurrenceDate?.Date == nextPay.Date);

                    if (transactionOverride == null) {
                        int? toAccountId = pay.AccountId ?? cashAccount?.Id;
                        events.Add(
                            new ProjectionGridItem(nextPay, pay.ExpectedAmount, $"Expected Pay: {pay.Name}", null,
                                toAccountId, null,
                                pay.Id, nextPay, ProjectionEventType.Paycheck, false, false, false, false));
                    }
                    else {
                        // if (transactionOverride.ToAccountReconciledId != null) {
                        //     events.Add(
                        //         new ProjectionGridItem (nextPay, pay.ExpectedAmount, $"Reconciled Pay: {pay.Name}", null, null, null,
                        //             pay.Id, nextPay, ProjectionEventType.Paycheck, false, false, false, false));
                        // }
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

        // 2. Bills & Transfers
        var primaryChecking = accounts.FirstOrDefault(a => a.Type == AccountType.Checking)?.Id;

        foreach (var bill in bills) {
            DateTime nextDue = bill.NextDueDate ?? current;
            if (bill.NextDueDate == null) {
                nextDue = new DateTime(current.Year, current.Month,
                    Math.Min(bill.DueDay, DateTime.DaysInMonth(current.Year, current.Month)));
                if (nextDue < current) nextDue = nextDue.AddMonths(1);
            }

            while (nextDue < endDate) {
                //It is possible for the actual bill date to be different from the expected. If someone changes it in the period bill, we want to use the actual date
                var pb = periodBills.FirstOrDefault(p => p.BillId == bill.Id && (p.DueDate.Date == nextDue.Date ||
                    (p.DueDate.Date >= new DateTime(nextDue.Year, nextDue.Month, 1) && p.DueDate.Date <=
                        new DateTime(nextDue.Year, nextDue.Month, DateTime.DaysInMonth(nextDue.Year, nextDue.Month)))));
                var isPaid = (pb != null && allBillTransactions.Any(t =>
                                 t.BillId == bill.Id &&
                                 (t.Date >= nextDue || (Math.Abs((t.Date - nextDue).TotalDays) <= 14)) &&
                                 t.Date >= pb.PeriodDate && t.Date <= pb.PeriodDate.AddDays(28)))
                             || (pb == null && allBillTransactions.Any(t =>
                                 t.BillId == bill.Id &&
                                 ((Math.Abs((t.Date - nextDue).TotalDays) <= 14)))); //some arbitrary thresholds

                if (!isPaid) {
                    //bill has been paid by a logged transaction. We will use the logged transaction instead of the expected bill or paid period bill entry.
                    decimal amountToUse = (pb != null) ? pb.ActualAmount : bill.ExpectedAmount;
                    DateTime dueDate = (pb != null) ? pb.DueDate : nextDue;
                    string paidSuffix = (pb != null && pb.IsPaid) ? " (PAID)" : "";

                    int? fromAccId = bill.AccountId ?? primaryChecking;
                    if (amountToUse != 0) {
                        if (bill.ToAccountId.HasValue) {
                            events.Add(new ProjectionGridItem(dueDate, amountToUse,
                                $"Transfer: {bill.Name}{paidSuffix}", fromAccId,
                                bill.ToAccountId.Value, null, null, null, ProjectionEventType.Transfer, false, false,
                                false, false));
                        }
                        else {
                            events.Add(new ProjectionGridItem(dueDate, -amountToUse, $"Bill: {bill.Name}{paidSuffix}",
                                fromAccId, null, null, null,
                                null, ProjectionEventType.Bill, false, false, false, false));
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

        // 3. Buckets
        foreach (var bucket in buckets) {
            if (bucket.PaycheckId.HasValue) {
                // If the bucket is associated with a specific paycheck, project it for each occurrence of THAT paycheck.
                foreach (var pay in paychecks.Where(p => p.Id == bucket.PaycheckId)) {
                    DateTime nextPay = pay.StartDate;
                    while (nextPay < endDate) {
                        var payPeriodEndDate = (pay.Frequency switch {
                            Frequency.Weekly => nextPay.AddDays(7),
                            Frequency.BiWeekly => nextPay.AddDays(14),
                            Frequency.Monthly => nextPay.AddMonths(1),
                            _ => nextPay.AddYears(100)
                        }).AddDays(-1);

                        if (nextPay >= current && (pay.EndDate == null || nextPay <= pay.EndDate)) {
                            PeriodBucket? pb = periodBuckets.FirstOrDefault(p =>
                                p.BucketId == bucket.Id && (p.PeriodDate.Date == nextPay.Date));

                            decimal amountToUse = (pb != null) ? pb.ActualAmount : bucket.ExpectedAmount;
                            string paidSuffix = (pb != null && pb.IsPaid) ? " (PAID)" : "";

                            int? fromAccId = bucket.AccountId ?? primaryChecking;
                            events.Add(new ProjectionGridItem(payPeriodEndDate, -amountToUse,
                                $"Bucket: {bucket.Name}{paidSuffix}", fromAccId, null,
                                bucket.Id, null, null, ProjectionEventType.Bucket, false, false, false, false));
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
                // If NOT associated with a paycheck, project it for each period (based on ALL paychecks).
                // Find all unique paycheck occurrences across all paychecks.
                var occurrences = new List<(DateTime Start, DateTime End)>();
                foreach (var pay in paychecks) {
                    DateTime nextPay = pay.StartDate;
                    while (nextPay < endDate) {
                        var payPeriodEndDate = (pay.Frequency switch {
                            Frequency.Weekly => nextPay.AddDays(7),
                            Frequency.BiWeekly => nextPay.AddDays(14),
                            Frequency.Monthly => nextPay.AddMonths(1),
                            _ => nextPay.AddYears(100)
                        }).AddDays(-1);

                        occurrences.Add((nextPay, payPeriodEndDate));

                        nextPay = pay.Frequency switch {
                            Frequency.Weekly => nextPay.AddDays(7),
                            Frequency.BiWeekly => nextPay.AddDays(14),
                            Frequency.Monthly => nextPay.AddMonths(1),
                            _ => nextPay.AddYears(100)
                        };
                    }
                }

                // Group by start date to avoid double-projecting if two paychecks start on the same day.
                foreach (var group in occurrences.GroupBy(o => o.Start)) {
                    var occurrence = group.First();
                    if (occurrence.Start >= current) {
                        PeriodBucket? pb = periodBuckets.FirstOrDefault(p =>
                            p.BucketId == bucket.Id && (p.PeriodDate.Date == occurrence.Start.Date));

                        decimal amountToUse = (pb != null) ? pb.ActualAmount : bucket.ExpectedAmount;
                        string paidSuffix = (pb != null && pb.IsPaid) ? " (PAID)" : "";

                        int? fromAccId = bucket.AccountId ?? primaryChecking;
                        events.Add(new ProjectionGridItem(occurrence.End, -amountToUse,
                            $"Bucket: {bucket.Name}{paidSuffix}", fromAccId, null,
                            bucket.Id, null, null, ProjectionEventType.Bucket, false, false, false, false));
                    }
                }
            }
        }

        // 4. Transactions
        foreach (var transaction in transactions) {
            // Skip fully reconciled transactions:
            // A transaction is fully reconciled if:
            // - It has a FromAccountReconciledId (if AccountId is set)
            // - It has a ToAccountReconciledId (if ToAccountId is set)
            // - Both reconciliations are complete for accounts involved
            bool isFromAccountReconciled =
                !transaction.AccountId.HasValue || transaction.FromAccountReconciledId.HasValue;
            bool isToAccountReconciled =
                !transaction.ToAccountId.HasValue || transaction.ToAccountReconciledId.HasValue;
            bool isFullyReconciled = isFromAccountReconciled && isToAccountReconciled;

            if (showReconciled) {
                isFromAccountReconciled = false;
                isToAccountReconciled = false;
                isFullyReconciled = false;
            }

            if (!isFullyReconciled) {
                // We need to collect ALL transactions that could affect balances from the earliest BalanceAsOf
                //Handle paticlaly reconcilced transactions with respect to projections, but not including the account
                //to and from depending on if its been reconciled, we will indicate partial reconciliation
                //Ex. Ive made a payment from my checking account, but the bank hasnt posted it yet and
                //my credit card account has posted it. I reconciled my credit card account, but not my bank account and 
                //with regard to this transaction since the bank has not posted it yet.
                var accountId = transaction.AccountId;
                var toAcountId = transaction.ToAccountId;
                if (!showReconciled) {
                    if (transaction.AccountId.HasValue && transaction.FromAccountReconciledId.HasValue) {
                        accountId = null;
                    }

                    if (transaction.ToAccountId.HasValue && transaction.ToAccountReconciledId.HasValue) {
                        toAcountId = null;
                    }
                }

                events.Add(new ProjectionGridItem(transaction.Date, transaction.Amount, transaction.Description,
                    accountId, toAcountId, transaction.BucketId,
                    transaction.PaycheckId, transaction.PaycheckOccurrenceDate, ProjectionEventType.Transaction,
                    transaction.IsPrincipalOnly,
                    transaction.IsRebalance, transaction.IsInterestAdjustment, false));
            }
        }

        // 5. Interest & Growth Setup
        foreach (var acc in accounts) {
            if (acc.Type == AccountType.Mortgage && acc.MortgageDetails != null) {
                DateTime nextInterest = acc.MortgageDetails.PaymentDate;
                if (nextInterest == DateTime.MinValue) nextInterest = startDate;
                while (nextInterest < startDate) nextInterest = nextInterest.AddMonths(1);

                while (nextInterest < endDate) {
                    var hasInterestTransaction =
                        transactions.Any(a => a.ToAccountId == acc.Id && a.Date.Date == nextInterest.Date);

                    if (!hasInterestTransaction) {
                        events.Add(new ProjectionGridItem(nextInterest, 0, $"Interest: {acc.Name}", acc.Id, null, null,
                            null, null,
                            ProjectionEventType.Interest, false, false, false, false));
                    }

                    nextInterest = nextInterest.AddMonths(1);
                }
            }

            if (acc.Type == AccountType.CreditCard && acc.CreditCardDetails != null) {
                DateTime nextStatement = new DateTime(startDate.Year, startDate.Month,
                    Math.Min(acc.CreditCardDetails.StatementDay,
                        DateTime.DaysInMonth(startDate.Year, startDate.Month)));
                if (nextStatement <= startDate) nextStatement = nextStatement.AddMonths(1);
                DateTime nextDueDate = nextStatement.AddDays(acc.CreditCardDetails.DueDateOffset);

                //what we need to know is if the nextDueDate has come, and then if there are any payments that bring the balance amount to 0 or what that balance amount would be 
                //based on those payments, and then calculate the interest that would have been applied on the statement date. At present, we will assume the due date is not the same as the statement date.
                //At a later time we will account for the possibility there is no a grace period which would change the <= on line 341 to a <.
                while (nextStatement <= endDate) {
                    if (nextStatement.Day != acc.CreditCardDetails.StatementDay) {
                        nextStatement = new DateTime(nextStatement.Year, nextStatement.Month,
                            Math.Min(acc.CreditCardDetails.StatementDay,
                                DateTime.DaysInMonth(nextStatement.Year, nextStatement.Month)));
                    }

                    var statementMonthStart = new DateTime(nextStatement.Year, nextStatement.Month, 1);
                    var statementMonthEnd = new DateTime(nextStatement.Year, nextStatement.Month,
                        DateTime.DaysInMonth(nextStatement.Year, nextStatement.Month));
                    var hasInterestAdjustment = transactions.Any(t =>
                        t.AccountId == acc.Id && t.IsInterestAdjustment && t.Date >= statementMonthStart &&
                        t.Date <= statementMonthEnd);

                    if (!hasInterestAdjustment) {
                        events.Add(new ProjectionGridItem(nextStatement, 0, $"Credit Card Interest: {acc.Name}", acc.Id,
                            null, null, null, null,
                            ProjectionEventType.Interest, false, false, false, false));
                    }

                    nextStatement = nextStatement.AddMonths(1);
                }
            }
        }

        foreach (var recon in allValidReconciliations) {
            events.Add(new ProjectionGridItem(recon.ReconciledAsOfDate, recon.ReconciledBalance, "Reconciliation",
                recon.AccountId, null, null, null, null, ProjectionEventType.Reconciliation, false, false, false,
                false));
        }

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
        foreach (var acc in accounts) {
            DateTime effectiveBalanceDate = acc.BalanceAsOf;
            decimal effectiveBalance = acc.Balance;

            // Use latest reconciliation strictly BEFORE 'current' if available and more recent than BalanceAsOf
            // We use BEFORE (<) instead of AT (<=) to avoid double-processing reconciliations that happen AT 'current'
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
            foreach (var e in priorEvents) {
                decimal amountChange = Math.Abs(e.Amount);
                if (e.FromAccountId == acc.Id) {
                    if (acc.Type == AccountType.Mortgage || acc.Type == AccountType.PersonalLoan ||
                        acc.Type == AccountType.CreditCard) {
                        accountBalances[acc.Id] += amountChange;
                    }
                    else {
                        accountBalances[acc.Id] -= amountChange;
                    }
                }

                if (e.ToAccountId == acc.Id) {
                    bool isMortgage = acc.Type == AccountType.Mortgage;
                    bool isPersonalLoan = acc.Type == AccountType.PersonalLoan;
                    bool isCreditCard = acc.Type == AccountType.CreditCard;
                    bool isPrincipalOnly = e.IsPrincipalOnly;
                    bool isRebalance = e.IsRebalance;
                    bool isInterestAdjustment = (e.Type == ProjectionEventType.Transaction && e.IsInterestAdjustment);
                    bool isInterestOrRebalance = (isMortgage || isCreditCard) && (isRebalance || isInterestAdjustment);

                    if (isInterestOrRebalance) {
                        accountBalances[acc.Id] += amountChange;
                    }
                    else if (isMortgage) {
                        decimal principal = amountChange;
                        if (!isPrincipalOnly && acc.MortgageDetails != null) {
                            principal = amountChange - acc.MortgageDetails.Escrow -
                                        acc.MortgageDetails.MortgageInsurance;
                            if (principal < 0) principal = 0;
                        }

                        accountBalances[acc.Id] -= principal;
                    }
                    else if (isPersonalLoan && (e.Type == ProjectionEventType.Transaction && isPrincipalOnly)) {
                        accountBalances[acc.Id] -= amountChange;
                    }
                    else if (isPersonalLoan && (e.Type == ProjectionEventType.Transaction && isRebalance)) {
                        accountBalances[acc.Id] += amountChange;
                    }
                    else if (isPersonalLoan) {
                        accountBalances[acc.Id] += amountChange;
                    }
                    else if (isCreditCard) {
                        accountBalances[acc.Id] -= amountChange;
                    }
                    else {
                        accountBalances[acc.Id] += amountChange;
                    }
                }
            }
        }

        decimal runningBalance = accounts.Where(a => includedTotalAccounts.Contains(a.Id)).Sum(a => {
            var bal = accountBalances[a.Id];
            return (a.Type == AccountType.Mortgage || a.Type == AccountType.PersonalLoan ||
                    a.Type == AccountType.CreditCard)
                ? -bal
                : bal;
        });

        DateTime lastDate = current;
        var futureEvents = sortedEvents.Where(e => e.Date >= current).ToList();
        var paycheckDates = futureEvents
            .Where(e => e.Type == ProjectionEventType.Paycheck ||
                        (e.Type == ProjectionEventType.Transaction && e.PaycheckId.HasValue)).Select(e => e.Date)
            .Distinct()
            .OrderBy(d => d).ToList();

        if (!paycheckDates.Any() || paycheckDates[0] > current) {
            paycheckDates.Insert(0, current);
        }

        var bucketSpending = new Dictionary<(DateTime PeriodDate, int BucketId), decimal>();
        foreach (var transaction in allBucketTransactions) {
            if (transaction.BucketId.HasValue) {
                DateTime periodDate = paycheckDates.LastOrDefault(d => d <= transaction.Date);
                if (periodDate != DateTime.MinValue) {
                    var key = (periodDate, transaction.BucketId.Value);
                    if (!bucketSpending.ContainsKey(key)) bucketSpending[key] = 0;
                    bucketSpending[key] += Math.Abs(transaction.Amount);
                }
            }
        }

        foreach (var e in futureEvents) {
            int days = (e.Date - lastDate).Days;
            if (days > 0) {
                for (int d = 0; d < days; d++) {
                    DateTime dayDate = lastDate.AddDays(d);
                    foreach (var acc in accounts.Where(a =>
                                 a.AnnualGrowthRate > 0 && a.Type != AccountType.Mortgage &&
                                 a.Type != AccountType.CreditCard)) {
                        if (dayDate < accountBalanceDates[acc.Id]) continue;
                        decimal dailyRate = acc.AnnualGrowthRate / 100m / 365m;
                        decimal growth = accountBalances[acc.Id] * dailyRate;
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

            // Apply Interest
            if (e.Type == ProjectionEventType.Interest && e.FromAccountId.HasValue) {
                var acc = accounts.FirstOrDefault(a => a.Id == e.FromAccountId.Value);
                if (acc != null && acc.Type == AccountType.Mortgage && acc.MortgageDetails != null) {
                    decimal monthlyRate = (acc.MortgageDetails.InterestRate / 100m) / 12m;
                    decimal interest = Math.Round(accountBalances[acc.Id] * monthlyRate, 2);
                    accountBalances[acc.Id] += interest;
                    if (includedTotalAccounts.Contains(acc.Id)) {
                        runningBalance -= interest;
                    }

                    list.Add(new ProjectionItem {
                        Date = e.Date,
                        Description = e.Description,
                        Amount = interest,
                        Balance = runningBalance,
                        AccountBalances = accountBalances.ToDictionary(kv => accountNames[kv.Key], kv => kv.Value)
                    });
                    continue;
                }

                if (acc != null && acc.Type == AccountType.CreditCard && acc.CreditCardDetails != null) {
                    var dailyBalances = ccDailyBalances[acc.Id];
                    if (dailyBalances.Any()) {
                        var aprHist = acc.AccountAprHistory?.SingleOrDefault(x => x.AsOfDate <= e.Date);
                        if (aprHist == null) aprHist = new AccountAprHistory { AsOfDate = e.Date };
                        decimal dailyPeriodicRate = (aprHist.AnnualPercentageRate / 100m) / 365m;
                        decimal totalInterest = 0;
                        bool gracePeriodApplies = acc.CreditCardDetails.PayPreviousMonthBalanceInFull &&
                                                  ccPreviousMonthPaidInFull[acc.Id];

                        if (!gracePeriodApplies) {
                            decimal sumBalances = dailyBalances.Sum(db => db.Balance);
                            decimal avgDailyBalance = sumBalances / dailyBalances.Count;
                            totalInterest = Math.Round(avgDailyBalance * dailyPeriodicRate * dailyBalances.Count, 2);
                        }

                        if (totalInterest > 0) {
                            accountBalances[acc.Id] += totalInterest;
                            if (includedTotalAccounts.Contains(acc.Id)) {
                                runningBalance -= totalInterest;
                            }
                        }

                        if (totalInterest < 0) totalInterest = 0;
                        // Check if previous balance (before interest) was paid in full to determine grace period for NEXT month
                        ccPreviousMonthPaidInFull[acc.Id] = (accountBalances[acc.Id] - totalInterest) <= 0.01m;
                        dailyBalances.Clear();

                        list.Add(new ProjectionItem {
                            Date = e.Date,
                            Description = e.Description,
                            Amount = totalInterest,
                            Balance = runningBalance,
                            AccountBalances = accountBalances.ToDictionary(kv => accountNames[kv.Key], kv => kv.Value)
                        });
                        continue;
                    }
                }
            }

            // Apply Reconciliation
            if (e.Type == ProjectionEventType.Reconciliation && e.FromAccountId.HasValue) {
                int accId = e.FromAccountId.Value;
                if (accountBalances.ContainsKey(accId)) {
                    var acc = accounts.FirstOrDefault(a => a.Id == accId);
                    if (acc != null) {
                        decimal oldBalance = accountBalances[accId];
                        decimal newBalance = e.Amount;
                        accountBalances[accId] = newBalance;

                        if (acc.Type == AccountType.CreditCard) {
                            // Check if reconciled balance is 0 to determine grace period for next month
                            ccPreviousMonthPaidInFull[accId] = newBalance <= 0.01m;
                        }

                        if (includedTotalAccounts.Contains(accId)) {
                            bool isDebt = (acc.Type == AccountType.Mortgage || acc.Type == AccountType.PersonalLoan ||
                                           acc.Type == AccountType.CreditCard);
                            if (isDebt) {
                                runningBalance -= (newBalance - oldBalance);
                            }
                            else {
                                runningBalance += (newBalance - oldBalance);
                            }
                        }
                    }
                }

                // Do not add reconciliation to the projection grid (as requested)
                continue;
            }

            // Apply balances
            decimal currentEventAmount = e.Amount;

            // Handle Mortgage Payoff Adjustment
            if (e.ToAccountId.HasValue && mortgagePaidOff.ContainsKey(e.ToAccountId.Value) &&
                mortgagePaidOff[e.ToAccountId.Value]) {
                var toAcc = accounts.FirstOrDefault(a => a.Id == e.ToAccountId.Value);
                if (toAcc?.MortgageDetails != null) {
                    decimal escrowOnly = toAcc.MortgageDetails.Escrow + toAcc.MortgageDetails.MortgageInsurance;
                    currentEventAmount = -escrowOnly;
                }
            }

            if (e.Type == ProjectionEventType.Bucket && e.BucketId.HasValue) {
                DateTime periodDate = paycheckDates.LastOrDefault(d => d <= e.Date);
                if (periodDate != DateTime.MinValue) {
                    var key = (periodDate, e.BucketId.Value);
                    decimal spent = bucketSpending.ContainsKey(key) ? bucketSpending[key] : 0;
                    decimal projectedAmount = Math.Abs(e.Amount);
                    currentEventAmount = -Math.Max(0, projectedAmount - spent);
                }
            }

            if (e.ToAccountId.HasValue && accountBalances.ContainsKey(e.ToAccountId.Value)) {
                var toAcc = accounts.FirstOrDefault(a => a.Id == e.ToAccountId.Value);
                decimal amountChange = Math.Abs(currentEventAmount);
                bool isMortgagePayment = toAcc != null && toAcc.Type == AccountType.Mortgage;
                bool isPersonalLoanPayment = toAcc != null && toAcc.Type == AccountType.PersonalLoan;
                bool isCreditCardPayment = toAcc != null && toAcc.Type == AccountType.CreditCard;
                bool isPrincipalOnly = e.IsPrincipalOnly;
                bool isRebalance = e.IsRebalance;
                bool isInterestAdjustment = (e.Type == ProjectionEventType.Transaction && e.IsInterestAdjustment);
                bool isInterestOrRebalance =
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
                decimal amountChange = Math.Abs(currentEventAmount);
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

        for (int i = 0; i < paycheckDates.Count; i++) {
            DateTime start = paycheckDates[i];
            DateTime next = (i + 1 < paycheckDates.Count) ? paycheckDates[i + 1] : endDate;
            var periodItems = list.Where(item => item.Date >= start && item.Date < next).ToList();
            if (periodItems.Any()) {
                periodItems.First().PeriodNet = periodItems.Sum(item => item.Amount);
            }
        }

        if (lastDate < endDate) {
            int remainingDays = (endDate - lastDate).Days;
            if (remainingDays > 0) {
                for (int d = 0; d < remainingDays; d++) {
                    DateTime dayDate = lastDate.AddDays(d);
                    foreach (var acc in accounts.Where(a =>
                                 a.AnnualGrowthRate > 0 && a.Type != AccountType.Mortgage &&
                                 a.Type != AccountType.CreditCard)) {
                        if (dayDate < accountBalanceDates[acc.Id]) continue;
                        decimal dailyRate = acc.AnnualGrowthRate / 100m / 365m;
                        decimal growth = accountBalances[acc.Id] * dailyRate;
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