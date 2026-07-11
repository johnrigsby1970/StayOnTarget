using System.IO;
using System.Windows;
using StayOnTarget.Data;
using StayOnTarget.Services;
using StayOnTarget.ViewModels;

namespace StayOnTarget;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // STEP 1: Tell WPF not to shut down just because a window closes
        Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        
        string dbPath = DatabaseContext.GetDefaultDbPath();
        bool dbExists = File.Exists(dbPath);

        var passwordWindow = new PasswordPromptWindow(!dbExists);
        if (passwordWindow.ShowDialog() == true)
        {
            string password = passwordWindow.Password;
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
        else
        {
            Shutdown();
        }
    }
}