using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using SistemaPDV.Models;

namespace SistemaPDV.Converters;

// Nunca só cor pra indicar SyncStatus (ver specs/SPEC-pdv-ui.md, "Convenções de
// UI") — por isso são dois converters, sempre usados juntos pelo
// SyncStatusIndicator: um pro texto+ícone, outro só pra cor de apoio.
public class SyncStatusParaTextoConverter : IValueConverter
{
    public static readonly SyncStatusParaTextoConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        SyncStatus.Sincronizado => "🟢 Sincronizado",
        SyncStatus.PendenteSync => "🟡 Pendente",
        SyncStatus.FalhaSync => "🔴 Falha",
        _ => string.Empty,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class SyncStatusParaCorConverter : IValueConverter
{
    public static readonly SyncStatusParaCorConverter Instance = new();

    // Mesmos valores hex do tema (Themes/PdvTheme.axaml) — hardcoded aqui porque um
    // IValueConverter não tem acesso direto e confiável ao ResourceDictionary na
    // hora da conversão.
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        SyncStatus.Sincronizado => new SolidColorBrush(Color.Parse("#10B981")),
        SyncStatus.PendenteSync => new SolidColorBrush(Color.Parse("#FED400")),
        SyncStatus.FalhaSync => new SolidColorBrush(Color.Parse("#F43F5E")),
        _ => new SolidColorBrush(Color.Parse("#94A3B8")),
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
