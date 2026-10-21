using System.Text;
using MoonBot.Core.Voice;
using Whisper.net;

namespace MoonBot.Infrastructure.Voice;

// Voz a texto 100 % local con whisper.cpp (paquete Whisper.net, MIT). El audio nunca sale del PC.
// Cargar el modelo cuesta (cientos de MB), así que se carga una vez y se reutiliza.
public sealed class WhisperSpeechToText(string modelPath) : ISpeechToText, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WhisperFactory? _factory;

    public async Task<string> TranscribeAsync(float[] samples, string language, CancellationToken cancellationToken)
    {
        if (samples.Length < 16_000 / 4) // menos de un cuarto de segundo: nada que transcribir
            return "";

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _factory ??= WhisperFactory.FromPath(modelPath);

            var builder = _factory.CreateBuilder().WithThreads(Math.Max(2, Environment.ProcessorCount / 2));
            builder = language is "" or "auto" ? builder.WithLanguageDetection() : builder.WithLanguage(language);
            await using var processor = builder.Build();

            var text = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(samples, cancellationToken))
                text.Append(segment.Text);

            return Transcript.Clean(text.ToString());
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _factory?.Dispose();
        _gate.Dispose();
    }
}
