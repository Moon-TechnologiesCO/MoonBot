namespace MoonBot.Core.Voice;

public enum PushToTalkAction { None, StartRecording, Cancel, Finish }

// Decide qué hacer con la tecla de dictado (Ctrl derecho). No sabe nada del teclado real:
// recibe "¿está pulsada?", "¿hay otra tecla pulsada?" y la hora, y devuelve una acción.
//   - Empieza a grabar en cuanto se pulsa (para no perder las primeras palabras).
//   - Si se usa en un atajo (Ctrl+C) o se suelta muy rápido, se cancela.
//   - Si se mantiene demasiado, termina sola (tecla "pegada").
public sealed class PushToTalk(TimeSpan minHold, TimeSpan? maxHold = null)
{
    private readonly TimeSpan _maxHold = maxHold ?? TimeSpan.FromMinutes(2);
    private TimeSpan _start;
    private bool _done; // ya se canceló o terminó, pero la tecla sigue abajo

    public bool IsHeld { get; private set; }

    public PushToTalkAction Update(bool keyDown, bool otherKeyDown, TimeSpan now)
    {
        if (!IsHeld)
        {
            if (!keyDown || otherKeyDown)
                return PushToTalkAction.None;

            IsHeld = true;
            _done = false;
            _start = now;
            return PushToTalkAction.StartRecording;
        }

        if (!keyDown)
        {
            IsHeld = false;
            if (_done)
                return PushToTalkAction.None;
            return now - _start < minHold ? PushToTalkAction.Cancel : PushToTalkAction.Finish;
        }

        if (_done)
            return PushToTalkAction.None;

        if (otherKeyDown)
        {
            _done = true;
            return PushToTalkAction.Cancel;
        }

        if (now - _start >= _maxHold)
        {
            _done = true;
            return PushToTalkAction.Finish;
        }

        return PushToTalkAction.None;
    }
}
