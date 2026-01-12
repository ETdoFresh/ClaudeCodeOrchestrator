using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ClaudeCodeOrchestrator.App.Converters;

/// <summary>
/// Converts a boolean value to one of two string values.
/// </summary>
public class BoolToStringConverter : IValueConverter
{
    /// <summary>
    /// The string to return when the value is true.
    /// </summary>
    public string TrueValue { get; set; } = "True";

    /// <summary>
    /// The string to return when the value is false.
    /// </summary>
    public string FalseValue { get; set; } = "False";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? TrueValue : FalseValue;
        }
        return FalseValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
