using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MoonBot.Core;
using MoonBot.Core.Chat;
using MoonBot.Core.Localization;
using MoonBot.Core.Settings;
using MoonBot.Infrastructure.Ai;
using MoonBot.Infrastructure.Storage;

namespace MoonBot.App;

public enum PanelPage { Chat, Settings, About }

// La "app" de MoonBot: chat, ajustes e información.
public partial class PanelWindow : Window
{
    private static readonly IBrush OkBrush = new SolidColorBrush(Color.Parse("#7FE0A0"));
    private static readonly IBrush WarnBrush = new SolidColorBrush(Color.Parse("#F2D98D"));
    private static readonly IBrush BadBrush = new SolidColorBrush(Color.Parse("#FF8A8A"));
    private static readonly IBrush BlueBrush = new SolidColorBrush(Color.Parse("#8FB3FF"));
    private static readonly IBrush SleepBrush = new SolidColorBrush(Color.Parse("#6B739A"));

    private readonly MoonBotHost _host;
    private readonly ObservableCollection<ChatItem> _items = [];
    private CancellationTokenSource? _replyCts;
    private bool _loadingForm;

    // Solo para el diseñador de Avalonia.
    public PanelWindow() : this(new MoonBotHost()) { }

    public PanelWindow(MoonBotHost host)
    {
        _host = host;
        InitializeComponent();

        Messages.ItemsSource = _items;
        _items.CollectionChanged += (_, _) => EmptyState.IsVisible = _items.Count == 0;

        NavChat.Click += (_, _) => ShowPage(PanelPage.Chat);
        NavSettings.Click += (_, _) => ShowPage(PanelPage.Settings);
        NavAbout.Click += (_, _) => ShowPage(PanelPage.About);

        SendButton.Click += async (_, _) => await SendAsync(Input.Text);
        StopButton.Click += (_, _) => _replyCts?.Cancel();
        NewChatButton.Click += (_, _) => NewChat();
        Suggestion1.Click += async (_, _) => await SendAsync(_host.Text["chat.suggestion1"]);
        Suggestion2.Click += async (_, _) => await SendAsync(_host.Text["chat.suggestion2"]);
        Suggestion3.Click += async (_, _) => await SendAsync(_host.Text["chat.suggestion3"]);

        // Enter envía; Shift+Enter hace salto de línea. Se intercepta en la fase "túnel",
        // antes de que el TextBox convierta el Enter en un salto de línea.
        Input.AddHandler(KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);

        ProviderBox.SelectionChanged += (_, _) => OnProviderChanged();
        RefreshModelsButton.Click += async (_, _) => await TestConnectionAsync(fillModels: true);
        TestButton.Click += async (_, _) => await TestConnectionAsync(fillModels: true);
        RemoveKeyButton.Click += (_, _) => { _host.DeleteApiKey(); UpdateKeyStatus(); };
        SaveButton.Click += (_, _) => Save();

        OpenLogsButton.Click += async (_, _) => await OpenFolderAsync(AppPaths.LogsDirectory);
        OpenDataButton.Click += async (_, _) => await OpenFolderAsync(AppPaths.SettingsDirectory);

        _host.Machine.StateChanged += (_, e) => Dispatcher.UIThread.Post(() => ShowState(e.To));
        _host.SettingsChanged += (_, _) => ApplyTexts();

        // Cerrar la ventana solo la oculta: MoonBot sigue en la bandeja.
        Closing += (_, e) =>
        {
            if (e.CloseReason is WindowCloseReason.WindowClosing)
            {
                e.Cancel = true;
                Hide();
            }
        };

        ApplyTexts();
        LoadForm();
        ShowState(_host.Machine.Current);
        ShowPage(PanelPage.Chat);
    }

    public void ShowPage(PanelPage page)
    {
        ChatPage.IsVisible = page == PanelPage.Chat;
        SettingsPage.IsVisible = page == PanelPage.Settings;
        AboutPage.IsVisible = page == PanelPage.About;

        NavChat.Classes.Set("selected", page == PanelPage.Chat);
        NavSettings.Classes.Set("selected", page == PanelPage.Settings);
        NavAbout.Classes.Set("selected", page == PanelPage.About);

        if (page == PanelPage.Settings)
            LoadForm();
        if (page == PanelPage.Chat)
            Dispatcher.UIThread.Post(() => Input.Focus());
    }

