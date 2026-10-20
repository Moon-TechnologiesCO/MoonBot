using System.Globalization;
using System.Text.Json;

namespace MoonBot.Core.Localization;

public sealed record LanguageInfo(string Code, string NativeName, string EnglishName);

// Textos de la interfaz en varios idiomas. Cada idioma es un JSON incrustado en la DLL.
// Si a un idioma le falta una frase, se usa la inglesa; si tampoco existe, se muestra la clave.
public sealed class Localizer
{
    // El primero es el idioma por defecto.
    public static readonly IReadOnlyList<LanguageInfo> Languages =
    [
        new("en", "English", "English"),
        new("es", "Español", "Spanish"),
        new("pt", "Português", "Portuguese"),
        new("fr", "Français", "French"),
        new("de", "Deutsch", "German"),
        new("it", "Italiano", "Italian"),
        new("zh", "中文", "Chinese (Simplified)"),
        new("ja", "日本語", "Japanese")
    ];

    private static readonly Dictionary<string, Localizer> Cache = [];

    private readonly Dictionary<string, string> _own;
    private readonly Localizer? _fallback;

    private Localizer(LanguageInfo language, Dictionary<string, string> own, Localizer? fallback)
    {
        Language = language;
        _own = own;
        _fallback = fallback;
    }

    public LanguageInfo Language { get; }
    public IEnumerable<string> Keys => _own.Keys;

    public string this[string key]
        => _own.TryGetValue(key, out var text) ? text
         : _fallback is not null ? _fallback[key]
         : key;

    public bool HasOwn(string key) => _own.ContainsKey(key);

    public string Format(string key, params object[] args)
        => string.Format(CultureInfo.InvariantCulture, this[key], args);

    public static Localizer For(string code)
    {
        var language = Languages.FirstOrDefault(l => l.Code == code) ?? Languages[0];

        lock (Cache)
        {
            if (Cache.TryGetValue(language.Code, out var cached))
                return cached;

            var fallback = language == Languages[0] ? null : For(Languages[0].Code);
            var localizer = new Localizer(language, LoadStrings(language.Code), fallback);
            Cache[language.Code] = localizer;
            return localizer;
        }
    }

    private static Dictionary<string, string> LoadStrings(string code)
    {
        var assembly = typeof(Localizer).Assembly;
        using var stream = assembly.GetManifestResourceStream($"MoonBot.Strings.{code}.json");
        if (stream is null)
            return [];

        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}
