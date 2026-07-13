using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using StayOnTarget.Models;
using StayOnTarget.ViewModels;
using System.Windows.Shapes;
using Serilog;

namespace StayOnTarget;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window {
    private readonly MainViewModel _viewModel;

    public MainWindow() {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    public MainWindow(MainViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e) {
        try {
            _viewModel.PropertyChanged += Vm_PropertyChanged;
            if (_viewModel.Accounts != null)
                UpdateProjectionColumns(_viewModel.Accounts);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in MainWindow_Loaded.");
        }
    }

    private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        try {
            if (e.PropertyName == nameof(MainViewModel.Accounts) && sender == DataContext) {
                if (DataContext is MainViewModel vm && vm.Accounts != null)
                    UpdateProjectionColumns(vm.Accounts);
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in Vm_PropertyChanged for {PropertyName}.", e.PropertyName);
        }
    }

    private void UpdateProjectionColumns(IEnumerable<Account> accounts) {
        // Keep the first 5 columns (Date, Description, Amount, Total Balance, Period Net)
        while (ProjectionGrid.Columns.Count > 5) {
            ProjectionGrid.Columns.RemoveAt(5);
        }

        var sortedAccounts = accounts.OrderBy(a => a.Type switch {
            AccountType.Checking => 0,
            AccountType.CreditCard => 1,
            AccountType.Savings => 2,
            _ => 3
        }).ThenBy(a => a.Name);

        foreach (var account in sortedAccounts) {
            var accountName = account.Name;
            var accountId = account.Id;

            var column = new DataGridTemplateColumn {
                Header = accountName,
                Width = 110,
                IsReadOnly = true
            };

            var gridFactory = new FrameworkElementFactory(typeof(Grid));
            gridFactory.SetValue(Control.PaddingProperty, new Thickness(6, 0, 6, 0));

            var colDef1 = new FrameworkElementFactory(typeof(ColumnDefinition));
            colDef1.SetValue(ColumnDefinition.WidthProperty,
                new GridLength(16, GridUnitType.Pixel)); // Room for the shape
            var colDef2 = new FrameworkElementFactory(typeof(ColumnDefinition));
            colDef2.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));

            gridFactory.AppendChild(colDef1);
            gridFactory.AppendChild(colDef2);

            // 1. The Shape Container (Column 0)
            var shapeViewFactory = new FrameworkElementFactory(typeof(ContentControl));
            shapeViewFactory.SetValue(Grid.ColumnProperty, 0);
            shapeViewFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            shapeViewFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);

            // Dynamic Template Selector using ExpressionConverter to generate the right shape framework element
            shapeViewFactory.SetBinding(ContentControl.ContentProperty, new Binding(".") 
            {
                Converter = new ExpressionConverter<ProjectionItem, object>(item =>
                {
                    if (item.ToAccountId == accountId)
                    {
                        // INFLOW: Diamond shape (Square rotated 45 degrees)
                        var diamond = new Rectangle
                        {
                            Width = 8,
                            Height = 8,
                            Fill = new SolidColorBrush(Colors.Green) { Opacity = 0.35 }, // Kept at your preferred 35%
                            RenderTransform = new System.Windows.Media.RotateTransform(45),
                            RenderTransformOrigin = new Point(0.5, 0.5),
                            // Left margin set to 4, top/right/bottom set to 0
                            Margin = new Thickness(4, 0, 0, 0) 
                        };
                        return diamond;
                    }
                    if (item.FromAccountId == accountId)
                    {
                        // OUTFLOW: Standard Square
                        var square = new Rectangle
                        {
                            Width = 8,
                            Height = 8,
                            Fill = new SolidColorBrush(Colors.DarkRed) { Opacity = 0.35 }, // Kept at your preferred 35%
                            // Matching left margin of 4
                            Margin = new Thickness(4, 0, 0, 0) 
                        };
                        return square;
                    }

                    return null;
                })
            });
            gridFactory.AppendChild(shapeViewFactory);

            // 2. The Balance TextBlock (Column 1)
            var balanceFactory = new FrameworkElementFactory(typeof(TextBlock));
            balanceFactory.SetValue(Grid.ColumnProperty, 1);
            balanceFactory.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            balanceFactory.SetBinding(TextBlock.TextProperty, new Binding(".") {
                Converter = new ExpressionConverter<ProjectionItem, string>(item =>
                    item.GetAccountBalance(accountName).ToString("C")),
            });
            gridFactory.AppendChild(balanceFactory);

