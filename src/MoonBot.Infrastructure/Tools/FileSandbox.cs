using System.Globalization;
using System.Text;
using MoonBot.Core.Tools;

namespace MoonBot.Infrastructure.Tools;

// Límite de lo que las herramientas pueden tocar: solo las carpetas personales del usuario.
// Toda ruta que viene del modelo pasa por Resolve(), que la normaliza y la rechaza si se sale.
public sealed class FileSandbox
{
    // Extensiones que ejecutan código al abrirse. Renombrar algo HACIA ellas está prohibido.
    public static readonly IReadOnlySet<string> ExecutableExtensions = new HashSet<string>(
        [".exe", ".bat", ".cmd", ".com", ".ps1", ".vbs", ".js", ".jse", ".wsf", ".msi", ".scr", ".lnk", ".hta", ".cpl", ".reg"],
        StringComparer.OrdinalIgnoreCase);

    public FileSandbox(IReadOnlyDictionary<string, string> roots)
    {
        Roots = roots.ToDictionary(r => r.Key.ToLowerInvariant(),
            r => Path.TrimEndingDirectorySeparator(Path.GetFullPath(r.Value)));
    }

    // alias ("documents") -> ruta completa
    public IReadOnlyDictionary<string, string> Roots { get; }

    public static FileSandbox ForCurrentUser()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new Dictionary<string, string>
        {
            ["desktop"] = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            ["documents"] = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            ["downloads"] = Path.Combine(home, "Downloads"),
            ["pictures"] = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            ["music"] = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            ["videos"] = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };
        return new FileSandbox(candidates
            .Where(c => !string.IsNullOrEmpty(c.Value) && Directory.Exists(c.Value))
            .ToDictionary());
    }

    // Acepta una ruta completa ("C:\Users\Ana\Documents\x.pdf") o relativa a un alias ("documents/Facturas").
    public string Resolve(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ToolArgumentException("A path is required.");

        var trimmed = path.Trim().Trim('"');
        string full;
        if (Path.IsPathFullyQualified(trimmed))
        {
            full = Path.GetFullPath(trimmed);
        }
        else
        {
            var parts = trimmed.Split(['/', '\\'], 2);
            if (!Roots.TryGetValue(parts[0].ToLowerInvariant(), out var root))
                throw new ToolArgumentException(
                    $"Use a full path or start with one of: {string.Join(", ", Roots.Keys)}.");
            full = Path.GetFullPath(parts.Length > 1 ? Path.Combine(root, parts[1]) : root);
        }

        full = Path.TrimEndingDirectorySeparator(full);
        if (!IsInside(full))
            throw new ToolArgumentException("That location is outside the folders MoonBot may use.");

        EnsureNoLinks(full);
        return full;
    }

    public bool IsInside(string fullPath)
        => Roots.Values.Any(root =>
            fullPath.Equals(root, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

    public bool IsRoot(string fullPath)
        => Roots.Values.Any(root => fullPath.Equals(root, StringComparison.OrdinalIgnoreCase));

    // Un enlace simbólico o una "junction" dentro de Documentos podría apuntar a C:\Windows.
    // Se rechaza cualquier tramo de la ruta que sea un enlace.
    private void EnsureNoLinks(string fullPath)
    {
        var root = Roots.Values.First(r => IsInsideRoot(fullPath, r));
        for (var current = fullPath; current.Length > root.Length; current = Path.GetDirectoryName(current)!)
        {
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.Exists && info.LinkTarget is not null)
                throw new ToolArgumentException("Links and junctions are not allowed.");
        }
    }

    private static bool IsInsideRoot(string fullPath, string root)
        => fullPath.Equals(root, StringComparison.OrdinalIgnoreCase)
           || fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    // Nombre de archivo válido en Windows, sin rutas escondidas dentro.
    public static string ValidateName(string? name)
    {
        var value = name?.Trim() ?? "";
        if (value.Length is 0 or > 200 || value is "." or ".."
            || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.EndsWith('.'))
            throw new ToolArgumentException($"'{name}' is not a valid name.");
        return value;
    }

    // "Presupuesto Año" -> "presupuesto ano": así "ano" encuentra "Año".
    public static string Fold(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        return builder.ToString();
    }
}
