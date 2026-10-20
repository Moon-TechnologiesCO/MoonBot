using System.Text.Json;
using MoonBot.Core.Tools;
using MoonBot.Infrastructure.Tools;

namespace MoonBot.Infrastructure.Tests;

// Cada test crea su propia carpeta temporal y la usa como "documents" y "downloads".
public sealed class FileToolsTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "moonbot-sandbox-" + Guid.NewGuid().ToString("N"));
    private readonly string _docs;
    private readonly string _downloads;
    private readonly FileSandbox _sandbox;

    public FileToolsTests()
    {
        _docs = Directory.CreateDirectory(Path.Combine(_home, "Documents")).FullName;
        _downloads = Directory.CreateDirectory(Path.Combine(_home, "Downloads")).FullName;
        _sandbox = new FileSandbox(new Dictionary<string, string> { ["documents"] = _docs, ["downloads"] = _downloads });
    }

    public void Dispose() => Directory.Delete(_home, recursive: true);

    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private string Touch(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(_home, relative)); // normaliza "/" -> "\"
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    // ---------- Sandbox ----------

    [Theory]
    [InlineData("documents/../../escape.txt")]
    [InlineData("C:\\Windows\\System32\\drivers\\etc\\hosts")]
    [InlineData("system/thing")]
    [InlineData("")]
    public void Sandbox_RejectsAnythingOutsideTheUserFolders(string path)
    {
        Assert.Throws<ToolArgumentException>(() => _sandbox.Resolve(path));
    }

    [Fact]
    public void Sandbox_RejectsAPrefixTrick()
    {
        // "Documents-evil" empieza igual que "Documents" pero NO está dentro.
        var evil = Directory.CreateDirectory(_docs + "-evil").FullName;

        Assert.Throws<ToolArgumentException>(() => _sandbox.Resolve(Path.Combine(evil, "a.txt")));
    }

    [Fact]
    public void Sandbox_AcceptsAliasesAndFullPaths()
    {
        Assert.Equal(Path.Combine(_docs, "Invoices"), _sandbox.Resolve("documents/Invoices"));
        Assert.Equal(Path.Combine(_docs, "Invoices"), _sandbox.Resolve(Path.Combine(_docs, "Invoices")));
    }

    // ---------- Buscar ----------

    [Fact]
    public async Task Search_FindsByPartialName_IgnoringCaseAndAccents()
    {
        var file = Touch("Documents/Trabajo/Presupuesto Año 2026.xlsx");
        Touch("Documents/otra cosa.txt");
        var tool = new SearchFilesTool(_sandbox);

        var outcome = await tool.ExecuteAsync(Args(new { query = "presupuesto ano" }), CancellationToken.None);

        Assert.True(outcome.Ok);
        Assert.Equal([file], outcome.Paths);
        Assert.Contains("Presupuesto A", outcome.Json);
    }

    [Fact]
    public async Task Search_CanBeLimitedToOneFolder()
    {
        Touch("Documents/report.pdf");
        var inDownloads = Touch("Downloads/report.pdf");

        var outcome = await new SearchFilesTool(_sandbox)
            .ExecuteAsync(Args(new { query = "report", folder = "downloads" }), CancellationToken.None);

        Assert.Equal([inDownloads], outcome.Paths);
    }

    // ---------- Crear carpeta ----------

    [Fact]
    public async Task CreateFolder_CreatesIt()
    {
        var tool = new CreateFolderTool(_sandbox);

        await tool.ExecuteAsync(Args(new { path = "documents/Facturas 2026" }), CancellationToken.None);

        Assert.True(Directory.Exists(Path.Combine(_docs, "Facturas 2026")));
        Assert.Equal(ToolRisk.Changes, tool.Risk);
    }

    [Fact]
    public void CreateFolder_RejectsInvalidNames()
    {
        Assert.Throws<ToolArgumentException>(() =>
            new CreateFolderTool(_sandbox).Preview(Args(new { path = "documents/bad|name" })));
    }

    // ---------- Renombrar ----------

    [Fact]
    public async Task Rename_RenamesInPlace()
    {
        var file = Touch("Documents/old.txt");

        await new RenameItemTool(_sandbox).ExecuteAsync(Args(new { path = file, new_name = "new.txt" }), CancellationToken.None);

        Assert.False(File.Exists(file));
        Assert.True(File.Exists(Path.Combine(_docs, "new.txt")));
    }

    [Fact]
    public void Rename_RefusesToTurnAFileIntoAnExecutable()
    {
        var file = Touch("Documents/photo.jpg");

        Assert.Throws<ToolArgumentException>(() =>
            new RenameItemTool(_sandbox).Preview(Args(new { path = file, new_name = "photo.exe" })));
    }

    [Fact]
    public void Rename_RefusesToOverwrite()
    {
        var file = Touch("Documents/a.txt");
        Touch("Documents/b.txt");

        Assert.Throws<ToolArgumentException>(() =>
            new RenameItemTool(_sandbox).Preview(Args(new { path = file, new_name = "b.txt" })));
    }

    [Fact]
    public void Rename_RefusesToSmuggleAPathInTheName()
    {
        var file = Touch("Documents/a.txt");

        Assert.Throws<ToolArgumentException>(() =>
            new RenameItemTool(_sandbox).Preview(Args(new { path = file, new_name = "..\\..\\a.txt" })));
    }

    [Fact]
    public void Rename_RefusesTheUserFoldersThemselves()
    {
        Assert.Throws<ToolArgumentException>(() =>
            new RenameItemTool(_sandbox).Preview(Args(new { path = _docs, new_name = "Docs2" })));
    }

    // ---------- Mover ----------

    [Fact]
    public async Task Move_MovesSeveralFiles_AndSkipsNameClashes()
    {
        var a = Touch("Downloads/a.pdf");
        var b = Touch("Downloads/b.pdf");
        var target = Directory.CreateDirectory(Path.Combine(_docs, "Facturas")).FullName;
        Touch("Documents/Facturas/b.pdf");

        var outcome = await new MoveItemsTool(_sandbox)
            .ExecuteAsync(Args(new { paths = new[] { a, b }, destination = target }), CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(target, "a.pdf")));
        Assert.True(File.Exists(b)); // no se sobrescribió: se reporta como omitido
        Assert.Contains("\"moved\":1", outcome.Json);
    }

    [Fact]
    public void Move_RefusesADestinationOutsideTheSandbox()
    {
        var a = Touch("Downloads/a.pdf");

        Assert.Throws<ToolArgumentException>(() => new MoveItemsTool(_sandbox)
            .Preview(Args(new { paths = new[] { a }, destination = Path.GetTempPath() })));
    }

    [Fact]
    public void Move_RefusesMovingAFolderIntoItself()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_docs, "Parent")).FullName;
        var child = Directory.CreateDirectory(Path.Combine(folder, "Child")).FullName;

        Assert.Throws<ToolArgumentException>(() => new MoveItemsTool(_sandbox)
            .Preview(Args(new { paths = new[] { folder }, destination = child })));
    }

    [Fact]
    public void ThereIsNoDeleteTool()
    {
        Assert.DoesNotContain(FileTools.All(_sandbox), t => t.Definition.Name.Contains("delete"));
    }
}
