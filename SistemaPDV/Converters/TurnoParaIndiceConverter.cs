using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SistemaPDV.Converters;

// Turno é 1-based (1/2/3, o que a API/o banco esperam) mas ComboBox.SelectedIndex
// é 0-based — esse converter só faz essa troca de base nos dois sentidos, sem
// nenhuma lógica de negócio.
public class TurnoParaIndiceConverter : IValueConverter
{
    public static readonly TurnoParaIndiceConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int turno ? turno - 1 : 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int indice ? indice + 1 : 1;
}
