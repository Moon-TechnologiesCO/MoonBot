using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MoonBot.Core;
using MoonBot.Core.Localization;
using MoonBot.Core.Placement;
using PlacementMode = MoonBot.Core.Placement.PlacementMode;
using MoonBot.Core.Settings;
using MoonBot.Infrastructure.Ai;
using MoonBot.Infrastructure.Storage;

namespace MoonBot.App;

public enum HubPage { General, Ai, Appearance, Files, About }

// El "Hub": todos los ajustes de MoonBot. El chat vive en ChatPopup, junto a la luna.
public partial class HubWindow : Window
{
    private static readonly IBrush OkBrush = new SolidColorBrush(Color.Parse("#7FE0A0"));
    private static readonly IBrush BadBrush = new SolidColorBrush(Color.Parse("#FF8A8A"));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#A9A6CF"));

    private readonly MoonBotHost _host;
    private bool _loadingForm;

    // Solo para el diseñador de Avalonia.
    public HubWindow() : this(new MoonBotHost()) { }

    public HubWindow(MoonBotHost host)
    {
        _host = host;
        InitializeComponent();

        NavGeneral.Click += (_, _) => ShowPage(HubPage.General);
        NavAi.Click += (_, _) => ShowPage(HubPage.Ai);
        NavAppearance.Click += (_, _) => ShowPage(HubPage.Appearance);
        NavFiles.Click += (_, _) => ShowPage(HubPage.Files);
        NavAbout.Click += (_, _) => ShowPage(HubPage.About);
        OpenChatButton.Click += (_, _) => ChatRequested?.Invoke(this, EventArgs.Empty);

        SaveGeneralButton.Click += (_, _) => SaveGeneral();
        ProviderBox.SelectionChanged += (_, _) => OnProviderChanged();
        RefreshModelsButton.Click += async (_, _) => await TestConnectionAsync();
        TestButton.Click += async (_, _) => await TestConnectionAsync();
        RemoveKeyButton.Click += (_, _) => { _host.DeleteApiKey(); UpdateKeyStatus(); };
        SaveAiButton.Click += (_, _) => SaveAi();

        PlacementBox.SelectionChanged += (_, _) =>
        {
            if (_loadingForm || PlacementBox.SelectedIndex < 0) return;
            var mode = (PlacementMode)PlacementBox.SelectedIndex;
            _host.Update(s => s with { Placement = mode });
        };
        ResetPlacementButton.Click += (_, _) =>
            _host.Update(s => s with { Placement = PlacementMode.Auto, CustomX = null, CustomY = null });

        ToolsSwitch.IsCheckedChanged += (_, _) =>
        {
            if (_loadingForm) return;
            _host.Update(s => s with { ToolsEnabled = ToolsSwitch.IsChecked == true });
        };

        OpenLogsButton.Click += async (_, _) => await OpenFolderAsync(AppPaths.LogsDirectory);
        OpenDataButton.Click += async (_, _) => await OpenFolderAsync(AppPaths.SettingsDirectory);

        _host.Machine.StateChanged += (_, e) => Avalonia.Threading.Dispatcher.UIThread.Post(() => Logo.State = e.To);
        _host.SettingsChanged += (_, _) => { ApplyTexts(); LoadForm(); };

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
        ShowPage(HubPage.General);
    }

    public event EventHandler? ChatRequested;

