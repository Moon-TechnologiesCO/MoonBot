using System.Net;
using System.Text.Json;
using MoonBot.Core.Chat;
using MoonBot.Core.Settings;
using MoonBot.Infrastructure.Ai;

namespace MoonBot.Infrastructure.Tests;

public class ProviderTests
{
    private static readonly ChatMessage[] Conversation =
        [new(ChatRole.System, "Be kind."), new(ChatRole.User, "Hi")];

    private static async Task<string> ReadAll(IAiProvider provider)
    {
        var parts = new List<string>();
        await foreach (var chunk in provider.StreamReplyAsync(Conversation, CancellationToken.None))
            parts.Add(chunk);
        return string.Join("|", parts);
    }

    [Fact]
    public async Task Ollama_JoinsStreamedChunks_AndIgnoresThinking()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, """
            {"message":{"role":"assistant","content":"","thinking":"hmm"},"done":false}
            {"message":{"role":"assistant","content":"Hel"},"done":false}
            {"message":{"role":"assistant","content":"lo"},"done":false}
            {"message":{"role":"assistant","content":""},"done":true}
            """);
        var provider = new OllamaProvider(new HttpClient(handler), new Uri("http://localhost:11434"), "gemma3:4b");

        Assert.Equal("Hel|lo", await ReadAll(provider));
    }

    [Fact]
    public async Task Ollama_SendsModelRolesAndStreamFlag()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, """{"done":true}""");
        var provider = new OllamaProvider(new HttpClient(handler), new Uri("http://localhost:11434"), "gemma3:4b");

        await ReadAll(provider);

        Assert.Equal("http://localhost:11434/api/chat", handler.LastRequest!.RequestUri!.ToString());
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("gemma3:4b", body.RootElement.GetProperty("model").GetString());
        Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
        var messages = body.RootElement.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("Hi", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Ollama_ServerError_BecomesStatusError_WithServerMessage()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.NotFound, """{"error":"model 'nope' not found"}""");
        var provider = new OllamaProvider(new HttpClient(handler), new Uri("http://localhost:11434"), "nope");

        var error = await Assert.ThrowsAsync<AiProviderException>(() => ReadAll(provider));

        Assert.Equal(AiErrorKind.Status, error.Kind);
        Assert.Contains("model 'nope' not found", error.Detail);
        Assert.Contains("404", error.Detail);
    }

    [Fact]
    public async Task Ollama_NothingListening_BecomesConnectionError()
    {
        var provider = new OllamaProvider(new HttpClient(new RefusingHttpHandler()),
            new Uri("http://localhost:11434"), "gemma3:4b");

        var error = await Assert.ThrowsAsync<AiProviderException>(() => ReadAll(provider));

        Assert.Equal(AiErrorKind.Connection, error.Kind);
    }

    [Fact]
    public async Task Ollama_ListsInstalledModels()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK,
            """{"models":[{"name":"gemma3:4b"},{"name":"qwen3:4b"}]}""");
        var provider = new OllamaProvider(new HttpClient(handler), new Uri("http://localhost:11434"), "x");

        var models = await provider.ListModelsAsync(CancellationToken.None);

        Assert.Equal(["gemma3:4b", "qwen3:4b"], models);
    }

    [Fact]
    public async Task OpenAi_ParsesServerSentEvents()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, """
            data: {"choices":[{"delta":{"role":"assistant"}}]}

            data: {"choices":[{"delta":{"content":"Hel"}}]}

            data: {"choices":[{"delta":{"content":"lo"}}]}

            data: [DONE]

            """);
        var provider = new OpenAiCompatibleProvider(new HttpClient(handler),
            new Uri("https://api.example.com/v1"), "some-model", apiKey: "sk-test");

        Assert.Equal("Hel|lo", await ReadAll(provider));
        Assert.Equal("https://api.example.com/v1/chat/completions", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal("sk-test", handler.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task OpenAi_WithoutKey_SendsNoAuthorizationHeader()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "data: [DONE]\n");
        var provider = new OpenAiCompatibleProvider(new HttpClient(handler),
            new Uri("http://localhost:1234/v1"), "local-model", apiKey: null);

        await ReadAll(provider);

        Assert.Null(handler.LastRequest!.Headers.Authorization);
    }

    [Fact]
    public async Task OpenAi_ErrorObject_IsReported()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.Unauthorized,
            """{"error":{"message":"Invalid API key","type":"auth"}}""");
        var provider = new OpenAiCompatibleProvider(new HttpClient(handler),
            new Uri("https://api.example.com/v1"), "m", apiKey: "sk-wrong");

        var error = await Assert.ThrowsAsync<AiProviderException>(() => ReadAll(provider));

        Assert.Equal(AiErrorKind.Status, error.Kind);
        Assert.Contains("Invalid API key", error.Detail);
        Assert.DoesNotContain("sk-wrong", error.Detail); // el secreto nunca aparece en un error
    }

    [Theory]
    [InlineData("http://api.example.com/v1", false)]  // clave por HTTP a otra máquina: prohibido
    [InlineData("https://api.example.com/v1", true)]
    [InlineData("http://localhost:1234/v1", true)]   // HTTP dentro de este PC: permitido
    [InlineData("http://127.0.0.1:1234/v1", true)]
    public void Factory_OnlySendsKeysOverHttpsOrLoopback(string url, bool allowed)
    {
        var settings = new MoonSettings { Provider = ProviderKind.OpenAiCompatible, BaseUrl = url, Model = "m" };

        var create = () => AiProviderFactory.Create(settings, apiKey: "sk-test", new HttpClient());

        if (allowed)
            Assert.NotNull(create());
        else
            Assert.Equal(AiErrorKind.InsecureKey, Assert.Throws<AiProviderException>(create).Kind);
    }

    [Fact]
    public void Factory_PicksProviderBySettings()
    {
        var ollama = AiProviderFactory.Create(new MoonSettings(), null, new HttpClient());
        var openAi = AiProviderFactory.Create(new MoonSettings
        {
            Provider = ProviderKind.OpenAiCompatible, BaseUrl = "https://api.example.com/v1"
        }, null, new HttpClient());

        Assert.IsType<OllamaProvider>(ollama);
        Assert.IsType<OpenAiCompatibleProvider>(openAi);
    }
}
