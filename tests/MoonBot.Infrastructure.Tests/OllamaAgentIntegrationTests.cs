using MoonBot.Core;
using MoonBot.Core.Chat;
using MoonBot.Core.Diagnostics;
using MoonBot.Core.Tools;
using MoonBot.Infrastructure.Ai;
using MoonBot.Infrastructure.Tools;

namespace MoonBot.Infrastructure.Tests;

// Solo corre si MOONBOT_OLLAMA_IT=1 y hay un Ollama local con el modelo indicado.
// Así la CI no depende de un servidor de IA, pero en tu PC puedes comprobar el agente de verdad.
public sealed class OllamaFactAttribute : FactAttribute
{
    public OllamaFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("MOONBOT_OLLAMA_IT") != "1")
            Skip = "Set MOONBOT_OLLAMA_IT=1 to run against a local Ollama.";
    }
}

public sealed class OllamaAgentIntegrationTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "moonbot-it-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_home))
            Directory.Delete(_home, recursive: true);
    }

    private sealed class RecordingApprovals(bool answer) : IApprovalGate
    {
        public List<ToolPreview> Asked { get; } = [];

        public Task<bool> RequestAsync(ToolCall call, ToolPreview preview, CancellationToken cancellationToken)
        {
            Asked.Add(preview);
            return Task.FromResult(answer);
        }
    }

    private sealed class ListLog : IEventLog
    {
        public List<string> Lines { get; } = [];
        public void Info(string message) => Lines.Add(message);
        public void Error(string message, Exception? exception = null) => Lines.Add(message + " " + exception?.Message);
    }

    private (ChatSession Session, RecordingApprovals Approvals, ListLog Log, string Docs) Build(bool approve)
    {
        var docs = Directory.CreateDirectory(Path.Combine(_home, "Documents")).FullName;
        File.WriteAllText(Path.Combine(docs, "presupuesto-luna-2026.txt"), "x");
        var sandbox = new FileSandbox(new Dictionary<string, string> { ["documents"] = docs });
        var approvals = new RecordingApprovals(approve);
        var log = new ListLog();
        var model = Environment.GetEnvironmentVariable("MOONBOT_OLLAMA_MODEL") ?? "nemotron-3-nano:4b";
        var provider = new OllamaProvider(new HttpClient { Timeout = TimeSpan.FromMinutes(10) },
            new Uri("http://localhost:11434"), model);
        var router = new ToolRouter(FileTools.All(sandbox), approvals, log);
        var session = new ChatSession(provider, new MoonStateMachine(), router)
        {
            SystemPrompt = $"You are MoonBot. You can use tools on the user's files. documents = {docs}. " +
                           "Never invent paths. The app asks the user to approve changes; call the tools directly."
        };
        return (session, approvals, log, docs);
    }

    [OllamaFact]
    public async Task RealModel_SearchesWithTheTool()
    {
        var (session, _, log, _) = Build(approve: true);
        var tools = new List<string>();

        var answer = await session.SendAsync("Find the file whose name contains 'presupuesto' in documents.",
            new ChatObserver { OnToolStarted = c => tools.Add(c.Name) }, CancellationToken.None);

        Assert.Contains("search_files", tools);
        Assert.Contains("presupuesto", answer, StringComparison.OrdinalIgnoreCase);
    }

    [OllamaFact]
    public async Task RealModel_CreatesAFolder_OnlyAfterApproval()
    {
        var (session, approvals, log, docs) = Build(approve: true);
        var calls = new List<string>();

        var answer = await session.SendAsync("Create a folder called Facturas inside documents.",
            new ChatObserver { OnToolStarted = c => calls.Add($"{c.Name} {c.ArgumentsJson}") }, CancellationToken.None);

        var trace = $"calls: [{string.Join(" | ", calls)}]\nlog: [{string.Join(" | ", log.Lines)}]\nanswer: {answer}";
        Assert.True(approvals.Asked.Count > 0, trace);
        Assert.True(Directory.Exists(Path.Combine(docs, "Facturas")), trace);
    }

    [OllamaFact]
    public async Task RealModel_DeniedChange_LeavesTheDiskUntouched()
    {
        var (session, approvals, _, docs) = Build(approve: false);

        await session.SendAsync("Create a folder called Facturas inside documents.", null, CancellationToken.None);

        Assert.NotEmpty(approvals.Asked);
        Assert.False(Directory.Exists(Path.Combine(docs, "Facturas")));
    }
}
