using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using MoonBot.Core.Settings;
using MoonBot.Core.Voice;
using MoonBot.Platform.Windows;

namespace MoonBot.App;

// Dictado con Ctrl derecho: mantener -> grabar -> soltar -> Whisper -> escribir donde está el cursor.
// Cada 30 ms consulta UNA tecla (no hay hook de teclado). La lógica de cuándo grabar o cancelar
// vive en Core (PushToTalk) y está probada; aquí solo se conecta con Windows y con la interfaz.
[SupportedOSPlatform("windows")]
public sealed class VoiceController : IDisposable
{
    private static readonly TimeSpan MinHold = TimeSpan.FromMilliseconds(350);

    private readonly MoonBotHost _host;
    private readonly MainWindow _moon;
    private readonly ChatPopup _chat;
    private readonly HubWindow _hub;
    private readonly PushToTalk _pushToTalk = new(MinHold);
    private readonly MicRecorder _mic = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer;
    private bool _recording;
    private bool _transcribing;
    private IntPtr _targetWindow;
    private bool _targetIsMoonBot;
    private TimeSpan _lastHint = TimeSpan.FromHours(-1);

    public VoiceController(MoonBotHost host, MainWindow moon, ChatPopup chat, HubWindow hub)
    {
        _host = host;
        _moon = moon;
        _chat = chat;
        _hub = hub;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
    }

    private void Poll()
    {
        if (!_host.Settings.VoiceEnabled || _transcribing)
        {
            if (_recording) Cancel();
            return;
        }

        var action = _pushToTalk.Update(
            KeyState.IsDown(KeyState.RightControl),
            KeyState.AnyOtherKeyDown(KeyState.RightControl),
            _clock.Elapsed);

        if (_recording)
            _moon.ListenLevel = _mic.Level;

        switch (action)
        {
            case PushToTalkAction.StartRecording:
                Start();
                break;
            case PushToTalkAction.Cancel:
                Cancel();
                break;
            case PushToTalkAction.Finish when _recording:
                _ = FinishAsync();
                break;
        }
    }

    private void Start()
    {
        if (_host.SpeechToText is null)
        {
            // Sin modelo no hay dictado: se avisa (como mucho cada 10 s) y se abre la página de Voz.
            if (_clock.Elapsed - _lastHint > TimeSpan.FromSeconds(10))
            {
                _lastHint = _clock.Elapsed;
                _moon.Say(_host.Text["voice.needModel"]);
                _hub.Open(HubPage.Voice);
            }
            return;
        }

        try
        {
            _mic.Start();
        }
        catch (InvalidOperationException e)
        {
            _host.Log.Error("Microphone unavailable", e);
            _moon.Say(_host.Text["voice.noMic"]);
            return;
        }

        _recording = true;
        _targetWindow = KeyState.ForegroundWindow();
        _targetIsMoonBot = KeyState.ForegroundIsThisProcess();
        _moon.SetListening(true);
        _moon.Say(_host.Text["voice.listening"]);
    }

    private void Cancel()
    {
        if (!_recording)
            return;
        _mic.Stop();
        _recording = false;
        _moon.SetListening(false);
        _moon.HideBubble();
    }

    private async Task FinishAsync()
    {
        var samples = _mic.Stop();
        _recording = false;
        _transcribing = true;
        _moon.SetListening(false);
        _moon.Say(_host.Text["voice.transcribing"]);

        try
        {
            var stt = _host.SpeechToText!;
            var language = _host.Settings.VoiceLanguage;
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var text = await Task.Run(() => stt.TranscribeAsync(samples, language, timeout.Token));

            if (text.Length == 0)
                _moon.Say(_host.Text["voice.nothing"]);
            else
                await DeliverAsync(text);

            // Se registra cuánto se dictó, nunca qué se dijo.
            _host.Log.Info($"Dictation: {samples.Length / MicRecorder.SampleRate}s audio, {text.Length} chars");
        }
        catch (Exception e)
        {
            _host.Log.Error("Dictation failed", e);
            _moon.Say(_host.Text.Format("voice.failed", e.Message));
        }
        finally
        {
            _transcribing = false;
        }
    }

    private async Task DeliverAsync(string text)
    {
        // Al chat si: el usuario lo eligió, estaba en MoonBot, o cambió de ventana mientras se transcribía
        // (escribir en una ventana distinta a la que tenía delante sería una sorpresa).
        var toChat = _host.Settings.VoiceTarget == VoiceTarget.Chat
                     || _targetIsMoonBot
                     || KeyState.ForegroundWindow() != _targetWindow;
        if (toChat)
        {
            _chat.ShowNextTo(_moon);
            _chat.AppendToInput(text);
            _moon.HideBubble();
            return;
        }

        // Si Ctrl/Shift/Alt siguen abajo, las letras se convertirían en atajos: se espera a soltarlos.
        for (var i = 0; i < 40 && KeyState.AnyModifierDown(); i++)
            await Task.Delay(25);

        TextTyper.Type(text);
        _moon.Say(_host.Text.Format("voice.typed", text.Length > 40 ? text[..40] + "…" : text));
    }

    public void Dispose()
    {
        _timer.Stop();
        _mic.Dispose();
    }
}
