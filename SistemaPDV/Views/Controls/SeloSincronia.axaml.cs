using Avalonia;
using Avalonia.Controls;
using SistemaPDV.Models;

namespace SistemaPDV.Views.Controls;

public partial class SeloSincronia : UserControl
{
    public static readonly StyledProperty<SyncStatus> StatusProperty =
        AvaloniaProperty.Register<SeloSincronia, SyncStatus>(nameof(Status));

    public SyncStatus Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public SeloSincronia()
    {
        InitializeComponent();
    }
}
