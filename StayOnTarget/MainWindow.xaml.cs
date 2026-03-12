using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using StayOnTarget.ViewModels;

namespace StayOnTarget;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window {
    private readonly MainViewModel _viewModel;

    public MainWindow() {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _viewModel.PropertyChanged += Vm_PropertyChanged;
        if (_viewModel.Accounts != null)
            UpdateProjectionColumns(_viewModel.Accounts.Select(a => a.Name));
    }

    private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Accounts) && sender == DataContext)
        {
            if (DataContext is MainViewModel vm && vm.Accounts != null)
                UpdateProjectionColumns(vm.Accounts.Select(a => a.Name));
        }
    }

    private void UpdateProjectionColumns(IEnumerable<string> accountNames)
    {
        // Keep the first 5 columns (Date, Description, Amount, Total Balance, Period Net)
        while (ProjectionGrid.Columns.Count > 5)
        {
            ProjectionGrid.Columns.RemoveAt(5);
        }

        foreach (var accountName in accountNames)
        {
            var column = new DataGridTextColumn
            {
                Header = accountName,
                Binding = new Binding
                {
                    Converter = new AccountBalanceConverter(),
                    ConverterParameter = accountName,
                    StringFormat = "C"
                },
                Width = 90
            };
            ProjectionGrid.Columns.Add(column);
        }
    }
}

public class AccountBalanceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is ProjectionItem item && parameter is string accountName)
        {
            return item.GetAccountBalance(accountName);
        }
        return 0m;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}