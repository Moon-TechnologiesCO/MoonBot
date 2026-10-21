using System.Diagnostics;
using System.IO.Enumeration;
using System.Text.Json;
using MoonBot.Core.Chat;
using MoonBot.Core.Tools;

namespace MoonBot.Infrastructure.Tools;

// Las herramientas de archivos de MoonBot. No existe ninguna para borrar, a propósito.
public static class FileTools
{
    public static IReadOnlyList<ITool> All(FileSandbox sandbox) =>
    [
        new SearchFilesTool(sandbox),
        new ListFolderTool(sandbox),
        new CreateFolderTool(sandbox),
        new RenameItemTool(sandbox),
        new MoveItemsTool(sandbox)
    ];

    internal static string String(JsonElement args, string name, bool required = true)
    {
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
            return value.GetString()!;
        if (required)
            throw new ToolArgumentException($"Missing '{name}'.");
        return "";
    }

    internal static string Json(object value) => JsonSerializer.Serialize(value);

    internal static object Describe(string path) => new
    {
        name = Path.GetFileName(path),
        path,
        isFolder = Directory.Exists(path),
        modified = File.GetLastWriteTime(path).ToString("yyyy-MM-dd")
    };
}

// ---------- Solo lectura ----------

public sealed class SearchFilesTool(FileSandbox sandbox) : ITool
{
    private const int MaxScanned = 60_000;
    private static readonly TimeSpan MaxTime = TimeSpan.FromSeconds(6);
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
        { "node_modules", ".git", "bin", "obj", "AppData", "$RECYCLE.BIN" };

    public ToolDefinition Definition { get; } = new("search_files",
        "Find files or folders whose NAME contains the words in 'query' (not case or accent sensitive). " +
        "Searches the user's desktop, documents, downloads, pictures, music and videos. Returns full paths.",
        """
        {"type":"object","properties":{
          "query":{"type":"string","description":"Words that appear in the file name, e.g. 'budget 2026'"},
          "folder":{"type":"string","enum":["all","desktop","documents","downloads","pictures","music","videos"],"description":"Where to search. Default: all"}
        },"required":["query"]}
        """);

    public ToolRisk Risk => ToolRisk.ReadOnly;

    public ToolPreview Preview(JsonElement arguments)
        => new("tool.search", [FileTools.String(arguments, "query")]);

    public Task<ToolOutcome> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var query = FileTools.String(arguments, "query");
        var folder = FileTools.String(arguments, "folder", required: false).ToLowerInvariant();
        var roots = folder is "" or "all"
            ? sandbox.Roots.Values.ToList()
            : sandbox.Roots.TryGetValue(folder, out var one) ? [one] : sandbox.Roots.Values.ToList();

        var results = Search(roots, query, 25, cancellationToken);
        var outcome = new
        {
            query,
            count = results.Count,
            results = results.Take(15).Select(FileTools.Describe)
        };
        return Task.FromResult(new ToolOutcome(true, FileTools.Json(outcome), results.Take(8).ToList()));
    }

    // También la usa el comando rápido "/find", sin pasar por la IA.
    public List<string> Search(IEnumerable<string> roots, string query, int limit, CancellationToken cancellationToken)
    {
        var words = FileSandbox.Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return [];

        var clock = Stopwatch.StartNew();
        var scanned = 0;
        var found = new List<(string Path, DateTime Modified)>();

        foreach (var root in roots)
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
            };
            var entries = new FileSystemEnumerable<(string Path, DateTime Modified)>(root,
                (ref FileSystemEntry e) => (e.ToFullPath(), e.LastWriteTimeUtc.UtcDateTime), options)
            {
                ShouldIncludePredicate = (ref FileSystemEntry e) =>
                {
                    var name = FileSandbox.Fold(e.FileName.ToString());
                    return words.All(w => name.Contains(w, StringComparison.Ordinal));
                },
                // No seguir enlaces (symlinks/junctions). Las carpetas de OneDrive también son
                // "reparse points" pero no son enlaces: esas sí se recorren.
                ShouldRecursePredicate = (ref FileSystemEntry e) =>
                    !SkippedFolders.Contains(e.FileName.ToString())
                    && ((e.Attributes & FileAttributes.ReparsePoint) == 0
                        || new DirectoryInfo(e.ToFullPath()).LinkTarget is null)
            };

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                found.Add(entry);
                if (++scanned > MaxScanned || clock.Elapsed > MaxTime)
                    break;
            }
        }

        return found
            .OrderByDescending(f => FileSandbox.Fold(Path.GetFileNameWithoutExtension(f.Path)) == string.Join(' ', words))
            .ThenByDescending(f => f.Modified)
            .Select(f => f.Path)
            .Take(limit)
            .ToList();
    }
}

public sealed class ListFolderTool(FileSandbox sandbox) : ITool
{
    public ToolDefinition Definition { get; } = new("list_folder",
        "List the files and folders inside a folder. Use a full path from earlier results, " +
        "or a name like 'documents' or 'downloads/invoices'.",
        """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}""");

    public ToolRisk Risk => ToolRisk.ReadOnly;

    public ToolPreview Preview(JsonElement arguments)
        => new("tool.list", [sandbox.Resolve(FileTools.String(arguments, "path"))]);

    public Task<ToolOutcome> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var path = sandbox.Resolve(FileTools.String(arguments, "path"));
        if (!Directory.Exists(path))
            throw new ToolArgumentException("That folder does not exist.");

        var entries = new DirectoryInfo(path)
            .EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = FileAttributes.Hidden | FileAttributes.System })
            .OrderByDescending(e => e is DirectoryInfo).ThenBy(e => e.Name)
            .Take(80)
            .Select(e => new { name = e.Name, path = e.FullName, isFolder = e is DirectoryInfo })
            .ToList();

        return Task.FromResult(new ToolOutcome(true, FileTools.Json(new { folder = path, entries }), [path]));
    }
}

