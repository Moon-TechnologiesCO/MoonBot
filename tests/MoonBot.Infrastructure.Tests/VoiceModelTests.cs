using System.Net;
using System.Security.Cryptography;
using MoonBot.Infrastructure.Voice;

namespace MoonBot.Infrastructure.Tests;

public sealed class VoiceModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "moonbot-models-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private sealed class BytesHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    private static VoiceModel ModelFor(byte[] content)
        => new("test", "test.bin", new Uri("https://example.com/test.bin"),
            Convert.ToHexStringLower(SHA256.HashData(content)), content.Length);

    [Fact]
    public async Task Download_WithTheRightHash_IsSaved()
    {
        var content = "pretend this is a model"u8.ToArray();
        var store = new VoiceModelStore(_dir, new HttpClient(new BytesHandler(content)));
        var model = ModelFor(content);

        await store.DownloadAsync(model, progress: null, CancellationToken.None);

        Assert.True(store.IsInstalled(model));
        Assert.Equal(content, File.ReadAllBytes(store.PathOf(model)));
    }

    [Fact]
    public async Task Download_WithAWrongHash_IsRejectedAndDeleted()
    {
        var expected = "the real model"u8.ToArray();
        var tampered = "a tampered model"u8.ToArray();
        var store = new VoiceModelStore(_dir, new HttpClient(new BytesHandler(tampered)));
        var model = ModelFor(expected) with { Size = tampered.Length };

        await Assert.ThrowsAsync<InvalidDataException>(() => store.DownloadAsync(model, null, CancellationToken.None));

        Assert.False(store.IsInstalled(model));
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task Download_ReportsProgress()
    {
        var content = new byte[200_000];
        Random.Shared.NextBytes(content);
        var store = new VoiceModelStore(_dir, new HttpClient(new BytesHandler(content)));
        var reports = new List<double>();

        await store.DownloadAsync(ModelFor(content), new Progress<double>(reports.Add), CancellationToken.None);
        await Task.Delay(100); // Progress<T> informa de forma asíncrona

        Assert.NotEmpty(reports);
        Assert.All(reports, r => Assert.InRange(r, 0, 1));
    }

    [Fact]
    public void Catalog_UsesHttpsAndPinnedHashes()
    {
        Assert.All(VoiceModels.All, m =>
        {
            Assert.Equal("https", m.Url.Scheme);
            Assert.Equal(64, m.Sha256.Length);
        });
        Assert.Equal("base", VoiceModels.All[0].Id);
    }
}
