using SJMirror.App.ViewModels;

namespace SJMirror.Tests;

public sealed class BootstrapTests
{
    [Fact]
    public void MainViewModelExposesApplicationName()
    {
        var viewModel = new MainViewModel();

        Assert.Equal("SJ Mirror", viewModel.ApplicationName);
    }
}
