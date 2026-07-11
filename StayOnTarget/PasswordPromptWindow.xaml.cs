using System.Windows;
using System.Windows.Input;

namespace StayOnTarget;

public partial class PasswordPromptWindow : Window
{
    public string Password { get; private set; } = string.Empty;
    private readonly bool _isNewDatabase;

    public PasswordPromptWindow(bool isNewDatabase)
    {
        InitializeComponent();
        _isNewDatabase = isNewDatabase;

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

        if (string.IsNullOrEmpty(PasswordInput.Password))
        {
            ShowError("Password cannot be empty.");
            return;
        }

        if (_isNewDatabase)
        {
            if (PasswordInput.Password != ConfirmPasswordInput.Password)
            {
                ShowError("Passwords do not match.");
                return;
            }
        }

        Password = PasswordInput.Password;
        DialogResult = true;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorMessage.Text = message;
        ErrorMessage.Visibility = Visibility.Visible;
    }
}
