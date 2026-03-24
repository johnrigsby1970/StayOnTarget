using StayOnTarget.Models;
using StayOnTarget.ViewModels;

namespace StayOnTarget.Services.Projections;

public static class ProjectionEngineExtensions {
    
    //Determine the initial balance for an account based either on most recent reconciliations or past running
    //events impacts on balance
    public static void AdjustForReconciliations(
        Dictionary<int, decimal> accountBalances,
        Dictionary<int, DateTime> accountBalanceDates,
        Dictionary<int, bool> ccPreviousMonthPaidInFull,
        List<Account> accounts,
        List<AccountReconciliation> allValidReconciliations,
        List<ProjectionGridItem> sortedEvents,
        DateTime current
    ) {
        foreach (var acc in accounts) {
            var effectiveBalanceDate = acc.BalanceAsOf;
            var effectiveBalance = acc.Balance;

            // Use the latest reconciliation strictly BEFORE 'current' if available and more recent than BalanceAsOf
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
                var amountChange = Math.Abs(e.Amount);
                if (e.FromAccountId == acc.Id) {
                    if (acc.Type is AccountType.Mortgage or AccountType.PersonalLoan or AccountType.CreditCard) {
                        accountBalances[acc.Id] += amountChange;
                    }
                    else {
                        accountBalances[acc.Id] -= amountChange;
                    }
                }

                if (e.ToAccountId == acc.Id) {
                    var isMortgage = acc.Type == AccountType.Mortgage;
                    var isPersonalLoan = acc.Type == AccountType.PersonalLoan;
                    var isCreditCard = acc.Type == AccountType.CreditCard;
                    var isPrincipalOnly = e.IsPrincipalOnly;
                    var isRebalance = e.IsRebalance;
                    var isInterestAdjustment = (e.Type == ProjectionEngine.ProjectionEventType.Transaction &&
                                                e.IsInterestAdjustment);
                    var isInterestOrRebalance = (isMortgage || isCreditCard) && (isRebalance || isInterestAdjustment);

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
                    }
                    else {
                        accountBalances[acc.Id] += amountChange;
                    }
                }
            }
        }
    }

    //As time goes on, the balances in an account could be reconciled at points in time. These may have out-of-band
    //adjustments to the account not reflected in transactions within this system. Pick up those changes for 
    //projected balances.
    public static bool AdjustBalanceForReconciliationBalances(
        ref decimal runningBalance,
        ProjectionGridItem e,
        List<Account> accounts,
        Dictionary<int, decimal> accountBalances,
        Dictionary<int, bool> ccPreviousMonthPaidInFull,
        HashSet<int> includedTotalAccounts) {
        if (e.Type == ProjectionEngine.ProjectionEventType.Reconciliation && e.FromAccountId.HasValue) {
            var accId = e.FromAccountId.Value;
            if (accountBalances.ContainsKey(accId)) {
                var acc = accounts.FirstOrDefault(a => a.Id == accId);
                if (acc != null) {
                    var oldBalance = accountBalances[accId];
                    var newBalance = e.Amount;
                    accountBalances[accId] = newBalance;

                    if (acc.Type == AccountType.CreditCard) {
                        // Check if the reconciled balance is 0 to determine the grace period for next month
                        ccPreviousMonthPaidInFull[accId] = newBalance <= 0.01m;
                    }

                    if (includedTotalAccounts.Contains(accId)) {
                        var isDebt = (acc.Type == AccountType.Mortgage || acc.Type == AccountType.PersonalLoan ||
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
            return false; 
        }

        return true;
    }
    
    public static bool AddInterestProjection(
        List<ProjectionItem> list,
        ref decimal runningBalance,
        ProjectionGridItem e, 
        List<Account> accounts,
        Dictionary<int, decimal> accountBalances,
        Dictionary<int, string> accountNames,
        Dictionary<int, bool> ccPreviousMonthPaidInFull,
        Dictionary<int, List<(DateTime Date, decimal Balance, decimal InterestAccruingBalance)>> ccDailyBalances,
        HashSet<int> includedTotalAccounts) {
        
        if (e is { Type: ProjectionEngine.ProjectionEventType.Interest, FromAccountId: not null }) {
            var acc = accounts.FirstOrDefault(a => a.Id == e.FromAccountId.Value);
            if (acc is { Type: AccountType.Mortgage, MortgageDetails: not null }) {
                var monthlyRate = (acc.MortgageDetails.InterestRate / 100m) / 12m;
                var interest = Math.Round(accountBalances[acc.Id] * monthlyRate, 2);
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
                return false;
            }

            if (acc is { Type: AccountType.CreditCard, CreditCardDetails: not null }) {
                var dailyBalances = ccDailyBalances[acc.Id];
                if (dailyBalances.Any()) {
                    var aprHist = acc.AccountAprHistory?.SingleOrDefault(x => x.AsOfDate <= e.Date);
                    if (aprHist == null) aprHist = new AccountAprHistory { AsOfDate = e.Date };
                    var dailyPeriodicRate = (aprHist.AnnualPercentageRate / 100m) / 365m;
                    decimal totalInterest = 0;
                    var gracePeriodApplies = acc.CreditCardDetails.PayPreviousMonthBalanceInFull &&
                                             ccPreviousMonthPaidInFull[acc.Id];

                    if (!gracePeriodApplies) {
                        var sumBalances = dailyBalances.Sum(db => db.Balance);
                        var avgDailyBalance = sumBalances / dailyBalances.Count;
                        totalInterest = Math.Round(avgDailyBalance * dailyPeriodicRate * dailyBalances.Count, 2);
                    }

                    if (totalInterest > 0) {
                        accountBalances[acc.Id] += totalInterest;
                        if (includedTotalAccounts.Contains(acc.Id)) {
                            runningBalance -= totalInterest;
                        }
                    }

                    if (totalInterest < 0) totalInterest = 0;
                    // Check if the previous balance (before interest) was paid in full to determine the grace period for NEXT month
                    ccPreviousMonthPaidInFull[acc.Id] = (accountBalances[acc.Id] - totalInterest) <= 0.01m;
                    dailyBalances.Clear();

                    list.Add(new ProjectionItem {
                        Date = e.Date,
                        Description = e.Description,
                        Amount = totalInterest,
                        Balance = runningBalance,
                        AccountBalances = accountBalances.ToDictionary(kv => accountNames[kv.Key], kv => kv.Value)
                    });
                    return false;
                }
            }
        }

        return true;
    }

    public static void AddReconciliationEvents(this List<ProjectionGridItem> events,
        List<AccountReconciliation> allValidReconciliations) {
        foreach (var recon in allValidReconciliations) {
            events.Add(new ProjectionGridItem(recon.ReconciledAsOfDate, recon.ReconciledBalance, "Reconciliation",
                recon.AccountId, null, null, null, null, ProjectionEngine.ProjectionEventType.Reconciliation, false,
                false, false,
                false));
        }
    }

    public static void AddInterestEvents(this List<ProjectionGridItem> events, List<Account> accounts,
        List<Transaction> transactions, DateTime startDate,
        DateTime endDate) {
        foreach (var acc in accounts) {
            if (acc.Type == AccountType.Mortgage && acc.MortgageDetails != null) {
                var nextInterest = acc.MortgageDetails.PaymentDate;
                if (nextInterest == DateTime.MinValue) nextInterest = startDate;
                while (nextInterest < startDate) nextInterest = nextInterest.AddMonths(1);

                while (nextInterest < endDate) {
                    var hasInterestTransaction =
                        transactions.Any(a => a.ToAccountId == acc.Id && a.Date.Date == nextInterest.Date);

                    if (!hasInterestTransaction) {
                        events.Add(new ProjectionGridItem(nextInterest, 0, $"Interest: {acc.Name}", acc.Id, null, null,
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
                var nextDueDate = nextStatement.AddDays(acc.CreditCardDetails.DueDateOffset);

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
                            ProjectionEngine.ProjectionEventType.Interest, false, false, false, false));
                    }

                    nextStatement = nextStatement.AddMonths(1);
                }
            }
        }
    }

    public static void AddTransactionEvents(this List<ProjectionGridItem> events, List<Transaction> transactions,
        bool showReconciled) {
        foreach (var transaction in transactions) {
            // Skip fully reconciled transactions:
            // A transaction is fully reconciled if:
            // - It has a FromAccountReconciledId (if AccountId is set)
            // - It has a ToAccountReconciledId (if ToAccountId is set)
            // - Both reconciliations are complete for accounts involved
            var isFromAccountReconciled =
                !transaction.AccountId.HasValue || transaction.FromAccountReconciledId.HasValue;
            var isToAccountReconciled =
                !transaction.ToAccountId.HasValue || transaction.ToAccountReconciledId.HasValue;
            var isFullyReconciled = isFromAccountReconciled && isToAccountReconciled;

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
                    transaction.PaycheckId, transaction.PaycheckOccurrenceDate,
                    ProjectionEngine.ProjectionEventType.Transaction,
                    transaction.IsPrincipalOnly,
                    transaction.IsRebalance, transaction.IsInterestAdjustment, false));
            }
        }
    }

    public static void AddBucketEvents(this List<ProjectionGridItem> events,
        List<Account> accounts,
        List<Paycheck> paychecks,
        List<BudgetBucket> buckets,
        List<PeriodBucket> periodBuckets,
        DateTime current,
        DateTime endDate) {
        var today = DateTime.Today;
        var primaryChecking = accounts.FirstOrDefault(a => a.Type == AccountType.Checking)?.Id;
        foreach (var bucket in buckets) {
            if (bucket.PaycheckId.HasValue) {
                // If the bucket is associated with a specific paycheck, project it for each occurrence of THAT paycheck.
                foreach (var pay in paychecks.Where(p => p.Id == bucket.PaycheckId)) {
                    var nextPay = pay.StartDate;
                    while (nextPay < endDate) {
                        var payPeriodEndDate = (pay.Frequency switch {
                            Frequency.Weekly => nextPay.AddDays(7),
                            Frequency.BiWeekly => nextPay.AddDays(14),
                            Frequency.Monthly => nextPay.AddMonths(1),
                            _ => nextPay.AddYears(100)
                        }).AddDays(-1);

                        //Buckets are projected entries. We won't project past entries. We simply didn't spend that money.
                        //Transactions that fit into that bucket matter, but not simply the bucket which represents future
                        //planned spending
                        if (payPeriodEndDate >= today && nextPay >= current &&
                            (pay.EndDate == null || nextPay <= pay.EndDate)) {
                            var pb = periodBuckets.FirstOrDefault(p =>
                                p.BucketId == bucket.Id && (p.PeriodDate.Date == nextPay.Date));

                            var amountToUse = (pb != null) ? pb.ActualAmount : bucket.ExpectedAmount;
                            var paidSuffix = (pb != null && pb.IsPaid) ? " (PAID)" : "";

                            var fromAccId = bucket.AccountId ?? primaryChecking;
                            events.Add(new ProjectionGridItem(payPeriodEndDate, -amountToUse,
                                $"Bucket: {bucket.Name}{paidSuffix}", fromAccId, null,
                                bucket.Id, null, null, ProjectionEngine.ProjectionEventType.Bucket, false, false, false,
                                false));
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
                    var nextPay = pay.StartDate;
                    while (nextPay < endDate) {
                        var payPeriodEndDate = (pay.Frequency switch {
                            Frequency.Weekly => nextPay.AddDays(7),
                            Frequency.BiWeekly => nextPay.AddDays(14),
                            Frequency.Monthly => nextPay.AddMonths(1),
                            _ => nextPay.AddYears(100)
                        }).AddDays(-1);

                        //Buckets are projected entries. We won't project past entries. We simply didn't spend that money.
                        //Transactions that fit into that bucket matter, but not simply the bucket which represents future
                        //planned spending
                        if (payPeriodEndDate >= today) {
                            occurrences.Add((nextPay, payPeriodEndDate));
                        }

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
                        var pb = periodBuckets.FirstOrDefault(p =>
                            p.BucketId == bucket.Id && (p.PeriodDate.Date == occurrence.Start.Date));

                        var amountToUse = (pb != null) ? pb.ActualAmount : bucket.ExpectedAmount;
                        var paidSuffix = (pb != null && pb.IsPaid) ? " (PAID)" : "";

                        var fromAccId = bucket.AccountId ?? primaryChecking;
                        events.Add(new ProjectionGridItem(occurrence.End, -amountToUse,
                            $"Bucket: {bucket.Name}{paidSuffix}", fromAccId, null,
                            bucket.Id, null, null, ProjectionEngine.ProjectionEventType.Bucket, false, false, false,
                            false));
                    }
                }
            }
        }
    }

    public static void AddBillEvents(this List<ProjectionGridItem> events,
        List<Account> accounts,
        List<Bill> bills,
        List<Transaction> allBillTransactions,
        List<PeriodBill> periodBills,
        DateTime current,
        DateTime endDate) {
        var primaryChecking = accounts.FirstOrDefault(a => a.Type == AccountType.Checking)?.Id;

        foreach (var bill in bills) {
            var nextDue = bill.NextDueDate ?? current;
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
                    var amountToUse = (pb != null) ? pb.ActualAmount : bill.ExpectedAmount;
                    var dueDate = (pb != null) ? pb.DueDate : nextDue;
                    var paidSuffix = (pb != null && pb.IsPaid) ? " (PAID)" : "";

                    var fromAccId = bill.AccountId ?? primaryChecking;
                    if (amountToUse != 0) {
                        if (bill.ToAccountId.HasValue) {
                            events.Add(new ProjectionGridItem(dueDate, amountToUse,
                                $"Transfer: {bill.Name}{paidSuffix}", fromAccId,
                                bill.ToAccountId.Value, null, null, null, ProjectionEngine.ProjectionEventType.Transfer,
                                false, false,
                                false, false));
                        }
                        else {
                            events.Add(new ProjectionGridItem(dueDate, -amountToUse, $"Bill: {bill.Name}{paidSuffix}",
                                fromAccId, null, null, null,
                                null, ProjectionEngine.ProjectionEventType.Bill, false, false, false, false));
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

    public static void AddPaycheckEvents(this List<ProjectionGridItem> events,
        List<Account> accounts,
        List<Paycheck> paychecks,
        List<Transaction> allPaycheckTransactions,
        DateTime current,
        DateTime endDate) {
        var cashAccount = accounts.FirstOrDefault(a => a.Name == "Household Cash");
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
                    // Association mechanism: check if a transaction overrides this paycheck occurrence
                    var transactionOverride = allPaycheckTransactions.FirstOrDefault(a =>
                        a.PaycheckId == pay.Id &&
                        a.PaycheckOccurrenceDate?.Date ==
                        nextPay.Date); // && a.Date >= nextPay && a.Date < endPay); //&& a.PaycheckOccurrenceDate?.Date == nextPay.Date);

                    if (transactionOverride == null) {
                        var toAccountId = pay.AccountId ?? cashAccount?.Id;
                        events.Add(
                            new ProjectionGridItem(nextPay, pay.ExpectedAmount, $"Expected Pay: {pay.Name}", null,
                                toAccountId, null,
                                pay.Id, nextPay, ProjectionEngine.ProjectionEventType.Paycheck, false, false, false,
                                false));
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
    }
}