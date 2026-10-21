using System.Globalization;
using MoonBot.Core.Diagnostics;

namespace MoonBot.Infrastructure.Storage;

// Un archivo de log por día. Si escribir falla, se ignora: el log nunca debe tumbar la app.
public sealed class FileEventLog(string directory) : IEventLog
{
    private readonly Lock _gate = new();

    public string Directory { get; } = directory;

    public void Info(string message) => Write("INFO ", message);

    public void Error(string message, Exception? exception = null)
        => Write("ERROR", exception is null ? message : $"{message} | {exception.GetType().Name}: {exception.Message}");

    private void Write(string level, string message)
    {
        var now = DateTime.Now;
        var file = Path.Combine(Directory, $"moonbot-{now:yyyyMMdd}.log");
        var line = $"{now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} {level} {message}{Environment.NewLine}";

        try
        {
            lock (_gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.AppendAllText(file, line);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
