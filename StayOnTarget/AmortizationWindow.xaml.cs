using System.Windows;
using StayOnTarget.Models;

namespace StayOnTarget
{
    public partial class AmortizationWindow : Window
    {
        public AmortizationWindow(Account account)
        {
            InitializeComponent();
            HeaderLabel.Text = $"Amortization for {account.Name}";
            CalculateSchedule(account);
        }

        private void CalculateSchedule(Account account)
        {
            if (account.MortgageDetails == null) return;

            var schedule = new List<AmortizationItem>();
            decimal balance = account.Balance;
            decimal monthlyInterestRate = account.MortgageDetails.InterestRate / 100 / 12;
            decimal escrowInsurance = account.MortgageDetails.Escrow + account.MortgageDetails.MortgageInsurance;
            decimal totalPayment = account.MortgageDetails.LoanPayment;
            DateTime paymentDate = account.MortgageDetails.PaymentDate;

            int month = 1;
            while (balance > 0 && month <= 600) // Limit to 50 years to prevent infinite loop
            {
                decimal interest = balance * monthlyInterestRate;
                decimal principal = totalPayment - interest - escrowInsurance;

                if (principal <= 0 && balance > 0)
                {
                    // Payment is too low to cover interest and escrow
                    break;
                }

                if (balance < principal)
                {
                    principal = balance;
                }

                balance -= principal;

                schedule.Add(new AmortizationItem
                {
                    Month = month++,
                    Date = paymentDate,
                    Payment = totalPayment,
                    Interest = interest,
                    Principal = principal,
                    EscrowInsurance = escrowInsurance,
                    Balance = balance
                });

                paymentDate = paymentDate.AddMonths(1);
            }

            ScheduleGrid.ItemsSource = schedule;
        }

        public class AmortizationItem
        {
            public int Month { get; set; }
            public DateTime Date { get; set; }
            public decimal Payment { get; set; }
            public decimal Principal { get; set; }
            public decimal Interest { get; set; }
            public decimal EscrowInsurance { get; set; }
            public decimal Balance { get; set; }
        }
    }
}