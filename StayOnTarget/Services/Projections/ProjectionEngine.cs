using StayOnTarget.Models;
using StayOnTarget.ViewModels;

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
        List<Transaction> allTransactions,
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
        var moneyAccountIds = accounts.Where(x => x.Type == AccountType.Checking || x.Type == AccountType.Savings).Select(x => x.Id).ToList();
        var includedTotalAccounts = new HashSet<int>(accounts.Where(a => a.IncludeInTotal).Select(a => a.Id));

        var uniqueTransactions = allTransactions;

        if (showReconciled) {
            var unbalancedPaychecks = paychecks.Where(p => !p.IsBalanced).ToList();
            if (unbalancedPaychecks.Any()) {
                DateTime earliestUnbalanced = unbalancedPaychecks.Min(p => p.StartDate);
                if (earliestUnbalanced < current) {
                    current = earliestUnbalanced;
                }
            }
        }
        
        // Build reconciliation lookup: latest valid reconciliation per account
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

        if (!showReconciled) {
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
            
            // ENSURE we rewind far enough to capture all transactions that could affect credit card interest
            var creditCardAccounts = accounts.Where(a => a.Type == AccountType.CreditCard).ToList();
            foreach (var cc in creditCardAccounts) {
                var minDate = cc.BalanceAsOf;
                if (reconLookup.ContainsKey(cc.Id)) {
                    minDate = reconLookup[cc.Id].ReconciledAsOfDate;
                }
                if (minDate < current) {
                    current = minDate;
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
        var test = events.Where(x => x.Amount == -500).ToList();
        // 4. Create events for Transactions
        // We always use uniqueTransactions to build the events list for simulation.
        // This ensures consistent balance reconstruction between ShowReconciled modes.
        events.AddTransactionEvents(uniqueTransactions, showReconciled);
        var test8 = events.Where(x => x.Amount == -500).ToList();
        // 5. Create events or Interest & Growth Setup
        // Use all transactions for interest calculation to correctly identify manual adjustments
        events.AddInterestEvents(accounts, uniqueTransactions, startDate, endDate);

        // 6. Create events for reconciliations (points when an account balance is reset and verified so that balances do not have to run from the very beginning)
        events.AddReconciliationEvents(allValidReconciliations);

        #endregion

        //order the events, being sure that paychecks are the first within a period
        var sortedEvents = events
            .OrderBy(e => e.Date)
            .ThenByDescending(e =>
                e.Type == ProjectionEventType.Paycheck || (e.PaycheckId.HasValue && e.ToAccountId.HasValue))
            .ThenByDescending(e => e.Type == ProjectionEventType.Paycheck)
            .ToList();
        var test2 = events.Where(x => x.Amount == -500).ToList();
        // Tracking variables
        var accountBalanceDates = accounts.ToDictionary(a => a.Id, a => a.BalanceAsOf);
        var accumulatedGrowth = accounts.ToDictionary(a => a.Id, a => 0m);
        var ccDailyBalances = accounts.Where(a => a.Type == AccountType.CreditCard).ToDictionary(a => a.Id,
            a => new List<(DateTime Date, decimal Balance, decimal InterestAccruingBalance)>());
        
        var ccGraceActive = accounts.Where(a => a.Type == AccountType.CreditCard)
            .ToDictionary(a => a.Id, a => a.CreditCardDetails?.GraceActive ?? true);
        var ccUnpaidStatementBalance = accounts.Where(a => a.Type == AccountType.CreditCard)
            .ToDictionary(a => a.Id, a => a.Balance <= 0.01m ? 0m : a.Balance); 
        var ccPaidThisCycle = accounts.Where(a => a.Type == AccountType.CreditCard)
            .ToDictionary(a => a.Id, a => 0m);

        var ccPreviousMonthPaidInFull = accounts.Where(a => a.Type == AccountType.CreditCard)
            .ToDictionary(a => a.Id, a => a.Balance <= 0.01m);
        var mortgagePaidOff = accounts.Where(a => a.Type == AccountType.Mortgage).ToDictionary(a => a.Id, a => false);

        // Recalculate accountBalances based on BalanceAsOf (or reconciliation) and events before 'current'
        //Account balance to start projection is either the balanceAsOf date of the account or the reconciledAsOf date of the account.
        //The balance could require determining past events impact on balance up to the point the projection starts.
        //Reconciliations help by setting the balance at a point in time so that the projection can start from a known balance.
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

        var runningBalance = accounts.Where(a => includedTotalAccounts.Contains(a.Id)).Sum(a => {
            var bal = accountBalances[a.Id];
            return (a.Type == AccountType.Mortgage || a.Type == AccountType.PersonalLoan ||
                    a.Type == AccountType.CreditCard)
                ? bal //now signed as a debt already in the database, negative
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

        //Associate each transaction associated with a bucket with the period date
        //This will allow projected buckets to be reduced by actual spending later on.
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

        var primaryChecking = accounts.FirstOrDefault(a => a.Type == AccountType.Checking)?.Id;
        futureEvents = futureEvents.OrderBy(e => e.Date).ToList();
        foreach (var e in futureEvents) {

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

            // Apply Interest, add in interest events
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

            //Each projected bucket should be reduced by actual spending
            //if the amount spent is greater than the projected amount, the projected amount is set to zero.
            //we overspent the bucket, and to project further spending in this bucket category
            //would require the user to adjust the period bucket. We cannot go negative, so the floor is zero.
            if (e is { Type: ProjectionEventType.Bucket, BucketId: not null }) {
                var periodDate = paycheckDates.LastOrDefault(d => d <= e.Date);
                if (periodDate != DateTime.MinValue) {
                    var key = (periodDate, e.BucketId.Value);
                    var spent = bucketSpending.ContainsKey(key) ? bucketSpending[key] : 0;
                    var projectedAmount = Math.Abs(e.Amount);
                    currentEventAmount = -Math.Max(0, projectedAmount - spent);
                }
            }
            if ((e.FromAccountId != null && e.FromAccountId.Value == 1) || (e.ToAccountId != null && e.ToAccountId.Value == 1)) {
                var s = "";
            }
            // Handle ToAccountId balance update
            if (e.ToAccountId.HasValue && accountBalances.ContainsKey(e.ToAccountId.Value)) {
                var toAcc = accounts.FirstOrDefault(a => a.Id == e.ToAccountId.Value);
                var amountChange = Math.Abs(currentEventAmount);
                var isDebt = toAcc != null && (toAcc.Type == AccountType.Mortgage || toAcc.Type == AccountType.PersonalLoan || toAcc.Type == AccountType.CreditCard);
                var isPrincipalOnly = e.IsPrincipalOnly;
                var isRebalance = e.IsRebalance;
                var isInterestAdjustment = (e.Type == ProjectionEventType.Transaction && e.IsInterestAdjustment);
                var isInterestOrRebalance = isDebt && (isRebalance || isInterestAdjustment);

                if (isInterestOrRebalance) {
                    accountBalances[e.ToAccountId.Value] += amountChange;
                }
                else if (toAcc?.Type == AccountType.Mortgage) {
                    var principal = amountChange;
                    if (!isPrincipalOnly && toAcc.MortgageDetails != null) {
                        var escrowAndInsurance = toAcc.MortgageDetails.Escrow + toAcc.MortgageDetails.MortgageInsurance;
                        principal = Math.Max(0, amountChange - escrowAndInsurance);
                        if (Math.Abs(accountBalances[e.ToAccountId.Value]) <= principal) {
                            principal = accountBalances[e.ToAccountId.Value];
                            mortgagePaidOff[e.ToAccountId.Value] = true;
                            currentEventAmount = (principal + escrowAndInsurance);
                        }
                    }
                    accountBalances[e.ToAccountId.Value] += principal;
                }
                else if (toAcc?.Type == AccountType.PersonalLoan && isPrincipalOnly) {
                    accountBalances[e.ToAccountId.Value] += amountChange;
                }
                else if (toAcc?.Type == AccountType.CreditCard) {
                    accountBalances[e.ToAccountId.Value] += e.FromAccountId==null ? currentEventAmount : -currentEventAmount;
                    if (ccPaidThisCycle.ContainsKey(e.ToAccountId.Value)) {
                        ccPaidThisCycle[e.ToAccountId.Value] += amountChange;
                    }
                }
                else if (isDebt) {
                    accountBalances[e.ToAccountId.Value] += amountChange;
                }
                else {
                    accountBalances[e.ToAccountId.Value] += amountChange;
                }
                if (e.ToAccountId.Value == 1) {
                    var s = "";
                }
                if ((e.FromAccountId != null && e.FromAccountId.Value == 1) || (e.ToAccountId != null && e.ToAccountId.Value == 1)) {
                    var s = amountChange;
                }
            }

            // Handle FromAccountId balance update
            var effectiveFromAccountId = e.FromAccountId ?? ((e.Type == ProjectionEventType.Bill || e.Type == ProjectionEventType.Bucket || e.Type == ProjectionEventType.Transfer) ? primaryChecking : null);
            if (effectiveFromAccountId.HasValue && accountBalances.ContainsKey(effectiveFromAccountId.Value)) {
                var fromAcc = accounts.FirstOrDefault(a => a.Id == effectiveFromAccountId.Value);
                var amountChange = currentEventAmount;//Math.Abs(currentEventAmount);
                var isDebt = fromAcc != null && (fromAcc.Type == AccountType.Mortgage || fromAcc.Type == AccountType.PersonalLoan || fromAcc.Type == AccountType.CreditCard);
                if (e.FromAccountId == 7) {
                    var s = "";
                }
                if (isDebt) {
                    accountBalances[effectiveFromAccountId.Value] += amountChange;
                }
                else {
                    accountBalances[effectiveFromAccountId.Value] += amountChange;
                }
                
                if ((e.FromAccountId != null && e.FromAccountId.Value == 1) || (e.ToAccountId != null && e.ToAccountId.Value == 1)) {
                    var s = amountChange;
                }
            }

            // Recalculate running balance from all included accounts
            runningBalance = accounts.Where(a => includedTotalAccounts.Contains(a.Id)).Sum(a => {
                var bal = accountBalances[a.Id];
                return (a.Type == AccountType.Mortgage || a.Type == AccountType.PersonalLoan ||
                        a.Type == AccountType.CreditCard)
                    ? bal //now properly signed in the database as a debt, negative already
                    : bal;
            });

            var item = new ProjectionItem {
                ToAccountId = e.ToAccountId,
                FromAccountId = e.FromAccountId,
                TransactionDate = e.Date,
                Description = e.Description,
                PaycheckId = e.PaycheckId,
                Amount = currentEventAmount,
                Balance = runningBalance,
                AccountBalances = accountBalances.ToDictionary(kv => accountNames[kv.Key], kv => kv.Value)
            };
            if(
                e.FromAccountId != null && moneyAccountIds.Contains(e.FromAccountId.Value) || 
                 (e.ToAccountId != null && moneyAccountIds.Contains(e.ToAccountId.Value)
                 )){
                item.InOrOutOfMoneyAccount = true;
            }
            list.Add(item);
        }

        //Calculate the net income for this period. Not counting investment accounts unrealized gains/losses.
        for (var i = 0; i < paycheckDates.Count; i++) {
            var start = paycheckDates[i];
            var next = (i + 1 < paycheckDates.Count) ? paycheckDates[i + 1] : endDate;
            var periodItems = list.Where(item => item.TransactionDate >= start && item.TransactionDate < next && item.InOrOutOfMoneyAccount).ToList();
            if (periodItems.Count != 0) {
                periodItems.First().PeriodNet = periodItems.Sum(item => item.Amount);
            }
        }

        //No more specific events in this projection, so what growth might occur in balances in accounts
        //until the end of the projection
        if (lastDate < endDate) {
            ProjectionEngineExtensions.AccountForGrowthInRemainderOfProjection(
                lastDate, //date of last format transaction or expected event
                endDate,
                ref runningBalance,
                accounts,
                accountBalances,
                accountBalanceDates,
                accumulatedGrowth,
                includedTotalAccounts);
            
            list.Add(new ProjectionItem {
                TransactionDate = endDate,
                Description = "End of Projection",
                Amount = 0,
                Balance = runningBalance,
                AccountBalances = accountBalances.ToDictionary(kv => accountNames[kv.Key], kv => kv.Value)
            });
        }

        //Remove projected bucket events which get set to 0, along with other events that would clutter the projection
        if (removeZeroBalanceEntries) {
            list = list.Where(x => x.Amount != 0).ToList();
        }

        return list;
    }
}