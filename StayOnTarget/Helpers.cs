using Windows.Security.Credentials;
using Windows.Security.Credentials.UI;

namespace StayOnTarget;

public static class Helpers {
    
    public static void SaveDatabaseKeyToWindowsVault(string password) {
        var vault = new PasswordVault();

        // Resource name acts as the unique identifier for your app
        // UserName can just be a static identifier like "MasterKey"
        var credential = new PasswordCredential("StayOnTarget_DB_Vault", "MasterKey", password);

        vault.Add(credential);
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

    public static async Task<string?> TryUnlockWithWindowsHello() {
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
                var vault = new PasswordVault();
                var credential = vault.Retrieve("StayOnTarget_DB_Vault", "MasterKey");
                credential.RetrievePassword();
                return credential.Password;
            }
        }
        catch (Exception) {
            // Vault or verification failed/canceled; gracefully fall back to regular password prompt
            return null;
        }

        return null;
    }
}