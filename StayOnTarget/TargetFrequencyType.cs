using System.ComponentModel.DataAnnotations;

namespace StayOnTarget;

public enum TargetFrequencyType
{
    [Display(Name = "Paycheck Frequency")]
    PaycheckFrequency = 0, // <--- Tied directly to the associated paycheck's schedule
    Weekly = 1,
    [Display(Name = "Bi-Weekly")]
    BiWeekly = 2,
    [Display(Name = "Semi-Monthly")]
    SemiMonthly = 3,
    Monthly = 4,
    Quarterly = 5,
    Annual = 6,
    Custom = 7,
    [Display(Name = "Every Four Weeks")]
    EveryFourWeeks = 8
}