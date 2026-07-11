using System.Windows;
using System.Windows.Input;
using Microsoft.Data.Sqlite;
using StayOnTarget.Data;

namespace StayOnTarget;

public partial class PasswordPromptWindow : Window
{
    public string Password { get; private set; } = string.Empty;
    private readonly bool _isNewDatabase;
    private readonly string _dbPath;
    private int _failureCount = 0;

    public PasswordPromptWindow(bool isNewDatabase, string dbPath)
    {
        InitializeComponent();
        _isNewDatabase = isNewDatabase;
        _dbPath = dbPath;

        if (_isNewDatabase)
        {
            Title = "StayOnTarget - Create Master Password";
            InstructionText.Text = "No database found. Please set a new master password to secure your data.";
            ConfirmPasswordLabel.Visibility = Visibility.Visible;
            ConfirmPasswordInput.Visibility = Visibility.Visible;
            Height = 300;
        }
        
        PasswordInput.Focus();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        ProcessInput();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void PasswordInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ProcessInput();
        }
    }

    private void ProcessInput()
    {
        ErrorMessage.Visibility = Visibility.Collapsed;
        ErrorMessageBorder.Visibility = Visibility.Collapsed;

        string inputPassword = PasswordInput.Password;

        if (string.IsNullOrEmpty(inputPassword))
        {
            ShowError("Password cannot be empty.");
            return;
        }

        if (_isNewDatabase)
        {
            if (inputPassword != ConfirmPasswordInput.Password)
            {
                ShowError("Passwords do not match.");
                return;
            }
        }

        // Active validation
        try
        {
            var dbContext = new DatabaseContext(_dbPath, inputPassword);
            using (var connection = dbContext.GetConnection())
            {
                connection.Open();
                // If it's a new database, DatabaseContext constructor already initialized it.
                // If it's an existing one, Open() will throw if the password is wrong.
            }

            // Success!
            Password = inputPassword;
            if (UseWindowsHelloCheckBox.IsChecked == true)
            {
                try
                {
                    StayOnTarget.Properties.Settings.Default.UseWindowsHello = true;
                    StayOnTarget.Properties.Settings.Default.Save();
                    Helpers.SaveDatabaseKeyToWindowsVault(Password);
                }
                catch (Exception ex)
                {
                    // Gracefully log or handle vault storage issues without crashing the app setup
                    MessageBox.Show($"Could not enable Windows Hello: {ex.Message}", "Security Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            DialogResult = true;
            Close();
        }
        catch (SqliteException)
        {
            _failureCount++;
            PasswordInput.Password = "";
            ConfirmPasswordInput.Password = "";
            PasswordInput.Focus();

            if (_failureCount >= 3)
            {
                MessageBox.Show("Too many failed attempts. Shutting down.", "Security", MessageBoxButton.OK, MessageBoxImage.Stop);
                Application.Current.Shutdown();
                return;
            }

            ShowError($"Invalid password. (Attempt {_failureCount} of 3)");
        }
        catch (Exception ex)
        {
            ShowError($"Validation error: {ex.Message}");
        }
    }

    private void ShowError(string message)
    {
        ErrorMessage.Text = message;
        ErrorMessage.Visibility = Visibility.Visible;
        ErrorMessageBorder.Visibility = Visibility.Visible;
    }
}
