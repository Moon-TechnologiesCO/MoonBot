using System.Text.RegularExpressions;

namespace MoonBot.Core.Voice;

// Convierte audio en texto. La implementación local usa Whisper; un test usa un falso.
// samples: audio mono, 16 kHz, valores entre -1 y 1.
public interface ISpeechToText
{
    Task<string> TranscribeAsync(float[] samples, string language, CancellationToken cancellationToken);
}

public static partial class Transcript
{
    // Whisper a veces devuelve anotaciones como "[BLANK_AUDIO]" o "(music)" y espacios sobrantes.
    public static string Clean(string raw)
    {
        var text = Annotations().Replace(raw, " ");
        text = Whitespace().Replace(text, " ").Trim();
        return text.Trim('.', ' ').Length == 0 ? "" : text;
    }

    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)")]
    private static partial Regex Annotations();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
