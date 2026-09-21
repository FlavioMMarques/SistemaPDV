using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SistemaPDV.Views.Controls;

public partial class CartaoIndicador : UserControl
{
    public static readonly StyledProperty<string?> TituloProperty = AvaloniaProperty.Register<CartaoIndicador, string?>(nameof(Titulo));
    public static readonly StyledProperty<string?> ValorProperty = AvaloniaProperty.Register<CartaoIndicador, string?>(nameof(Valor));
    public static readonly StyledProperty<string?> DetalheProperty = AvaloniaProperty.Register<CartaoIndicador, string?>(nameof(Detalhe));
    public static readonly StyledProperty<IBrush?> CorFaixaProperty = AvaloniaProperty.Register<CartaoIndicador, IBrush?>(nameof(CorFaixa));
    public static readonly StyledProperty<IBrush?> CorValorProperty = AvaloniaProperty.Register<CartaoIndicador, IBrush?>(nameof(CorValor));

    public string? Titulo { get => GetValue(TituloProperty); set => SetValue(TituloProperty, value); }
    public string? Valor { get => GetValue(ValorProperty); set => SetValue(ValorProperty, value); }
    public string? Detalhe { get => GetValue(DetalheProperty); set => SetValue(DetalheProperty, value); }
    public IBrush? CorFaixa { get => GetValue(CorFaixaProperty); set => SetValue(CorFaixaProperty, value); }
    public IBrush? CorValor { get => GetValue(CorValorProperty); set => SetValue(CorValorProperty, value); }

    public CartaoIndicador()
    {
        InitializeComponent();
    }
}
