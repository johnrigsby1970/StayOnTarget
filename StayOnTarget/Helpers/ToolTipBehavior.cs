using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace StayOnTarget.Helpers;

public static class ToolTipBehavior
{
    public static readonly DependencyProperty OpenCommandProperty =
        DependencyProperty.RegisterAttached(
            "OpenCommand",
            typeof(ICommand),
            typeof(ToolTipBehavior),
            new PropertyMetadata(null, OnOpenCommandChanged));

    public static readonly DependencyProperty OpenCommandParameterProperty =
        DependencyProperty.RegisterAttached(
            "OpenCommandParameter",
            typeof(object),
            typeof(ToolTipBehavior),
            new PropertyMetadata(null));

    public static void SetOpenCommand(DependencyObject element, ICommand value) =>
        element.SetValue(OpenCommandProperty, value);

    public static ICommand GetOpenCommand(DependencyObject element) =>
        (ICommand)element.GetValue(OpenCommandProperty);

    public static void SetOpenCommandParameter(DependencyObject element, object value) =>
        element.SetValue(OpenCommandParameterProperty, value);

    public static object GetOpenCommandParameter(DependencyObject element) =>
        element.GetValue(OpenCommandParameterProperty);

    private static void OnOpenCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToolTip toolTip)
        {
            // Unsubscribe first to avoid duplicate handlers
            toolTip.Opened -= OnToolTipOpened;

            if (e.NewValue != null)
            {
                // Subscribe to the WPF ToolTip.Opened event
                toolTip.Opened += OnToolTipOpened;
            }
        }
    }

    private static void OnToolTipOpened(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject d)
        {
            ICommand command = GetOpenCommand(d);
            object parameter = GetOpenCommandParameter(d);

            if (command != null && command.CanExecute(parameter))
            {
                command.Execute(parameter);
            }
        }
    }
}