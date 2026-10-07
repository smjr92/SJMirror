using System.Windows;
using SJMirror.App.Services;
using SJMirror.App.ViewModels;

namespace SJMirror.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(
            new AdbService(),
            new ScrcpyMirrorService(),
            new WirelessAdbService());
        DataContext = _viewModel;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    private async void MainWindow_Closed(object? sender, EventArgs e)
    {
        await _viewModel.DisposeAsync();
    }

    private void ConnectWirelessly_Click(object sender, RoutedEventArgs e)
    {
        var window = new WirelessPairingWindow
        {
            Owner = this,
            DataContext = _viewModel.CreateWirelessPairingViewModel()
        };
        window.ShowDialog();
    }
}
