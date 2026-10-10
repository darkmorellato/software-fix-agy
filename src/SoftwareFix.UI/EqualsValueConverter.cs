using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SoftwareFix.UI;

/// <summary>
/// Compara o valor de um binding com o ConverterParameter (comparação textual).
/// Usado para marcar o item ativo da sidebar de categorias do My Device:
/// ex. Classes.Active="{Binding SelectedMyDeviceCategory,
///                     Converter={StaticResource EqualsConverter}, ConverterParameter=3}"
/// </summary>
public class EqualsValueConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString() == parameter?.ToString();

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