            // 3. Apply Opacity and Font Weight (Ghosting Effect)
            gridFactory.SetBinding(Grid.OpacityProperty, new Binding(".") {
                Converter = new ExpressionConverter<ProjectionItem, double>(item =>
                    (item.ToAccountId == accountId || item.FromAccountId == accountId) ? 1.0 : 0.45)
            });

            gridFactory.SetBinding(Control.FontWeightProperty, new Binding(".") {
                Converter = new ExpressionConverter<ProjectionItem, FontWeight>(item =>
                    (item.ToAccountId == accountId || item.FromAccountId == accountId)
                        ? FontWeights.SemiBold
                        : FontWeights.Normal)
            });

            var template = new DataTemplate { VisualTree = gridFactory };
            column.CellTemplate = template;

            ProjectionGrid.Columns.Add(column);
        }

//         foreach (var account in sortedAccounts)
// {
//     var accountName = account.Name;
//     var accountId = account.Id; 
//
//     var column = new DataGridTemplateColumn
//     {
//         Header = accountName,
//         Width = 110, // Bumped slightly to ensure plenty of space for both arrow and currency
//         IsReadOnly = true
//     };
//
//     // 1. Create a root Grid factory to hold the two sections
//     var gridFactory = new FrameworkElementFactory(typeof(Grid));
//     gridFactory.SetValue(Control.PaddingProperty, new Thickness(6, 0, 6, 0));
//     
//     // Define two columns: Left for the arrow (fixed/auto size), Right for the balance (stretches)
//     var colDef1 = new FrameworkElementFactory(typeof(ColumnDefinition));
//     colDef1.SetValue(ColumnDefinition.WidthProperty, new GridLength(15, GridUnitType.Pixel)); // Fixed width ensures arrows align vertically
//     var colDef2 = new FrameworkElementFactory(typeof(ColumnDefinition));
//     colDef2.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
//
//     gridFactory.AppendChild(colDef1);
//     gridFactory.AppendChild(colDef2);
//
//     // 2. The Arrow TextBlock (Left Aligned in Column 0)
//     var arrowFactory = new FrameworkElementFactory(typeof(TextBlock));
//     arrowFactory.SetValue(Grid.ColumnProperty, 0);
//     arrowFactory.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Left);
//     // arrowFactory.SetBinding(TextBlock.TextProperty, new Binding(".") {
//     //     Converter = new ExpressionConverter<ProjectionItem, string>(item => 
//     //         item.ToAccountId == accountId ? "+" : item.FromAccountId == accountId ? "-" : "")
//     // });
//     arrowFactory.SetBinding(TextBlock.TextProperty, new Binding(".") {
//         Converter = new ExpressionConverter<ProjectionItem, string>(item => 
//             (item.ToAccountId == accountId || item.FromAccountId == accountId) ? "•" : "") // Clean, soft dot
//     });
//     // arrowFactory.SetBinding(TextBlock.TextProperty, new Binding(".") {
//     //     Converter = new ExpressionConverter<ProjectionItem, string>(item => 
//     //         item.ToAccountId == accountId ? "▲" : item.FromAccountId == accountId ? "▼" : "")
//     // });
//     // arrowFactory.SetBinding(TextBlock.ForegroundProperty, new Binding(".") {
//     //     Converter = new ExpressionConverter<ProjectionItem, System.Windows.Media.Brush>(item =>
//     //         item.ToAccountId == accountId ? System.Windows.Media.Brushes.Green :
//     //         item.FromAccountId == accountId ? System.Windows.Media.Brushes.DarkRed : System.Windows.Media.Brushes.Transparent)
//     // });
//     arrowFactory.SetBinding(TextBlock.ForegroundProperty, new Binding(".") {
//         Converter = new ExpressionConverter<ProjectionItem, System.Windows.Media.Brush>(item =>
//         {
//             if (item.ToAccountId == accountId)
//             {
//                 // Green with 35% opacity
//                 return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green) { Opacity = 0.5 };
//             }
//             if (item.FromAccountId == accountId)
//             {
//                 // DarkRed with 35% opacity
//                 return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.DarkRed) { Opacity = 0.5 };
//             }
//         
//             return System.Windows.Media.Brushes.Transparent;
//         })
//     });
//     gridFactory.AppendChild(arrowFactory);
//
//     // 3. The Balance TextBlock (Right Aligned in Column 1)
//     var balanceFactory = new FrameworkElementFactory(typeof(TextBlock));
//     balanceFactory.SetValue(Grid.ColumnProperty, 1);
//     balanceFactory.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Right);
//     balanceFactory.SetBinding(TextBlock.TextProperty, new Binding(".") {
//         Converter = new ExpressionConverter<ProjectionItem, string>(item => 
//             item.GetAccountBalance(accountName).ToString("C")),
//     });
//     gridFactory.AppendChild(balanceFactory);
//
//     // 4. Apply Opacity and Font Weight to the entire Grid container (Ghosting Effect)
//     gridFactory.SetBinding(Grid.OpacityProperty, new Binding(".") {
//         Converter = new ExpressionConverter<ProjectionItem, double>(item =>
//             (item.ToAccountId == accountId || item.FromAccountId == accountId) ? 1.0 : 0.45)
//     });
//
// // Use Control.FontWeightProperty as the attached property instead of Grid
//     gridFactory.SetBinding(Control.FontWeightProperty, new Binding(".") {
//         Converter = new ExpressionConverter<ProjectionItem, FontWeight>(item =>
//             (item.ToAccountId == accountId || item.FromAccountId == accountId) ? FontWeights.SemiBold : FontWeights.Normal)
//     });
//
//     // Wrap it into a DataTemplate and assign to column
//     var template = new DataTemplate { VisualTree = gridFactory };
//     column.CellTemplate = template;
//
//     ProjectionGrid.Columns.Add(column);
// }
//         
//         // foreach (var account in sortedAccounts) {
//         //     var accountName = account.Name;
//         //     var accountId = account.Id;
//         //
//         //     var bindingParameter = Tuple.Create(accountName, accountId);
//         //
//         //     var column = new DataGridTemplateColumn {
//         //         Header = accountName,
//         //         Width = 100,
//         //         IsReadOnly = true
//         //     };
//         //
//         //     var textBlockFactory = new FrameworkElementFactory(typeof(TextBlock));
//         //     textBlockFactory.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Right);
//         //     textBlockFactory.SetValue(TextBlock.PaddingProperty, new Thickness(0, 0, 6, 0));
//         //
//         //     // --- REMOVED: textBlockFactory.SetBinding(TextBlock.DataContextProperty, mainBinding); ---
//         //
//         //     // 1. Balance Run: Binds directly to the row item using the converter
//         //     var balanceRunFactory = new FrameworkElementFactory(typeof(Run));
//         //     var balanceBinding = new Binding(".") // "." means bind to the entire row item (ProjectionItem)
//         //     {
//         //         Converter = new AccountBalanceConverter(),
//         //         ConverterParameter = bindingParameter,
//         //         StringFormat = "C" // The converter now needs to return a string or decimal directly
//         //     };
//         //     // Note: Since the converter used to return the whole AccountDisplayInfo object,
//         //     // we should modify the binding path if we want to extract just the string or handle it cleanly.
//         //
//         //     // Let's make it robust: Bind the Run's text to a specific property of the converter's output object
//         //     var cellDataBinding = new Binding(".") {
//         //         Converter = new AccountBalanceConverter(),
//         //         ConverterParameter = bindingParameter
//         //     };
//         //
//         //     // To cleanly evaluate sub-properties without overriding DataContext, 
//         //     // we can use separate mini-bindings or adjust the converter. 
//         //     // The absolute cleanest way in dynamic code without changing DataContext is:
//         //
//         //     balanceRunFactory.SetBinding(Run.TextProperty, new Binding(".") {
//         //         Converter = new ExpressionConverter<ProjectionItem, string>(item =>
//         //             item.GetAccountBalance(accountName).ToString("C")),
//         //     });
//         //     textBlockFactory.AppendChild(balanceRunFactory);
//         //
//         //     // 2. Arrow Run
//         //     var arrowRunFactory = new FrameworkElementFactory(typeof(Run));
//         //     arrowRunFactory.SetBinding(Run.TextProperty, new Binding(".") {
//         //         Converter = new ExpressionConverter<ProjectionItem, string>(item =>
//         //             item.ToAccountId == accountId ? " ▲" : item.FromAccountId == accountId ? " ▼" : "")
//         //     });
//         //
//         //     arrowRunFactory.SetBinding(Run.ForegroundProperty, new Binding(".") {
//         //         Converter = new ExpressionConverter<ProjectionItem, System.Windows.Media.Brush>(item =>
//         //             item.ToAccountId == accountId ? System.Windows.Media.Brushes.Green :
//         //             item.FromAccountId == accountId ? System.Windows.Media.Brushes.DarkRed :
//         //             System.Windows.Media.Brushes.Transparent)
//         //     });
//         //     textBlockFactory.AppendChild(arrowRunFactory);
//         //
//         //     // 3. Opacity (Ghosting Effect)
//         //     textBlockFactory.SetBinding(TextBlock.OpacityProperty, new Binding(".") {
//         //         Converter = new ExpressionConverter<ProjectionItem, double>(item =>
//         //             (item.ToAccountId == accountId || item.FromAccountId == accountId) ? 1.0 : 0.45)
//         //     });
//         //
//         //     // 4. Font Weight
//         //     textBlockFactory.SetBinding(TextBlock.FontWeightProperty, new Binding(".") {
//         //         Converter = new ExpressionConverter<ProjectionItem, FontWeight>(item =>
//         //             (item.ToAccountId == accountId || item.FromAccountId == accountId)
//         //                 ? FontWeights.SemiBold
//         //                 : FontWeights.Normal)
//         //     });
//         //
//         //     var template = new DataTemplate { VisualTree = textBlockFactory };
//         //     column.CellTemplate = template;
//         //
//         //     ProjectionGrid.Columns.Add(column);
//         // }
//
//         // foreach (var account in sortedAccounts)
//         // {
//         //     var accountName = account.Name;
//         //     var column = new DataGridTextColumn
//         //     {
//         //         Header = accountName,
//         //         Binding = new Binding
//         //         {
//         //             Converter = new AccountBalanceConverter(),
//         //             ConverterParameter = accountName,
//         //             StringFormat = "C"
//         //         },
//         //         Width = 90,
//         //         IsReadOnly = true
//         //     };
//         //     ProjectionGrid.Columns.Add(column);
//         // }
//     }
    }
}

