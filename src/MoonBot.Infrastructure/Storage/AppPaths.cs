namespace MoonBot.Infrastructure.Storage;

// Dónde vive cada cosa. En Windows:
//   ajustes -> %APPDATA%\MoonBot          (viaja con el perfil del usuario)
//   logs    -> %LOCALAPPDATA%\MoonBot\logs (se queda en esta máquina)
public static class AppPaths
{
    public static string SettingsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MoonBot");

    public static string SettingsFile { get; } = Path.Combine(SettingsDirectory, "settings.json");

    public static string LogsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MoonBot", "logs");
}
