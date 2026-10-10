using System.IO;
using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class OpenLogicoolDataRootTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "openlogicool-data-root-tests", Guid.NewGuid().ToString("N"));
    private string From => Path.Combine(root, "from");
    private string To => Path.Combine(root, "to");

    [Fact]
    public void 保存先は起動元のアプリで場所が分かれるAppDataの外に置く()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(Path.Combine(home, ".openlogicool"), OpenLogicoolDataRoot.Location);
        Assert.Equal(Path.Combine(home, ".openlogicool", "input-studio.db"), OpenLogicoolDataRoot.DatabasePath);
        Assert.Equal(Path.Combine(home, ".openlogicool", "bot-runs", "a"), OpenLogicoolDataRoot.Combine("bot-runs", "a"));
        foreach (var redirected in new[] { Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData })
            Assert.False(OpenLogicoolDataRoot.Location.StartsWith(Environment.GetFolderPath(redirected), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void 以前の保存先を階層ごと写し元のデータは変更しない()
    {
        Write(From, "input-studio.db", "設定DB");
        Write(From, Path.Combine("bot-runs", "mabinogi-mobile", "run-1", "events.jsonl"), "{}");
        Write(From, Path.Combine("input-studio.db.bot-assistance", "state.json"), "{\"schemaVersion\":1}");
        var result = HostDataImport.Import(From, To);
        Assert.Equal(3, result.Files);
        Assert.NotNull(result.DatabaseSha256);
        Assert.Equal("設定DB", File.ReadAllText(Path.Combine(To, "input-studio.db")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(To, "bot-runs", "mabinogi-mobile", "run-1", "events.jsonl")));
        Assert.Equal("{\"schemaVersion\":1}", File.ReadAllText(Path.Combine(To, "input-studio.db.bot-assistance", "state.json")));
        Assert.Equal("設定DB", File.ReadAllText(Path.Combine(From, "input-studio.db")));
        Assert.Equal(3, Directory.EnumerateFiles(From, "*", SearchOption.AllDirectories).Count());
    }

    [Fact]
    public void 保存先に同じ名前のファイルがあれば上書きせず何も写さずに止まる()
    {
        Write(From, "input-studio.db", "以前の設定");
        Write(From, Path.Combine("bot-runs", "events.jsonl"), "{}");
        Write(To, "input-studio.db", "現在の設定");
        Assert.Throws<IOException>(() => HostDataImport.Import(From, To));
        Assert.Equal("現在の設定", File.ReadAllText(Path.Combine(To, "input-studio.db")));
        Assert.False(File.Exists(Path.Combine(To, "bot-runs", "events.jsonl")));
    }

    [Fact]
    public void 取り込み元が無い時と空の時と同じ場所の時は取り込まない()
    {
        Assert.Throws<DirectoryNotFoundException>(() => HostDataImport.Import(From, To));
        Directory.CreateDirectory(From);
        Assert.Throws<InvalidOperationException>(() => HostDataImport.Import(From, To));
        Write(From, "input-studio.db", "設定DB");
        Assert.Throws<ArgumentException>(() => HostDataImport.Import(From, From + Path.DirectorySeparatorChar));
        Assert.False(Directory.Exists(To));
    }

    private static void Write(string directory, string relative, string text)
    {
        var path = Path.Combine(directory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
