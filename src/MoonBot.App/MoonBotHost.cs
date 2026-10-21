using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MoonBot.Core;
using MoonBot.Core.Chat;
using MoonBot.Core.Localization;
using MoonBot.Core.Security;
using MoonBot.Core.Settings;
using MoonBot.Core.Tools;
using MoonBot.Infrastructure.Ai;
using MoonBot.Infrastructure.Storage;
using MoonBot.Infrastructure.Tools;
using MoonBot.Infrastructure.Voice;
using MoonBot.Core.Voice;
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

        Sandbox = FileSandbox.ForCurrentUser();
        Tools = new ToolRouter(FileTools.All(Sandbox), Approvals, Log) { Enabled = Settings.ToolsEnabled };
        Chat = new ChatSession(CreateProviderOrBroken(Settings), Machine, Tools);
        UpdateSystemPrompt();
        Log.Info($"MoonBot started. Provider={Settings.Provider} Model={Settings.Model} Tools={Settings.ToolsEnabled}");
    }

    public MoonStateMachine Machine { get; } = new();
    public HttpClient Http { get; } = new() { Timeout = TimeSpan.FromMinutes(5) };
    public FileEventLog Log { get; }
    public ISecretStore? Secrets { get; }
    public FileSandbox Sandbox { get; }
    public UiApprovalGate Approvals { get; } = new();
    public ToolRouter Tools { get; }
    public ChatSession Chat { get; }
    public VoiceModelStore VoiceModels { get; } =
        new(AppPaths.ModelsDirectory, new HttpClient { Timeout = TimeSpan.FromMinutes(30) });
    public MoonSettings Settings { get; private set; }
    public Localizer Text { get; private set; }
    public bool IsFirstRun { get; }

    // Avisa a las ventanas para que se actualicen (idioma, skin, proveedor, posición).
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

    public void Apply(MoonSettings settings, string? newApiKey = null)
    {
        // Nada cambió: no se guarda ni se avisa. Corta de raíz cualquier bucle
        // "guardar -> avisar -> refrescar -> guardar lo mismo".
        if (settings == Settings && string.IsNullOrEmpty(newApiKey))
            return;

        var providerChanged = settings.Provider != Settings.Provider || settings.BaseUrl != Settings.BaseUrl
                              || settings.Model != Settings.Model || !string.IsNullOrEmpty(newApiKey);
        var provider = providerChanged ? CreateProvider(settings, newApiKey) : null; // validar antes de guardar

        if (!string.IsNullOrEmpty(newApiKey))
            Secrets?.Save(SecretNames.ProviderApiKey, newApiKey);

        _settingsStore.Save(settings);
        Settings = settings;
        Text = Localizer.For(settings.Language);
        Tools.Enabled = settings.ToolsEnabled;
        if (provider is not null)
            Chat.UseProvider(provider);
        UpdateSystemPrompt();
        Log.Info($"Settings saved. Provider={settings.Provider} Model={settings.Model} Language={settings.Language} " +
                 $"Skin={settings.Skin} Placement={settings.Placement} Tools={settings.ToolsEnabled}");
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    // Cambios pequeños (skin, posición) sin tocar el proveedor.
    public void Update(Func<MoonSettings, MoonSettings> change) => Apply(change(Settings));

    // Motor de voz del modelo elegido, o null si ese modelo aún no está descargado.
    // Se crea una sola vez por modelo: cargarlo cuesta cientos de MB de memoria.
    private WhisperSpeechToText? _speechToText;
    private string? _speechModelId;

    public ISpeechToText? SpeechToText
    {
        get
        {
            var model = Infrastructure.Voice.VoiceModels.Get(Settings.VoiceModel);
            if (!VoiceModels.IsInstalled(model))
                return null;
            if (_speechModelId != model.Id)
            {
                _speechToText?.Dispose();
                _speechToText = new WhisperSpeechToText(VoiceModels.PathOf(model));
                _speechModelId = model.Id;
            }
            return _speechToText;
        }
    }

    public void DeleteApiKey()
    {
        Secrets?.Delete(SecretNames.ProviderApiKey);
        Log.Info("API key removed.");
    }

    // Búsqueda instantánea para el comando "/find", sin pasar por la IA.
    public Task<List<string>> QuickSearchAsync(string query, CancellationToken cancellationToken)
        => Task.Run(() => new SearchFilesTool(Sandbox).Search(Sandbox.Roots.Values, query, 20, cancellationToken),
            cancellationToken);

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

        if (Settings.ToolsEnabled)
        {
            prompt += "\n\nYou can work with the user's files using tools. Folders you can reach: " +
                      string.Join(", ", Sandbox.Roots.Select(r => $"{r.Key} = {r.Value}")) + ". " +
                      "Rules: search or list before acting and never invent paths; to organize, call create_folder " +
                      "and then move_items; the app asks the user to approve every change, so call the tool directly " +
                      "instead of asking for permission in text; if the user denies, accept it. " +
                      "When you mention files, write their names, not long paths. You cannot delete files.";
        }

        Chat.SystemPrompt = prompt;
    }

    public void Dispose()
    {
        Log.Info("MoonBot closed.");
        _speechToText?.Dispose();
        Http.Dispose();
    }

    // Proveedor que explica por qué no se pudo crear el real, en lugar de tumbar la app.
    private sealed class BrokenProvider(Exception reason) : IAiProvider
    {
        public async IAsyncEnumerable<AiEvent> StreamAsync(IReadOnlyList<ChatMessage> conversation,
            IReadOnlyList<ToolDefinition> tools, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw reason;
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }

        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken) => throw reason;
    }
}

// Puerta de permisos de la interfaz. La ventana de chat registra el manejador que muestra la tarjeta.
// Si no hay nadie que pueda preguntar, la respuesta es "no" (fallar cerrado).
public sealed class UiApprovalGate : IApprovalGate
{
    public static readonly TimeSpan WaitLimit = TimeSpan.FromMinutes(3);

    public Func<ToolCall, ToolPreview, CancellationToken, Task<bool>>? Handler { get; set; }

    public async Task<bool> RequestAsync(ToolCall call, ToolPreview preview, CancellationToken cancellationToken)
    {
        if (Handler is null)
            return false;

        // Sin respuesta en 3 minutos = denegado.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(WaitLimit);
        try
        {
            return await Handler(call, preview, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
