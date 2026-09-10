using System.Windows;
using System.Windows.Media;
using Windows.Security.Credentials;
using Windows.Security.Credentials.UI;
using SkiaSharp;
using StayOnTarget.Helpers;
using StayOnTarget.Models;

namespace StayOnTarget;

public static class HelperMethods {
    
    public static void SaveDatabaseKeyToWindowsVault(string password, string ddFileName = "MasterKey") {
        
        VaultManager.SaveDatabaseKey(ddFileName, password);
        
        // var vault = new PasswordVault();
        //
        // // Resource name acts as the unique identifier for your app
        // // UserName can just be a static identifier like "MasterKey"
        // var credential = new PasswordCredential("StayOnTarget_DB_Vault", "MasterKey", password);
        //
        // vault.Add(credential);
    }
    
    public static SKColor GetSkColorFromBrush(string resourceKey, double opacity = 0.35)
    {
        if (Application.Current?.TryFindResource(resourceKey) is SolidColorBrush brush)
        {
            var c = brush.Color;
            // Combine brush color's native alpha with your desired opacity modifier
            byte alpha = (byte)(c.A * opacity); 
            return new SKColor(c.R, c.G, c.B, alpha);
        }

        // Default fallback (e.g., DarkRed with 35% opacity)
        return new SKColor(139, 0, 0, (byte)(255 * opacity));
    }
    
    public static async Task<bool> IsWindowsHelloFullySetup() {
        try {
            // 1. Hardware check: Does the machine physically support biometric or PIN auth?
            bool hardwareSupported = await KeyCredentialManager.IsSupportedAsync();
            if (!hardwareSupported) return false;

            // 2. Enrollment check: Has the user actually set up a PIN, Face, or Fingerprint?
            // This returns a UserConsentVerifierAvailability enum!
            UserConsentVerifierAvailability status = await UserConsentVerifier.CheckAvailabilityAsync();

            // Check against the correct enum type
            return status == UserConsentVerifierAvailability.Available;
        }
        catch (Exception) {
            // Vault or verification failed/canceled; gracefully dsiallow Windows Hello
            return false;
        }
    }

    public static async Task<string?> TryUnlockWithWindowsHello(string ddFileName = "MasterKey") {
        // 1. Check if the machine actually has Windows Hello biometric/PIN capability configured
        bool isAvailable = await KeyCredentialManager.IsSupportedAsync();
        if (!isAvailable) return null;

        try {
            // 2. Request modern verification directly. 
            // For UserConsentVerifier, the OS automatically handles anchoring the system overlay 
            // over the active thread without needing explicit HWND casting.
            var consentResult = await UserConsentVerifier.RequestVerificationAsync(
                "Authorize StayOnTarget to securely decrypt your local financial database."
            );

            // 3. If fingerprint/PIN matches, safely fetch the password from the vault
            if (consentResult == UserConsentVerificationResult.Verified) {
                return VaultManager.GetDatabaseKey(ddFileName);
                // var vault = new PasswordVault();
                // var credential = vault.Retrieve("StayOnTarget_DB_Vault", "MasterKey");
                // credential.RetrievePassword();
                // return credential.Password;
            }
        }
        catch (Exception) {
            // Vault or verification failed/canceled; gracefully fall back to regular password prompt
            return null;
        }

        return null;
    }
    
    public static DateTime GetCurrentPeriodStartForFixedDays(
        DateTime anchorStartDate, 
        DateTime today, 
        int frequencyDays)
    {
        // Ensure dates are compared purely by Date (strip time)
        anchorStartDate = anchorStartDate.Date;
        today = today.Date;

        if (today < anchorStartDate)
            return anchorStartDate;

        // Total days elapsed since the original anchor start date
        int totalDaysElapsed = (today - anchorStartDate).Days;

        // Integer division drops the remainder, giving the number of completed periods
        int periodsElapsed = totalDaysElapsed / frequencyDays;

        // Jump directly to the current period start date
        return anchorStartDate.AddDays(periodsElapsed * frequencyDays);
    }
    
    public static DateTime GetCurrentPeriodStartMonthly(DateTime anchorStartDate, DateTime today)
    {
        anchorStartDate = anchorStartDate.Date;
        today = today.Date;

        if (today < anchorStartDate)
            return anchorStartDate;

        // Total elapsed months between the two years/months
        int totalMonthsElapsed = ((today.Year - anchorStartDate.Year) * 12) + today.Month - anchorStartDate.Month;

        DateTime candidate = anchorStartDate.AddMonths(totalMonthsElapsed);

        // If today hasn't reached the candidate day yet in the current month, step back 1 month
        if (today < candidate)
        {
            candidate = anchorStartDate.AddMonths(totalMonthsElapsed - 1);
        }

        return candidate;
    }
    
    public static DateTime GetCurrentPeriodStartSemiMonthly(
        DateTime today, 
        int firstPayDay = 1, 
        int secondPayDay = 15)
    {
        today = today.Date;

        if (today.Day >= secondPayDay)
        {
            return new DateTime(today.Year, today.Month, secondPayDay);
        }
    
        if (today.Day >= firstPayDay)
        {
            return new DateTime(today.Year, today.Month, firstPayDay);
        }

        // Today is before the 1st pay day of the month -> rolls back to 2nd pay day of previous month
        DateTime prevMonth = today.AddMonths(-1);
        return new DateTime(prevMonth.Year, prevMonth.Month, secondPayDay);
    }
    
    public static DateTime GetCurrentPeriodStart(
        DateTime anchorStartDate, 
        DateTime today, 
        Frequency frequency)
    {
        int days = frequency switch
        {
            Frequency.Weekly => 7,
            Frequency.BiWeekly => 14,
            Frequency.EveryFourWeeks => 28,
            _ => 0
        };

        return frequency switch
        {
            Frequency.Weekly or Frequency.BiWeekly or Frequency.EveryFourWeeks => 
                GetCurrentPeriodStartForFixedDays(anchorStartDate, today, days),

            Frequency.Monthly => 
                GetCurrentPeriodStartMonthly(anchorStartDate, today),

            Frequency.SemiMonthly => 
                GetCurrentPeriodStartSemiMonthly(today, anchorStartDate.Day, 15),

            _ => throw new ArgumentOutOfRangeException(nameof(frequency))
        };
    }
}