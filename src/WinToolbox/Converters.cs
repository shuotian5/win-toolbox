using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WinToolbox;

/// <summary>Tiny converters so the card template stays free of code-behind.</summary>
internal sealed class NullOrEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool has = value is string s ? !string.IsNullOrWhiteSpace(s) : value is not null;
        if (parameter as string == "invert") has = !has;
        return has ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Shows the switch for toggle cards and the button for action cards.</summary>
internal sealed class KindToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string wanted = parameter as string ?? "toggle";
        string actual = value is Core.TweakKind.Action ? "action" : "toggle";
        return actual == wanted ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
