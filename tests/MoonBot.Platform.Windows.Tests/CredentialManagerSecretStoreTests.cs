using System.Runtime.Versioning;
using MoonBot.Platform.Windows;

namespace MoonBot.Platform.Windows.Tests;

// Test de integración: usa el Credential Manager real de Windows.
// Cada test usa un prefijo único y borra lo que crea, para no dejar basura.
[SupportedOSPlatform("windows")]
public sealed class CredentialManagerSecretStoreTests : IDisposable
{
    private readonly CredentialManagerSecretStore _store = new("MoonBot.Tests." + Guid.NewGuid().ToString("N"));

    public void Dispose() => _store.Delete("key");

    [Fact]
    public void SaveThenRead_ReturnsTheSameSecret()
    {
        _store.Save("key", "sk-ñandú-🌙-123");

        Assert.Equal("sk-ñandú-🌙-123", _store.Read("key"));
    }

    [Fact]
    public void Read_Missing_ReturnsNull()
    {
        Assert.Null(_store.Read("key"));
    }

    [Fact]
    public void Save_Twice_Replaces()
    {
        _store.Save("key", "old");
        _store.Save("key", "new");

        Assert.Equal("new", _store.Read("key"));
    }

    [Fact]
    public void Delete_RemovesTheSecret_AndIsSafeToRepeat()
    {
        _store.Save("key", "value");

        _store.Delete("key");
        _store.Delete("key");

        Assert.Null(_store.Read("key"));
    }
}
