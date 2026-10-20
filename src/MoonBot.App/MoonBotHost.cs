using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using MoonBot.Core;
using MoonBot.Core.Chat;
using MoonBot.Core.Diagnostics;
using MoonBot.Core.Localization;
using MoonBot.Core.Security;
using MoonBot.Core.Settings;
using MoonBot.Infrastructure.Ai;
using MoonBot.Infrastructure.Storage;
using MoonBot.Platform.Windows;

namespace MoonBot.App;

// Raíz de composición: crea las piezas una sola vez y las conecta.
// Las ventanas reciben este objeto en lugar de crear sus propias dependencias.
public sealed class MoonBotHost : IDisposable
{
    private readonly ISettingsStore _settingsStore = new JsonSettingsStore(AppPaths.SettingsFile);

    public MoonBotHost()
    {
        Log = new FileEventLog(AppPaths.LogsDirectory);
        Secrets = OperatingSystem.IsWindows() ? new CredentialManagerSecretStore() : null;
        IsFirstRun = !System.IO.File.Exists(AppPaths.SettingsFile);
        Settings = _settingsStore.Load();
        Text = Localizer.For(Settings.Language);
        Chat = new ChatSession(CreateProviderOrBroken(Settings), Machine);
        UpdateSystemPrompt();
        Log.Info($"MoonBot started. Provider={Settings.Provider} Model={Settings.Model}");
    }

    public MoonStateMachine Machine { get; } = new();
    public HttpClient Http { get; } = new() { Timeout = TimeSpan.FromMinutes(3) };
    public FileEventLog Log { get; }
    public ISecretStore? Secrets { get; }
    public ChatSession Chat { get; }
    public MoonSettings Settings { get; private set; }
    public Localizer Text { get; private set; }
    public bool IsFirstRun { get; }

    // Avisa a las ventanas para que se actualicen (idioma, nombre, proveedor).
    public event EventHandler? SettingsChanged;

    public bool HasApiKey => !string.IsNullOrEmpty(Secrets?.Read(SecretNames.ProviderApiKey));

    // Crea un proveedor con unos ajustes y una clave candidatos, sin guardar nada.
    // Lanza AiProviderException si la combinación es insegura.
    public IAiProvider CreateProvider(MoonSettings settings, string? candidateKey = null)
    {
        var key = settings.Provider == ProviderKind.OpenAiCompatible
            ? candidateKey ?? Secrets?.Read(SecretNames.ProviderApiKey)
            : null;
        return AiProviderFactory.Create(settings, key, Http);
    }

    public void Apply(MoonSettings settings, string? newApiKey)
    {
        var provider = CreateProvider(settings, newApiKey); // primero validar, luego guardar

        if (!string.IsNullOrEmpty(newApiKey))
            Secrets?.Save(SecretNames.ProviderApiKey, newApiKey);

        _settingsStore.Save(settings);
        Settings = settings;
        Text = Localizer.For(settings.Language);
        Chat.UseProvider(provider);
        UpdateSystemPrompt();
        Log.Info($"Settings saved. Provider={settings.Provider} Model={settings.Model} Language={settings.Language}");
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteApiKey()
    {
        Secrets?.Delete(SecretNames.ProviderApiKey);
        Log.Info("API key removed.");
    }

    // Convierte un error técnico en una frase para el usuario, en su idioma.
    public string Describe(Exception error) => error switch
    {
        AiProviderException { Kind: AiErrorKind.Connection } e => Text.Format("error.connection", e.Detail),
        AiProviderException { Kind: AiErrorKind.Status } e => Text.Format("error.status", e.Detail.Split(' ', 2)[0],
            e.Detail.Split(' ', 2) is [_, var rest] ? rest : ""),
        AiProviderException { Kind: AiErrorKind.InsecureKey } => Text["error.insecureKey"],
        AiProviderException { Kind: AiErrorKind.Timeout } => Text["error.timeout"],
        ArgumentException => Text["settings.invalidUrl"],
        _ => error.Message
    };

    private IAiProvider CreateProviderOrBroken(MoonSettings settings)
    {
        try
        {
            return CreateProvider(settings);
        }
        catch (Exception e) when (e is AiProviderException or ArgumentException)
        {
            Log.Error("Could not create the AI provider from saved settings", e);
            return new BrokenProvider(e);
        }
    }

    private void UpdateSystemPrompt()
    {
        var prompt = Text.Format("ai.systemPrompt", Text.Language.NativeName);
        if (!string.IsNullOrWhiteSpace(Settings.UserName))
            prompt += $" (User name: {Settings.UserName.Trim()})";
        Chat.SystemPrompt = prompt;
    }

    public void Dispose()
    {
        Log.Info("MoonBot closed.");
        Http.Dispose();
    }

    // Proveedor que explica por qué no se pudo crear el real, en lugar de tumbar la app.
    private sealed class BrokenProvider(Exception reason) : IAiProvider
    {
        public async IAsyncEnumerable<AiEvent> StreamAsync(IReadOnlyList<ChatMessage> conversation,
            IReadOnlyList<ToolDefinition> tools, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await System.Threading.Tasks.Task.Yield();
            throw reason;
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }

        public System.Threading.Tasks.Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken)
            => throw reason;
    }
}
