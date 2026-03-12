using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using StayOnTarget.Models;
using StayOnTarget.ViewModels;

namespace StayOnTarget;

public class ProjectionChartControl : UserControl
{
    public static readonly DependencyProperty ProjectionsProperty =
        DependencyProperty.Register(nameof(Projections), typeof(ObservableCollection<ProjectionItem>), typeof(ProjectionChartControl),
            new PropertyMetadata(null, OnDataChanged));

    public static readonly DependencyProperty AccountsProperty =
        DependencyProperty.Register(nameof(Accounts), typeof(ObservableCollection<Account>), typeof(ProjectionChartControl),
            new PropertyMetadata(null, OnDataChanged));

    public ObservableCollection<ProjectionItem> Projections
    {
        get => (ObservableCollection<ProjectionItem>)GetValue(ProjectionsProperty);
        set => SetValue(ProjectionsProperty, value);
    }

    public ObservableCollection<Account> Accounts
    {
        get => (ObservableCollection<Account>)GetValue(AccountsProperty);
        set => SetValue(AccountsProperty, value);
    }

    private readonly Canvas _canvas;

    public ProjectionChartControl()
    {
        _canvas = new Canvas { Background = Brushes.Transparent };
        Content = _canvas;
        SizeChanged += (s, e) => DrawChart();
    }

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ProjectionChartControl control)
        {
            if (e.OldValue is INotifyCollectionChanged oldCollection)
            {
                oldCollection.CollectionChanged -= control.CollectionChanged;
            }
            if (e.NewValue is INotifyCollectionChanged newCollection)
            {
                newCollection.CollectionChanged += control.CollectionChanged;
            }
            control.DrawChart();
        }
    }

    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DrawChart();
    }

    private void DrawChart()
    {
        _canvas.Children.Clear();

        if (Projections == null || !Projections.Any() || ActualWidth == 0 || ActualHeight == 0)
            return;

        var items = Projections.ToList();
        var accounts = Accounts?.ToList() ?? new List<Account>();
        var includedAccounts = accounts.Where(a => a.IncludeInTotal).ToList();

        decimal minBalance = decimal.MaxValue;
        decimal maxBalance = decimal.MinValue;

        foreach (var item in items)
        {
            maxBalance = Math.Max(maxBalance, item.Balance);
            minBalance = Math.Min(minBalance, item.Balance);

            foreach (var acc in includedAccounts)
            {
                decimal bal = item.GetAccountBalance(acc.Name);
                maxBalance = Math.Max(maxBalance, bal);
                minBalance = Math.Min(minBalance, bal);
            }
        }

        // Add some padding
        decimal range = maxBalance - minBalance;
        if (range == 0) range = 100;
        minBalance -= range * 0.1m;
        maxBalance += range * 0.1m;
        range = maxBalance - minBalance;

        double width = ActualWidth;
        double height = ActualHeight;

        // Total Balance Line
        DrawLine(items, it => (double)it.Balance, Brushes.Blue, 2, minBalance, (double)range, "Total Balance");

        // Individual Accounts
        var colors = new Brush[] { Brushes.Green, Brushes.Red, Brushes.Orange, Brushes.Purple, Brushes.Teal, Brushes.Brown, Brushes.Magenta };
        for (int i = 0; i < includedAccounts.Count; i++)
        {
            var acc = includedAccounts[i];
            var brush = colors[i % colors.Length];
            DrawLine(items, it => (double)it.GetAccountBalance(acc.Name), brush, 1, minBalance, (double)range, acc.Name);
        }
        
        // Zero Line
        if (minBalance < 0 && maxBalance > 0)
        {
            double zeroY = height - ((0 - (double)minBalance) / (double)range * height);
            var line = new Line
            {
                X1 = 0,
                Y1 = zeroY,
                X2 = width,
                Y2 = zeroY,
                Stroke = Brushes.Gray,
                StrokeDashArray = new DoubleCollection { 2, 2 }
            };
            _canvas.Children.Add(line);
        }
    }

    private void DrawLine(List<ProjectionItem> items, Func<ProjectionItem, double> valueSelector, Brush brush, double thickness, decimal minBalance, double range, string label)
    {
        double width = ActualWidth;
        double height = ActualHeight;
        
        var points = new PointCollection();
        for (int i = 0; i < items.Count; i++)
        {
            double x = (double)i / (items.Count - 1) * width;
            double val = valueSelector(items[i]);
            double y = height - ((val - (double)minBalance) / range * height);
            points.Add(new Point(x, y));
        }

        var polyline = new Polyline
        {
            Points = points,
            Stroke = brush,
            StrokeThickness = thickness,
            ToolTip = label
        };
        _canvas.Children.Add(polyline);
    }
}