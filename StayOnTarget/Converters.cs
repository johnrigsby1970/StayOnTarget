using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace StayOnTarget;

public class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int id && id == 0)
        {
            // Hide the button (and do not reserve layout space) when Id is 0
            return Visibility.Collapsed;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // This method is used for two-way binding; a simple implementation is sufficient here.
        return DependencyProperty.UnsetValue;
    }
}

public class NegativeToRedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is decimal d)
        {
            return d < 0;
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not null)
        {
            return Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return b ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Visibility v)
        {
            return v != Visibility.Visible;
        }
        return false;
    }
}

public class AccountTypeToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        Models.AccountType? accountType = null;
        if (value is Models.AccountType t) accountType = t;
        else if (value is Models.Account account) accountType = account.Type;

        if (accountType.HasValue && parameter is string targetTypeStr)
        {
            string actualTypeStr = accountType.Value.ToString();
            if (targetTypeStr.Contains("|"))
            {
                var targets = targetTypeStr.Split('|');
                return targets.Contains(actualTypeStr) ? Visibility.Visible : Visibility.Collapsed;
            }
            return actualTypeStr == targetTypeStr ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class PaycheckFieldVisibilityConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        // values[0] should be ToAccountId (int?)
        // values[1] should be the list of Accounts

        if (values.Length < 2 || values[0] is not int toAccountId || values[1] is not System.Collections.IEnumerable accounts)
        {
            return Visibility.Collapsed;
        }

        // Find the account with the matching ToAccountId
        foreach (var item in accounts)
        {
            if (item is Models.Account account && account.Id == toAccountId)
            {
                // Check if the account type is Checking or Savings
                if (account.Type == Models.AccountType.Checking || account.Type == Models.AccountType.Savings)
                {
                    return Visibility.Visible;
                }
                break;
            }
        }

        return Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
