using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using StayOnTarget.Converters;
using StayOnTarget.Models;
using StayOnTarget.ViewModels;

namespace StayOnTarget.Helpers;

public static class DynamicAccountColumnsBehavior {
    public static readonly DependencyProperty AccountsProperty =
        DependencyProperty.RegisterAttached(
            "Accounts",
            typeof(IEnumerable<Account>),
            typeof(DynamicAccountColumnsBehavior),
            new PropertyMetadata(null, OnAccountsChanged));

    public static void SetAccounts(DependencyObject element, IEnumerable<Account> value) =>
        element.SetValue(AccountsProperty, value);

    public static IEnumerable<Account> GetAccounts(DependencyObject element) =>
        (IEnumerable<Account>)element.GetValue(AccountsProperty);

    private static void OnAccountsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is not DataGrid grid) return;

        if (e.NewValue is IEnumerable<Account> accounts) {
            BuildAccountColumns(grid, accounts);
        }
    }

    private static void BuildAccountColumns(DataGrid grid, IEnumerable<Account> accounts) {
        // Retain standard leading columns (Posting Date, Description, Amount, Total Balance, Period Net)
        while (grid.Columns.Count > 5) {
            grid.Columns.RemoveAt(5);
        }

        var sortedAccounts = accounts.OrderBy(a => a.Type switch {
            AccountType.Checking => 0,
            AccountType.CreditCard => 1,
            AccountType.Savings => 2,
            _ => 3
        }).ThenBy(a => a.Name);

        var positiveBrush = Application.Current?.TryFindResource("PositiveBrush") as SolidColorBrush;
        var negativeBrush = Application.Current?.TryFindResource("NegativeBrush") as SolidColorBrush;

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
            colDef1.SetValue(ColumnDefinition.WidthProperty, new GridLength(16, GridUnitType.Pixel));
            var colDef2 = new FrameworkElementFactory(typeof(ColumnDefinition));
            colDef2.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));

            gridFactory.AppendChild(colDef1);
            gridFactory.AppendChild(colDef2);

            // Shape container
            var shapeViewFactory = new FrameworkElementFactory(typeof(ContentControl));
            shapeViewFactory.SetValue(Grid.ColumnProperty, 0);
            shapeViewFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            shapeViewFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);

            shapeViewFactory.SetBinding(ContentControl.ContentProperty, new Binding(".") {
                Converter = new ExpressionConverter<ProjectionItem, object>(item => {
                    if (item.ToAccountId == accountId) {
                        return new System.Windows.Shapes.Rectangle {
                            Width = 8,
                            Height = 8,
                            Fill = positiveBrush,
                            RenderTransform = new RotateTransform(45),
                            RenderTransformOrigin = new Point(0.5, 0.5),
                            Margin = new Thickness(4, 0, 0, 0)
                        };
                    }
                    if (item.FromAccountId == accountId) {
                        return new System.Windows.Shapes.Rectangle {
                            Width = 8,
                            Height = 8,
                            Fill = negativeBrush,
                            Margin = new Thickness(4, 0, 0, 0)
                        };
                    }
                    return null;
                })
            });
            gridFactory.AppendChild(shapeViewFactory);

            // Balance text block
            var balanceFactory = new FrameworkElementFactory(typeof(TextBlock));
            balanceFactory.SetValue(Grid.ColumnProperty, 1);
            balanceFactory.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            balanceFactory.SetBinding(TextBlock.TextProperty, new Binding(".") {
                Converter = new ExpressionConverter<ProjectionItem, string>(item =>
                    item.GetAccountBalance(accountName).ToString("C"))
            });
            gridFactory.AppendChild(balanceFactory);

            // Ghosting effect bindings
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

            column.CellTemplate = new DataTemplate { VisualTree = gridFactory };
            grid.Columns.Add(column);
        }
    }
}