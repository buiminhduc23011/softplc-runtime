using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SoftPlc.AgvSimulator.ViewModels;

namespace SoftPlc.AgvSimulator.Views;

public partial class MainWindow : Window
{
    private readonly AgvViewModel _vm;
    private readonly DispatcherTimer _timer;

    public MainWindow()
    {
        InitializeComponent();

        _vm = new AgvViewModel(App.Engine.Memory);
        DataContext = _vm;

        // Update S7 status text based on whether S7 layer is present
        _vm.S7Status = App.S7 is not null ? $"port {App.S7.Port}" : "unavailable";

        // Refresh UI at 4 Hz (250ms)
        _timer = new DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(250) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnTick(object? sender, System.EventArgs e)
    {
        _vm.Refresh();

        // Update diagnostics
        var eng = App.Engine;
        _vm.CpuState    = eng.CpuState.ToString();
        _vm.ScanCycle   = eng.ScanCycleCount;
        _vm.LastScanUs  = eng.LastScanTimeUs;
        _vm.OverrunCount = eng.OverrunCount;
    }

    protected override void OnClosed(System.EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }
}
