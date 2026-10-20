using System.Text.Json;
using System.Text.Json.Serialization;
using MoonBot.Core.Settings;

namespace MoonBot.Infrastructure.Storage;

// Guarda los ajustes en un JSON legible. Nunca contiene secretos (MoonSettings no los tiene).
public sealed class JsonSettingsStore(string path) : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public MoonSettings Load()
    {
        try
        {
            if (!File.Exists(path))
                return new MoonSettings();

            return JsonSerializer.Deserialize<MoonSettings>(File.ReadAllText(path), Options) ?? new MoonSettings();
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            // Un archivo dañado no debe impedir que MoonBot arranque.
            return new MoonSettings();
        }
    }

    public void Save(MoonSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Se escribe a un archivo temporal y luego se reemplaza: si se corta la luz a mitad,
        // queda el archivo viejo completo y no uno a medias.
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, path, overwrite: true);
    }
}
