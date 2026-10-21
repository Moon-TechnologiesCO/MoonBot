using MoonBot.Infrastructure.Storage;
using MoonBot.Infrastructure.Voice;

namespace MoonBot.Infrastructure.Tests;

// Solo corre si MOONBOT_WHISPER_IT=1. Descarga el modelo "base" real (148 MB, verificado por hash)
// en la carpeta de modelos de MoonBot y transcribe un WAV de 16 kHz indicado en MOONBOT_WHISPER_WAV.
public sealed class WhisperFactAttribute : FactAttribute
{
    public WhisperFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("MOONBOT_WHISPER_IT") != "1")
            Skip = "Set MOONBOT_WHISPER_IT=1 (and MOONBOT_WHISPER_WAV) to run Whisper for real.";
    }
}

public class WhisperIntegrationTests
{
    [WhisperFact]
    public async Task RealModel_DownloadsWithAValidHash_AndTranscribesSpeech()
    {
        var model = VoiceModels.Get("base");
        var store = new VoiceModelStore(AppPaths.ModelsDirectory, new HttpClient { Timeout = TimeSpan.FromMinutes(20) });
        if (!store.IsInstalled(model))
            await store.DownloadAsync(model, null, CancellationToken.None); // lanza si el hash no coincide

        using var stt = new WhisperSpeechToText(store.PathOf(model));
        var samples = ReadPcm16Mono(Environment.GetEnvironmentVariable("MOONBOT_WHISPER_WAV")!);

        var text = await stt.TranscribeAsync(samples, "auto", CancellationToken.None);

        Assert.Contains("budget", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("moon", text, StringComparison.OrdinalIgnoreCase);
    }

    // WAV PCM de 16 bits: se busca el bloque "data" y se convierte a floats.
    private static float[] ReadPcm16Mono(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var position = 12;
        while (position + 8 <= bytes.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(bytes, position, 4);
            var size = BitConverter.ToInt32(bytes, position + 4);
            if (id == "data")
            {
                var samples = new float[size / 2];
                for (var i = 0; i < samples.Length; i++)
                    samples[i] = BitConverter.ToInt16(bytes, position + 8 + i * 2) / 32768f;
                return samples;
            }
            position += 8 + size;
        }
        throw new InvalidDataException("No PCM data in the WAV file.");
    }
}
