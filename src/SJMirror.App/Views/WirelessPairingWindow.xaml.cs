using System.Windows;
using SJMirror.App.ViewModels;

namespace SJMirror.App.Views;

public partial class WirelessPairingWindow : Window
{
    public WirelessPairingWindow()
    {
        InitializeComponent();
    }

    private async void WirelessPairingWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is WirelessPairingViewModel viewModel)
        {
            await viewModel.GenerateQrCommand.ExecuteAsync();
        }
    }

    private async void WirelessPairingWindow_Closed(object? sender, EventArgs e)
    {
        if (DataContext is WirelessPairingViewModel viewModel)
        {
            await viewModel.DisposeAsync();
        }
    }

    private void PairingCodeBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is WirelessPairingViewModel viewModel)
        {
            viewModel.PairingCode = PairingCodeBox.Password;
        }
    }

    private async void PairWithCode_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is WirelessPairingViewModel viewModel)
        {
            await viewModel.PairWithCodeCommand.ExecuteAsync();
            PairingCodeBox.Clear();
        }
    }
}
