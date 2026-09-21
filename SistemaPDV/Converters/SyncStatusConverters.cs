using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using SistemaPDV.Models;
using SistemaPDV.Services.Sync;

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
        SyncStatus.Descartada => "⚫ Descartada",
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
        SyncStatus.Descartada => new SolidColorBrush(Color.Parse("#64748B")),
        _ => new SolidColorBrush(Color.Parse("#94A3B8")),
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// Texto do selo colorido de sincronização (SeloSincronia): o do protótipo, com ícone — cor nunca é a única pista.
public class SyncStatusParaSeloConverter : IValueConverter
{
    public static readonly SyncStatusParaSeloConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        SyncStatus.Sincronizado => "✓ Nuvem SoftcomShop",
        SyncStatus.PendenteSync => "⏳ Pendente de envio",
        SyncStatus.FalhaSync => "✕ Falha no envio",
        SyncStatus.Descartada => "⚫ Descartada",
        _ => string.Empty,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// Cor da linha do log da fila outbox (NivelAtividade). O texto da linha já diz o que aconteceu — a cor só ajuda a varrer.
// Mesmos valores do tema (Themes/PdvTheme.axaml): um IValueConverter não alcança o ResourceDictionary com confiança.
public class NivelAtividadeParaCorConverter : IValueConverter
{
    public static readonly NivelAtividadeParaCorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        NivelAtividade.Sucesso => new SolidColorBrush(Color.Parse("#10B981")),
        NivelAtividade.Aviso => new SolidColorBrush(Color.Parse("#F59E0B")),
        NivelAtividade.Erro => new SolidColorBrush(Color.Parse("#F43F5E")),
        _ => new SolidColorBrush(Color.Parse("#38BDF8")),
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// Indicador de conexão do header (Task 50) — como nos converters de SyncStatus, nunca só
// cor: cada estado combina ícone + texto.
public class EstadoConexaoParaTextoConverter : IValueConverter
{
    public static readonly EstadoConexaoParaTextoConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        EstadoConexao.Online => "🟢 Online",
        EstadoConexao.OnlineComFalhas => "🟠 Online, com falhas",
        EstadoConexao.Offline => "🔴 Offline",
        _ => "⚪ Conexão não verificada",
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class EstadoConexaoParaCorConverter : IValueConverter
{
    public static readonly EstadoConexaoParaCorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        EstadoConexao.Online => new SolidColorBrush(Color.Parse("#10B981")),
        EstadoConexao.OnlineComFalhas => new SolidColorBrush(Color.Parse("#F59E0B")),
        EstadoConexao.Offline => new SolidColorBrush(Color.Parse("#F43F5E")),
        _ => new SolidColorBrush(Color.Parse("#94A3B8")),
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// Rótulo da pílula de conexão da barra do topo (sem emoji: a bolinha colorida vem de EstadoConexaoParaCorConverter e o
// texto diz o estado — nunca só cor). "Offline" explica o que isso significa num PDV offline-first: as vendas seguem.
public class EstadoConexaoParaRotuloConverter : IValueConverter
{
    public static readonly EstadoConexaoParaRotuloConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        EstadoConexao.Online => "Online",
        EstadoConexao.OnlineComFalhas => "Online (com falhas)",
        EstadoConexao.Offline => "Offline (contingência ativa)",
        _ => "Conexão não verificada",
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
