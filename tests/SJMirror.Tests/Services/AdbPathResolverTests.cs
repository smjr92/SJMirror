using SJMirror.App.Services;

namespace SJMirror.Tests.Services;

public sealed class AdbPathResolverTests
{
    [Fact]
    public void ResolveUsesConfiguredExistingExecutable()
    {
        var temporaryFile = Path.GetTempFileName();

        try
        {
            var resolver = new AdbPathResolver(temporaryFile);

            var result = resolver.Resolve();

            Assert.True(result.IsResolved);
            Assert.Equal(Path.GetFullPath(temporaryFile), result.ExecutablePath);
            Assert.Null(result.ErrorMessage);
        }
        finally
        {
            File.Delete(temporaryFile);
        }
    }

    [Fact]
    public void ResolveReportsConfiguredMissingExecutable()
    {
        var missingPath = Path.Combine(
            Path.GetTempPath(),
            $"sjmirror-missing-adb-{Guid.NewGuid():N}.exe");
        var resolver = new AdbPathResolver(missingPath);

        var result = resolver.Resolve();

        Assert.False(result.IsResolved);
        Assert.Null(result.ExecutablePath);
        Assert.Contains(Path.GetFullPath(missingPath), result.ErrorMessage);
    }
}
