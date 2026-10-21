namespace MoonBot.Core.Diagnostics;

// Registro de eventos. Regla de privacidad: nunca se registra el texto de los mensajes ni secretos.
public interface IEventLog
{
    void Info(string message);
    void Error(string message, Exception? exception = null);
}