    public void Open(HubPage page)
    {
        ShowPage(page);
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    private void ShowPage(HubPage page)
    {
        GeneralPage.IsVisible = page == HubPage.General;
        AiPage.IsVisible = page == HubPage.Ai;
        AppearancePage.IsVisible = page == HubPage.Appearance;
        FilesPage.IsVisible = page == HubPage.Files;
        AboutPage.IsVisible = page == HubPage.About;

        NavGeneral.Classes.Set("selected", page == HubPage.General);
        NavAi.Classes.Set("selected", page == HubPage.Ai);
        NavAppearance.Classes.Set("selected", page == HubPage.Appearance);
        NavFiles.Classes.Set("selected", page == HubPage.Files);
        NavAbout.Classes.Set("selected", page == HubPage.About);
    }

    // ---------- Textos ----------

    private void ApplyTexts()
    {
        var t = _host.Text;
        Tagline.Text = t["app.tagline"];
        NavGeneral.Content = "⚙️   " + t["nav.general"];
        NavAi.Content = "✨   " + t["nav.ai"];
        NavAppearance.Content = "🎨   " + t["nav.appearance"];
        NavFiles.Content = "🗂   " + t["nav.files"];
        NavAbout.Content = "🌙   " + t["nav.about"];
        OpenChatButton.Content = t["tray.chat"];

        GeneralTitle.Text = t["nav.general"];
        LanguageLabel.Text = t["settings.language"];
        NameLabel.Text = t["settings.name"];
        NameBox.PlaceholderText = t["settings.namePlaceholder"];
        SleepLabel.Text = t["settings.sleepAfter"];
        SaveGeneralButton.Content = t["settings.save"];

        AiTitle.Text = t["settings.ai"];
        ProviderLabel.Text = t["settings.provider"];
        UrlLabel.Text = t["settings.url"];
        ModelLabel.Text = t["settings.model"];
        ModelHint.Text = t["settings.modelToolsHint"];
        RefreshModelsButton.Content = t["settings.refresh"];
        KeyLabel.Text = t["settings.apiKey"];
        KeyBox.PlaceholderText = t["settings.apiKeyPlaceholder"];
        RemoveKeyButton.Content = t["settings.apiKeyRemove"];
        TestButton.Content = t["settings.test"];
        SaveAiButton.Content = t["settings.save"];
        var providerIndex = ProviderBox.SelectedIndex;
        ProviderBox.ItemsSource = new[] { t["settings.provider.ollama"], t["settings.provider.openai"] };
        ProviderBox.SelectedIndex = providerIndex;

        AppearanceTitle.Text = t["appearance.title"];
        SkinLabel.Text = t["appearance.skin"];
        AccessoryLabel.Text = t["appearance.accessory"];
        PlacementLabel.Text = t["appearance.position"];
        ResetPlacementButton.Content = t["placement.reset"];
        var placementIndex = PlacementBox.SelectedIndex;
        PlacementBox.ItemsSource = new[] { t["placement.auto"], t["placement.top"], t["placement.custom"] };
        PlacementBox.SelectedIndex = placementIndex;

        FilesTitle.Text = t["files.title"];
        ToolsSwitch.Content = t["files.enable"];
        FilesHelp.Text = t["files.enableHelp"];
        FoldersLabel.Text = t["files.folders"];
        CloudNote.Text = t["files.cloudNote"];
        AuditNote.Text = t["files.audit"];
        FoldersList.ItemsSource = _host.Sandbox.Roots.Values.ToList();

        AboutTitle.Text = t["about.title"];
        AboutTagline.Text = t["app.tagline"];
        AboutVersion.Text = t.Format("about.version", AppVersion);
        PrivacyTitle.Text = t["about.privacyTitle"];
        PrivacyText.Text = t["about.privacy"];
        OpenLogsButton.Content = t["about.logs"];
        OpenDataButton.Content = t["about.data"];

        foreach (var (button, key) in new (Button, string)[]
                 {
                     (NavGeneral, "nav.general"), (NavAi, "nav.ai"), (NavAppearance, "nav.appearance"),
                     (NavFiles, "nav.files"), (NavAbout, "nav.about")
                 })
            AutomationProperties.SetName(button, t[key]);

        BuildTiles();
        UpdateKeyStatus();
    }

    private static string AppVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    // ---------- Apariencia: miniaturas vivas de cada skin y accesorio ----------

    private void BuildTiles()
    {
        var s = _host.Settings;
        var skin = MoonSkins.Get(s.Skin);
        var accessory = MoonAccessories.Get(s.Accessory);

        Preview.Skin = skin;
        Preview.Accessory = accessory;
        Logo.Skin = AboutMoon.Skin = skin;
        Logo.Accessory = AboutMoon.Accessory = accessory;

        SkinTiles.Children.Clear();
        foreach (var option in MoonSkins.All)
        {
            var id = option.Id;
            SkinTiles.Children.Add(Tile(_host.Text[option.NameKey], option, accessory, id == skin.Id,
                () => _host.Update(x => x with { Skin = id })));
        }

        AccessoryTiles.Children.Clear();
        foreach (var option in MoonAccessories.All)
        {
            var id = option;
            AccessoryTiles.Children.Add(Tile(_host.Text["acc." + id], skin, id, id == accessory,
                () => _host.Update(x => x with { Accessory = id })));
        }
    }

    private static Button Tile(string name, MoonSkin skin, string accessory, bool selected, Action onClick)
    {
        var button = new Button
        {
            Margin = new Avalonia.Thickness(0, 0, 8, 8),
            Content = new StackPanel
            {
                Width = 88,
                Children =
                {
                    new MoonView { Width = 80, Height = 80, Skin = skin, Accessory = accessory, IsHitTestVisible = false,
                                   HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = name, FontSize = 11.5, TextAlignment = TextAlignment.Center,
                                    TextWrapping = TextWrapping.Wrap }
                }
            }
        };
        button.Classes.Add("tile");
        button.Classes.Set("selected", selected);
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => onClick();
        return button;
    }

    // ---------- Formularios ----------

    private void LoadForm()
    {
        _loadingForm = true;
        var s = _host.Settings;

        LanguageBox.ItemsSource = Localizer.Languages.Select(l => l.NativeName).ToList();
        LanguageBox.SelectedIndex = Math.Max(0, Localizer.Languages.ToList().FindIndex(l => l.Code == s.Language));
        NameBox.Text = s.UserName;
        SleepBox.Value = s.SleepAfterMinutes;
        ProviderBox.SelectedIndex = s.Provider == ProviderKind.OpenAiCompatible ? 1 : 0;
        UrlBox.Text = s.BaseUrl;
        ModelBox.Text = s.Model;
        KeyBox.Text = "";
        KeySection.IsVisible = s.Provider == ProviderKind.OpenAiCompatible;
        PlacementBox.SelectedIndex = (int)s.Placement;
        ToolsSwitch.IsChecked = s.ToolsEnabled;
        UpdateKeyStatus();

        _loadingForm = false;
    }

    private void SaveGeneral()
    {
        _host.Update(s => s with
        {
            Language = Localizer.Languages[Math.Max(0, LanguageBox.SelectedIndex)].Code,
            UserName = NameBox.Text?.Trim() ?? "",
            SleepAfterMinutes = (int)(SleepBox.Value ?? 2)
        });
        GeneralStatus.Foreground = OkBrush;
        GeneralStatus.Text = _host.Text["settings.saved"];
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

    private MoonSettings ReadAiForm() => _host.Settings with
    {
        Provider = SelectedProvider,
        BaseUrl = UrlBox.Text?.Trim() ?? "",
        Model = ModelBox.Text?.Trim() ?? ""
    };

    private string? NewKey => string.IsNullOrWhiteSpace(KeyBox.Text) ? null : KeyBox.Text.Trim();

    private void UpdateKeyStatus()
    {
        var hasKey = _host.HasApiKey;
        KeyStatus.Text = _host.Text[hasKey ? "settings.apiKeySaved" : "settings.apiKeyNone"];
        RemoveKeyButton.IsVisible = hasKey;
    }

    private async Task TestConnectionAsync()
    {
        TestResult.Foreground = MutedBrush;
        TestResult.Text = _host.Text["settings.testing"];
        TestButton.IsEnabled = RefreshModelsButton.IsEnabled = false;
        try
        {
            var provider = _host.CreateProvider(ReadAiForm(), NewKey);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var models = await provider.ListModelsAsync(timeout.Token);

            TestResult.Foreground = OkBrush;
            TestResult.Text = _host.Text.Format("settings.testOk", models.Count);
            ModelBox.ItemsSource = models;
            if (string.IsNullOrWhiteSpace(ModelBox.Text) && models.Count > 0)
                ModelBox.Text = models[0];
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

    private void SaveAi()
    {
        try
        {
            _host.Apply(ReadAiForm(), NewKey);
            KeyBox.Text = "";
            UpdateKeyStatus();
            AiStatus.Foreground = OkBrush;
            AiStatus.Text = _host.Text["settings.saved"];
        }
        catch (Exception e)
        {
            AiStatus.Foreground = BadBrush;
            AiStatus.Text = _host.Describe(e);
        }
    }

    private async Task OpenFolderAsync(string path)
    {
        Directory.CreateDirectory(path);
        await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
    }
}
