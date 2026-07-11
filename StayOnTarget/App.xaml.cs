using System.IO;
using System.Windows;
using Microsoft.Data.Sqlite;
using StayOnTarget.Data;
using StayOnTarget.Services;
using StayOnTarget.ViewModels;
using Windows.Security.Credentials;

namespace StayOnTarget;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // STEP 1: Tell WPF not to shut down just because a window closes
        Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        
        string dbPath = DatabaseContext.GetDefaultDbPath();
        bool dbExists = File.Exists(dbPath);
        string? password = null;

        // Try Windows Hello first if database exists
        if (dbExists)
        {
            // // 1. Create a quick, hidden window to serve as the Win32 Owner Handle
            // Window hiddenAnchorWindow = new Window
            // {
            //     Width = 0,
            //     Height = 0,
            //     WindowStyle = WindowStyle.None,
            //     ShowInTaskbar = false,
            //     Opacity = 0
            // };
            // hiddenAnchorWindow.Show(); // Must be shown to generate an HWND handle!
            
            try
            {
                bool userWantsHello = StayOnTarget.Properties.Settings.Default.UseWindowsHello;
                if (userWantsHello) {
                    // 2. Pass this active anchor to your helper method
                    password = await Helpers.TryUnlockWithWindowsHello();
                }
            }
            finally
            {
                // // 3. Immediately close it so it leaves memory
                // hiddenAnchorWindow.Close();
            }
            
            if (password != null)
            {
                try
                {
                    // Verify the password from vault works
                    var dbContext = new DatabaseContext(dbPath, password);
                    using (var connection = dbContext.GetConnection())
                    {
                        connection.Open();
                    }
                    
                    // Success! Launch MainWindow
                    LaunchMainWindow(dbPath, password);
                    return;
                }
                catch (SqliteException)
                {
                    // Vault password invalid (e.g. database replaced), clear it
                    var vault = new PasswordVault();
                    try
                    {
                        var credential = vault.Retrieve("StayOnTarget_DB_Vault", "MasterKey");
                        vault.Remove(credential);
                    }
                    catch { /* Ignore */ }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Unexpected error during auto-unlock: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        var passwordWindow = new PasswordPromptWindow(!dbExists, dbPath);
        if (passwordWindow.ShowDialog() == true)
        {
            LaunchMainWindow(dbPath, passwordWindow.Password);
        }
        else
        {
            Shutdown();
        }
    }

    private void LaunchMainWindow(string dbPath, string password)
    {
        try
        {
            var budgetService = new BudgetService(dbPath, password);
            var viewModel = new MainViewModel(budgetService);
            var mainWindow = new MainWindow(viewModel);
            
            // STEP 2: Make this the official main window
            Current.MainWindow = mainWindow;
            
            // STEP 3: Change the shutdown mode back so closing the main window exits the app
            Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
            
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to initialize database: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }
}