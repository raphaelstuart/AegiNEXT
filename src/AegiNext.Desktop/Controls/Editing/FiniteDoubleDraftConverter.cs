using System.Globalization;
using Avalonia.Data.Converters;

namespace AegiNext.Desktop.Controls;

/// <summary>为双精度草稿保留权威文本，decimal 值只用于数值控件的显示投影。</summary>
internal sealed class FiniteDoubleDraftConverter(NumericDraftInput input) : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string;
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        if (!double.TryParse(text, NumberStyles.Float, input.NumberFormat ?? culture.NumberFormat, out var number) ||
            !double.IsFinite(number) || number < (double)input.Minimum)
        {
            throw new InvalidDataException("Invalid finite numeric draft.");
        }
        if (decimal.TryParse(text, NumberStyles.Float, input.NumberFormat ?? culture.NumberFormat, out var projection) &&
            (projection != 0 || number == 0))
        {
            return projection;
        }
        return input.Value;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = input.RawText;
        if (double.TryParse(text, NumberStyles.Float, input.NumberFormat ?? culture.NumberFormat, out var number) &&
            double.IsFinite(number) && number >= (double)input.Minimum)
        {
            return text;
        }
        return value is decimal projection ? projection.ToString(culture) : string.Empty;
    }
}
