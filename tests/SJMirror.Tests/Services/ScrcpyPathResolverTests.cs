using SJMirror.App.Services;

namespace SJMirror.Tests.Services;

public sealed class ScrcpyPathResolverTests
{
    [Fact]
    public void ResolveUsesExplicitExistingPath()
    {
        var temporaryFile = Path.GetTempFileName();

        try
        {
            var result = new ScrcpyPathResolver(temporaryFile).Resolve();

            Assert.True(result.IsResolved);
            Assert.Equal(Path.GetFullPath(temporaryFile), result.ExecutablePath);
        }
        finally
        {
            File.Delete(temporaryFile);
        }
    }

    [Fact]
    public void ResolveReportsMissingExplicitPath()
    {
        var missingPath = Path.Combine(
            Path.GetTempPath(),
            $"sjmirror-missing-scrcpy-{Guid.NewGuid():N}.exe");

        var result = new ScrcpyPathResolver(missingPath).Resolve();

        Assert.False(result.IsResolved);
        Assert.Contains(Path.GetFullPath(missingPath), result.ErrorMessage);
    }
}