// ---------- Cambian cosas: el ToolRouter pide permiso antes ----------

public sealed class CreateFolderTool(FileSandbox sandbox) : ITool
{
    public ToolDefinition Definition { get; } = new("create_folder",
        "Create a new folder. Give the full path of the new folder, e.g. 'documents/Invoices 2026'. " +
        "The user is asked to approve.",
        """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}""");

    public ToolRisk Risk => ToolRisk.Changes;

    public ToolPreview Preview(JsonElement arguments) => new("tool.createFolder", [Target(arguments)]);

    public Task<ToolOutcome> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var path = Target(arguments);
        var existed = Directory.Exists(path);
        Directory.CreateDirectory(path);
        return Task.FromResult(new ToolOutcome(true,
            FileTools.Json(new { created = !existed, alreadyExisted = existed, path }), [path]));
    }

    private string Target(JsonElement arguments)
    {
        var path = sandbox.Resolve(FileTools.String(arguments, "path"));
        FileSandbox.ValidateName(Path.GetFileName(path));
        if (File.Exists(path))
            throw new ToolArgumentException("A file with that name already exists.");
        return path;
    }
}

public sealed class RenameItemTool(FileSandbox sandbox) : ITool
{
    public ToolDefinition Definition { get; } = new("rename_item",
        "Rename one file or folder. 'path' is its current full path; 'new_name' is only the new name " +
        "(no folders). The user is asked to approve.",
        """{"type":"object","properties":{"path":{"type":"string"},"new_name":{"type":"string"}},"required":["path","new_name"]}""");

    public ToolRisk Risk => ToolRisk.Changes;

    public ToolPreview Preview(JsonElement arguments)
    {
        var (source, destination) = Plan(arguments);
        return new ToolPreview("tool.rename", [source], destination);
    }

    public Task<ToolOutcome> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var (source, destination) = Plan(arguments);
        if (Directory.Exists(source))
            Directory.Move(source, destination);
        else
            File.Move(source, destination);
        return Task.FromResult(new ToolOutcome(true, FileTools.Json(new { renamed = source, to = destination }), [destination]));
    }

    private (string Source, string Destination) Plan(JsonElement arguments)
    {
        var source = sandbox.Resolve(FileTools.String(arguments, "path"));
        if (!File.Exists(source) && !Directory.Exists(source))
            throw new ToolArgumentException("That file or folder does not exist.");
        if (sandbox.IsRoot(source))
            throw new ToolArgumentException("The main user folders cannot be renamed.");

        var newName = FileSandbox.ValidateName(FileTools.String(arguments, "new_name"));
        var newExtension = Path.GetExtension(newName);
        if (FileSandbox.ExecutableExtensions.Contains(newExtension)
            && !newExtension.Equals(Path.GetExtension(source), StringComparison.OrdinalIgnoreCase))
            throw new ToolArgumentException($"Renaming a file to '{newExtension}' is not allowed.");

        var destination = sandbox.Resolve(Path.Combine(Path.GetDirectoryName(source)!, newName));
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new ToolArgumentException("Something with that name already exists there.");
        return (source, destination);
    }
}

public sealed class MoveItemsTool(FileSandbox sandbox) : ITool
{
    private const int MaxItems = 50;

    public ToolDefinition Definition { get; } = new("move_items",
        "Move files or folders into an existing destination folder. 'paths' are full paths; " +
        "create the destination with create_folder first if needed. The user is asked to approve.",
        """
        {"type":"object","properties":{
          "paths":{"type":"array","items":{"type":"string"}},
          "destination":{"type":"string","description":"Full path of the destination folder"}
        },"required":["paths","destination"]}
        """);

    public ToolRisk Risk => ToolRisk.Changes;

    public ToolPreview Preview(JsonElement arguments)
    {
        var (sources, destination) = Plan(arguments);
        return new ToolPreview("tool.move", sources, destination);
    }

    public Task<ToolOutcome> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var (sources, destination) = Plan(arguments);
        var moved = new List<string>();
        var skipped = new List<object>();

        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, Path.GetFileName(source));
            if (File.Exists(target) || Directory.Exists(target))
            {
                skipped.Add(new { path = source, reason = "a file with the same name exists in the destination" });
                continue;
            }

            if (Directory.Exists(source))
                Directory.Move(source, target);
            else
                File.Move(source, target);
            moved.Add(target);
        }

        return Task.FromResult(new ToolOutcome(true,
            FileTools.Json(new { destination, moved = moved.Count, skipped }), [destination, .. moved.Take(6)]));
    }

    private (List<string> Sources, string Destination) Plan(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Array)
            throw new ToolArgumentException("Missing 'paths'.");

        var sources = paths.EnumerateArray()
            .Select(p => sandbox.Resolve(p.GetString()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (sources.Count is 0 or > MaxItems)
            throw new ToolArgumentException($"Move between 1 and {MaxItems} items at a time.");

        var destination = sandbox.Resolve(FileTools.String(arguments, "destination"));
        if (!Directory.Exists(destination))
            throw new ToolArgumentException("The destination folder does not exist. Create it first.");

        foreach (var source in sources)
        {
            if (!File.Exists(source) && !Directory.Exists(source))
                throw new ToolArgumentException($"'{source}' does not exist.");
            if (sandbox.IsRoot(source))
                throw new ToolArgumentException("The main user folders cannot be moved.");
            if (destination.Equals(source, StringComparison.OrdinalIgnoreCase)
                || destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ToolArgumentException("A folder cannot be moved inside itself.");
        }

        return (sources, destination);
    }
}
