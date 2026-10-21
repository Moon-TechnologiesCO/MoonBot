using Avalonia;
using System;

namespace MoonBot.App;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Red de seguridad: cualquier error no controlado queda en el log antes de que la app se cierre.
        // (Un StackOverflowException no se puede atrapar en .NET; todo lo demás sí.)
        var crashLog = new MoonBot.Infrastructure.Storage.FileEventLog(MoonBot.Infrastructure.Storage.AppPaths.LogsDirectory);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            crashLog.Error("Unhandled exception", e.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            crashLog.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