    public void Open(PanelPage page)
    {
        ShowPage(page);
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    // ---------- Textos (idioma) ----------

    private void ApplyTexts()
    {
        var t = _host.Text;
        Tagline.Text = t["app.tagline"];
        NavChatText.Text = t["nav.chat"];
        NavSettingsText.Text = t["nav.settings"];
        NavAboutText.Text = t["nav.about"];

        ChatTitle.Text = t["chat.title"];
        ChatSubtitle.Text = $"{_host.Settings.Model} · {ProviderName(_host.Settings.Provider)}";
        NewChatText.Text = t["chat.new"];
        Input.PlaceholderText = t["chat.placeholder"];
        ToolTip.SetTip(SendButton, t["chat.send"]);
        ToolTip.SetTip(StopButton, t["chat.stop"]);
        EmptyTitle.Text = t["chat.emptyTitle"];
        EmptyBody.Text = t["chat.emptyBody"];
        Suggestion1.Content = t["chat.suggestion1"];
        Suggestion2.Content = t["chat.suggestion2"];
        Suggestion3.Content = t["chat.suggestion3"];

        SettingsTitle.Text = t["settings.title"];
        GeneralTitle.Text = t["settings.general"];
        LanguageLabel.Text = t["settings.language"];
        NameLabel.Text = t["settings.name"];
        NameBox.PlaceholderText = t["settings.namePlaceholder"];
        AiTitle.Text = t["settings.ai"];
        ProviderLabel.Text = t["settings.provider"];
        UrlLabel.Text = t["settings.url"];
        ModelLabel.Text = t["settings.model"];
        RefreshModelsButton.Content = t["settings.refresh"];
        KeyLabel.Text = t["settings.apiKey"];
        KeyBox.PlaceholderText = t["settings.apiKeyPlaceholder"];
        RemoveKeyButton.Content = t["settings.apiKeyRemove"];
        TestButton.Content = t["settings.test"];
        BehaviorTitle.Text = t["settings.behavior"];
        SleepLabel.Text = t["settings.sleepAfter"];
        SaveButton.Content = t["settings.save"];

        AboutTitle.Text = t["about.title"];
        AboutTagline.Text = t["app.tagline"];
        AboutVersion.Text = t.Format("about.version", AppVersion);
        PrivacyTitle.Text = t["about.privacyTitle"];
        PrivacyText.Text = t["about.privacy"];
        OpenLogsButton.Content = t["about.logs"];
        OpenDataButton.Content = t["about.data"];

        // Los nombres de idioma se muestran en su propio idioma: siempre reconocibles.
        var providerIndex = ProviderBox.SelectedIndex;
        ProviderBox.ItemsSource = new[] { t["settings.provider.ollama"], t["settings.provider.openai"] };
        ProviderBox.SelectedIndex = providerIndex;

        // Nombres para lectores de pantalla en botones cuyo contenido no es solo texto.
        AutomationProperties.SetName(NavChat, t["nav.chat"]);
        AutomationProperties.SetName(NavSettings, t["nav.settings"]);
        AutomationProperties.SetName(NavAbout, t["nav.about"]);
        AutomationProperties.SetName(NewChatButton, t["chat.new"]);
        AutomationProperties.SetName(SendButton, t["chat.send"]);
        AutomationProperties.SetName(StopButton, t["chat.stop"]);
        AutomationProperties.SetName(Input, t["chat.title"]);

        UpdateKeyStatus();
        ShowState(_host.Machine.Current);
    }

    private string ProviderName(ProviderKind kind)
        => kind == ProviderKind.Ollama ? "Ollama" : "OpenAI-compatible";

    private static string AppVersion
        => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    private void ShowState(MoonState state)
    {
        Logo.State = state;
        EmptyMoon.State = state;
        var t = _host.Text;
        (StatusText.Text, StatusDot.Fill) = state switch
        {
            MoonState.Thinking => (t["status.thinking"], BlueBrush),
            MoonState.Working => (t["status.working"], WarnBrush),
            MoonState.Sleeping => (t["status.sleeping"], SleepBrush),
            MoonState.Error => (t["status.error"], BadBrush),
            _ => (t["status.ready"], OkBrush)
        };
        StatusModel.Text = $"{ProviderName(_host.Settings.Provider)} · {_host.Settings.Model}";
    }

    // ---------- Chat ----------

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            _ = SendAsync(Input.Text);
        }
    }

    private async Task SendAsync(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || _host.Chat.IsBusy)
            return;

        Input.Text = "";
        _items.Add(ChatItem.User(text.Trim()));
        var reply = ChatItem.Bot(_host.Text["moon.thinking"], pending: true);
        _items.Add(reply);
        SetBusy(true);
        ScrollToEnd();

        _replyCts = new CancellationTokenSource();
        try
        {
            await _host.Chat.SendAsync(text, new ChatObserver { OnText = chunk => OnUi(() =>
            {
                if (reply.IsPending)
                {
                    reply.Text = "";
                    reply.IsPending = false;
                }
                reply.Text += chunk;
                ScrollToEnd();
            }) }, _replyCts.Token);
        }
        catch (OperationCanceledException)
        {
            if (reply.IsPending)
                _items.Remove(reply);
            else
                reply.Text += "  " + _host.Text["chat.stopped"];
        }
        catch (Exception e)
        {
            // Se registra el tipo de error, nunca el contenido de la conversación.
            _host.Log.Error("Chat request failed", e);
            reply.IsPending = false;
            reply.IsError = true;
            reply.Text = _host.Text.Format("chat.failed", _host.Describe(e));
        }
        finally
        {
            _replyCts.Dispose();
            _replyCts = null;
            SetBusy(false);
            ScrollToEnd();
            Input.Focus();
        }
    }

    private void NewChat()
    {
        _replyCts?.Cancel();
        _host.Chat.Clear();
        _items.Clear();
        Input.Focus();
    }

    private void SetBusy(bool busy)
    {
        SendButton.IsVisible = !busy;
        StopButton.IsVisible = busy;
    }

    private void ScrollToEnd() => Dispatcher.UIThread.Post(() => ChatScroll.ScrollToEnd(), DispatcherPriority.Background);

    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    // ---------- Ajustes ----------

    private void LoadForm()
    {
        _loadingForm = true;
        var s = _host.Settings;

        LanguageBox.ItemsSource = Localizer.Languages.Select(l => l.NativeName).ToList();
        LanguageBox.SelectedIndex = Math.Max(0, Localizer.Languages.ToList().FindIndex(l => l.Code == s.Language));
        NameBox.Text = s.UserName;
        ProviderBox.SelectedIndex = s.Provider == ProviderKind.OpenAiCompatible ? 1 : 0;
        UrlBox.Text = s.BaseUrl;
        ModelBox.Text = s.Model;
        KeyBox.Text = "";
        SleepBox.Value = s.SleepAfterMinutes;
        TestResult.Text = "";
        SaveStatus.Text = "";
        KeySection.IsVisible = s.Provider == ProviderKind.OpenAiCompatible;
        UpdateKeyStatus();

        _loadingForm = false;
    }

    private void OnProviderChanged()
    {
        if (_loadingForm || ProviderBox.SelectedIndex < 0)
            return;

        var kind = SelectedProvider;
        KeySection.IsVisible = kind == ProviderKind.OpenAiCompatible;

        // Si la URL era la predeterminada del otro proveedor, se cambia por la de este.
        var url = UrlBox.Text?.Trim() ?? "";
        if (url.Length == 0 || AiProviderFactory.DefaultUrls.Values.Contains(url))
            UrlBox.Text = AiProviderFactory.DefaultUrls[kind];
    }

    private ProviderKind SelectedProvider
        => ProviderBox.SelectedIndex == 1 ? ProviderKind.OpenAiCompatible : ProviderKind.Ollama;

    private MoonSettings ReadForm() => new()
    {
        Language = Localizer.Languages[Math.Max(0, LanguageBox.SelectedIndex)].Code,
        UserName = NameBox.Text?.Trim() ?? "",
        Provider = SelectedProvider,
        BaseUrl = UrlBox.Text?.Trim() ?? "",
        Model = ModelBox.Text?.Trim() ?? "",
        SleepAfterMinutes = (int)(SleepBox.Value ?? 2)
    };

    private string? NewKey => string.IsNullOrWhiteSpace(KeyBox.Text) ? null : KeyBox.Text.Trim();

    private void UpdateKeyStatus()
    {
        var hasKey = _host.HasApiKey;
        KeyStatus.Text = _host.Text[hasKey ? "settings.apiKeySaved" : "settings.apiKeyNone"];
        RemoveKeyButton.IsVisible = hasKey;
    }

    private async Task TestConnectionAsync(bool fillModels)
    {
        TestResult.Foreground = (IBrush)this.FindResource("MutedBrush")!;
        TestResult.Text = _host.Text["settings.testing"];
        TestButton.IsEnabled = RefreshModelsButton.IsEnabled = false;
        try
        {
            var provider = _host.CreateProvider(ReadForm(), NewKey);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var models = await provider.ListModelsAsync(timeout.Token);

            TestResult.Foreground = OkBrush;
            TestResult.Text = _host.Text.Format("settings.testOk", models.Count);
            if (fillModels)
            {
                ModelBox.ItemsSource = models;
                if (string.IsNullOrWhiteSpace(ModelBox.Text) && models.Count > 0)
                    ModelBox.Text = models[0];
            }
        }
        catch (Exception e)
        {
            TestResult.Foreground = BadBrush;
            TestResult.Text = _host.Text.Format("settings.testFail", _host.Describe(e));
        }
        finally
        {
            TestButton.IsEnabled = RefreshModelsButton.IsEnabled = true;
        }
    }

    private void Save()
    {
        try
        {
            _host.Apply(ReadForm(), NewKey);
            KeyBox.Text = "";
            UpdateKeyStatus();
            SaveStatus.Foreground = OkBrush;
            SaveStatus.Text = _host.Text["settings.saved"];
        }
        catch (Exception e)
        {
            SaveStatus.Foreground = BadBrush;
            SaveStatus.Text = _host.Describe(e);
        }
    }

    private async Task OpenFolderAsync(string path)
    {
        Directory.CreateDirectory(path);
        await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
    }
}
