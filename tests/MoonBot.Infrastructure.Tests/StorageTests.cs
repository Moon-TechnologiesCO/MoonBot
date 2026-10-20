using MoonBot.Core.Settings;
using MoonBot.Infrastructure.Storage;

namespace MoonBot.Infrastructure.Tests;

public sealed class StorageTests : IDisposable
{
    // Cada test usa su propia carpeta temporal, así no tocan los ajustes reales del usuario.
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "moonbot-tests-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Settings_MissingFile_ReturnsDefaults()
    {
        var store = new JsonSettingsStore(Path.Combine(_dir, "settings.json"));

        Assert.Equal(new MoonSettings(), store.Load());
    }

    [Fact]
    public void Settings_SaveThenLoad_RoundTrips()
    {
        var store = new JsonSettingsStore(Path.Combine(_dir, "settings.json"));
        var settings = new MoonSettings
        {
            Language = "es", UserName = "JD", Provider = ProviderKind.OpenAiCompatible,
            BaseUrl = "https://api.example.com/v1", Model = "m", SleepAfterMinutes = 5
        };

        store.Save(settings);

        Assert.Equal(settings, new JsonSettingsStore(Path.Combine(_dir, "settings.json")).Load());
    }

    [Fact]
    public void Settings_CorruptFile_ReturnsDefaultsInsteadOfCrashing()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ this is not json");

        Assert.Equal(new MoonSettings(), new JsonSettingsStore(path).Load());
    }

    [Fact]
    public void Settings_StoresProviderAsReadableText()
    {
        var path = Path.Combine(_dir, "settings.json");
        new JsonSettingsStore(path).Save(new MoonSettings { Provider = ProviderKind.OpenAiCompatible });

        Assert.Contains("\"OpenAiCompatible\"", File.ReadAllText(path));
    }

    [Fact]
    public void EventLog_AppendsTimestampedLines()
    {
        var log = new FileEventLog(_dir);

        log.Info("started");
        log.Error("failed", new InvalidOperationException("boom"));

        var text = File.ReadAllText(Directory.GetFiles(_dir, "moonbot-*.log").Single());
        Assert.Contains("INFO  started", text);
        Assert.Contains("ERROR failed", text);
        Assert.Contains("InvalidOperationException: boom", text);
    }
}