public class AccountBalanceConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
        try {
            if (value is ProjectionItem item && parameter is Tuple<string, int> accountParam) {
                string accountName = accountParam.Item1;
                int accountId = accountParam.Item2;

                decimal balance = item.GetAccountBalance(accountName);
                BalanceImpact impact = BalanceImpact.Muted;

                // Determine if this specific account was involved in the transaction
                if (item.ToAccountId == accountId) {
                    impact = BalanceImpact.Increased;
                }
                else if (item.FromAccountId == accountId) {
                    impact = BalanceImpact.Decreased;
                }

                return new AccountDisplayInfo {
                    Balance = balance,
                    Impact = impact
                };
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in AccountBalanceConverter.");
        }

        return new AccountDisplayInfo { Balance = 0m, Impact = BalanceImpact.Muted };
    }

    public object ConvertBack(object value, Type targetType, object parameter,
        System.Globalization.CultureInfo culture) {
        throw new NotImplementedException();
    }
}

// public class AccountBalanceConverter : IValueConverter
// {
//     public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
//     {
//         if (value is ProjectionItem item && parameter is string accountName)
//         {
//             return item.GetAccountBalance(accountName);
//         }
//         return 0m;
//     }
//
//     public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
//     {
//         throw new NotImplementedException();
//     }
// }

public class ExpressionConverter<TIn, TOut> : IValueConverter {
    private readonly Func<TIn, TOut> _expression;
    public ExpressionConverter(Func<TIn, TOut> expression) => _expression = expression;

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
        try {
            if (value is TIn typedValue) return _expression(typedValue);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in ExpressionConverter for type {InType} to {OutType}.", typeof(TIn).Name, typeof(TOut).Name);
        }
        return default(TOut);
    }

    public object
        ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        throw new NotImplementedException();
}