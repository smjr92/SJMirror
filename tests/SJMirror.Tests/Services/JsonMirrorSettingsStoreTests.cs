using SJMirror.App.Models;
using SJMirror.App.Services;

namespace SJMirror.Tests.Services;

public sealed class JsonMirrorSettingsStoreTests
{
    [Fact]
    public void DefaultsAreSafe()
    {
        var settings = new MirrorSettings();

        Assert.Equal(MirrorQualityProfile.Balanced, settings.QualityProfile);
        Assert.False(settings.TurnScreenOff);
        Assert.True(settings.StayAwake);
        Assert.False(settings.AutoMirror);
    }

    [Fact]
    public async Task SaveAndLoadRoundTripsSettings()
    {
        var path = CreateSettingsPath();
        var store = new JsonMirrorSettingsStore(path);
        var expected = new MirrorSettings
        {
            QualityProfile = MirrorQualityProfile.Quality,
            TurnScreenOff = true,
            StayAwake = false,
            AutoMirror = true
        };

        await store.SaveAsync(expected);
        var actual = await store.LoadAsync();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task MissingFileReturnsDefaults()
    {
        var settings = await new JsonMirrorSettingsStore(CreateSettingsPath()).LoadAsync();

        Assert.Equal(new MirrorSettings(), settings);
    }

    [Fact]
    public async Task InvalidJsonReturnsDefaults()
    {
        var path = CreateSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{ invalid json");

        var settings = await new JsonMirrorSettingsStore(path).LoadAsync();

        Assert.Equal(new MirrorSettings(), settings);
    }

    private static string CreateSettingsPath() => Path.Combine(
        Path.GetTempPath(),
        "SJMirror.Tests",
        Guid.NewGuid().ToString("N"),
        "settings.json");
}
