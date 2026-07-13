using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Data.Sqlite;
using Serilog;
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
        LogConfig.Initialize();
        base.OnStartup(e);

        SetupGlobalExceptionHandling();

        Log.Information("OnStartup started.");
        
        // STEP 1: Tell WPF not to shut down just because a window closes
        Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        
        string dbPath = DatabaseContext.GetDefaultDbPath();
        Log.Information("Database path: {DbPath}", dbPath);
        bool dbExists = File.Exists(dbPath);
        string? password = null;

        // Try Windows Hello first if database exists
        if (dbExists)
        {
            Log.Information("Database exists, attempting auto-unlock.");
            try
            {
                bool userWantsHello = StayOnTarget.Properties.Settings.Default.UseWindowsHello;
                if (userWantsHello) {
                    password = await Helpers.TryUnlockWithWindowsHello();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during Windows Hello unlock attempt.");
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
                    
                    Log.Information("Auto-unlock successful.");
                    // Success! Launch MainWindow
                    LaunchMainWindow(dbPath, password);
                    return;
                }
                catch (SqliteException ex)
                {
                    Log.Warning(ex, "Vault password invalid or database error during auto-unlock. Clearing vault.");
                    // Vault password invalid (e.g. database replaced), clear it
                    var vault = new PasswordVault();
                    try
                    {
                        var credential = vault.Retrieve("StayOnTarget_DB_Vault", "MasterKey");
                        vault.Remove(credential);
                    }
                    catch (Exception vaultEx)
                    {
                        Log.Error(vaultEx, "Failed to remove invalid credential from vault.");
                    }
                }
                catch (Exception ex)
                {
                    Log.Fatal(ex, "Unexpected error during auto-unlock.");
                    MessageBox.Show($"Unexpected error during auto-unlock: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        else
        {
            Log.Information("Database does not exist. User will need to create one.");
        }

        try
        {
            Log.Information("Showing PasswordPromptWindow.");
            var passwordWindow = new PasswordPromptWindow(!dbExists, dbPath);
            if (passwordWindow.ShowDialog() == true)
            {
                Log.Information("Password provided, launching MainWindow.");
                LaunchMainWindow(dbPath, passwordWindow.Password);
            }
            else
            {
                Log.Information("Password prompt cancelled. Shutting down.");
                Shutdown();
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Error during password prompt or main window launch.");
            MessageBox.Show($"Critical error during startup: {ex.Message}", "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void SetupGlobalExceptionHandling()
    {
        DispatcherUnhandledException += (s, e) =>
        {
            Log.Fatal(e.Exception, "Unhandled UI dispatcher exception.");
            MessageBox.Show($"An unexpected UI error occurred: {e.Exception.Message}", "Unexpected Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            Log.Fatal(ex, "Unhandled AppDomain exception. Terminating: {IsTerminating}", e.IsTerminating);
            if (e.IsTerminating)
            {
                MessageBox.Show($"A critical error occurred and the application must close: {ex?.Message}", "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Log.Error(e.Exception, "Unobserved task exception.");
            e.SetObserved();
        };
    }

    private void LaunchMainWindow(string dbPath, string password)
    {
        try
        {
            Log.Information("Initializing BudgetService and MainWindow.");
            var budgetService = new BudgetService(dbPath, password);
            var viewModel = new MainViewModel(budgetService);
            var mainWindow = new MainWindow(viewModel);
            
            // STEP 2: Make this the official main window
            Current.MainWindow = mainWindow;
            
            // STEP 3: Change the shutdown mode back so closing the main window exits the app
            Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
            
            mainWindow.Show();
            Log.Information("MainWindow shown.");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to initialize database or main window.");
            MessageBox.Show($"Failed to initialize database: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LogConfig.Shutdown();
        base.OnExit(e);
    }
}