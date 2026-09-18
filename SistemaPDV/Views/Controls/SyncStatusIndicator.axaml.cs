using Avalonia;
using Avalonia.Controls;
using SistemaPDV.Models;

namespace SistemaPDV.Views.Controls;

public partial class SyncStatusIndicator : UserControl
{
    public static readonly StyledProperty<SyncStatus> StatusProperty =
        AvaloniaProperty.Register<SyncStatusIndicator, SyncStatus>(nameof(Status));

    public SyncStatus Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public SyncStatusIndicator()
    {
        InitializeComponent();
    }
}
