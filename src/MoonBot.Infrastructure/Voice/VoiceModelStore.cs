using System.Security.Cryptography;

namespace MoonBot.Infrastructure.Voice;

// Un modelo de Whisper descargable. Sha256 y Size vienen del repositorio oficial y quedan fijados:
// si el archivo descargado no coincide exactamente, se rechaza (cadena de suministro).
public sealed record VoiceModel(string Id, string FileName, Uri Url, string Sha256, long Size);

public static class VoiceModels
{
    // Fuente: punteros Git LFS de https://huggingface.co/ggerganov/whisper.cpp (licencia MIT),
    // consultados el 20 de octubre de 2026.
    public static readonly IReadOnlyList<VoiceModel> All =
    [
        new("base", "ggml-base.bin",
            new Uri("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin"),
            "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe", 147_951_465),
        new("small", "ggml-small.bin",
            new Uri("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin"),
            "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b", 487_601_967)
    ];

    public static VoiceModel Get(string? id) => All.FirstOrDefault(m => m.Id == id) ?? All[0];
}

// Guarda los modelos en una carpeta local y los descarga verificando tamaño y hash.
public sealed class VoiceModelStore(string directory, HttpClient http)
{
    public string Directory { get; } = directory;

    public string PathOf(VoiceModel model) => Path.Combine(Directory, model.FileName);

    // Se comprueba el tamaño (rápido). El hash completo se verifica al descargar.
    public bool IsInstalled(VoiceModel model)
    {
        var file = new FileInfo(PathOf(model));
        return file.Exists && file.Length == model.Size;
    }

    public async Task DownloadAsync(VoiceModel model, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var temp = PathOf(model) + ".download";

        try
        {
            using (var response = await http.GetAsync(model.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var target = File.Create(temp);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

                var buffer = new byte[1 << 16];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    hash.AppendData(buffer, 0, read);
                    total += read;
                    if (total > model.Size)
                        throw new InvalidDataException("The download is larger than expected.");
                    progress?.Report((double)total / model.Size);
                }

                var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
                if (total != model.Size || actual != model.Sha256)
                    throw new InvalidDataException("The downloaded model does not match the official checksum.");
            }

            File.Move(temp, PathOf(model), overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
