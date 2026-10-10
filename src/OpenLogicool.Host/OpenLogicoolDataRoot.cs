using System.IO;
using System.Security.Cryptography;

namespace OpenLogicool.Host;

/// <summary>
/// 製品のデータ保存先。AppData配下は、起動元がパッケージ化されたアプリの時にアプリごとの別の場所へ
/// 振り分けられる。どこから起動しても同じ場所になるよう、利用者のホーム直下に置く。
/// </summary>
public static class OpenLogicoolDataRoot
{
    public static string Location { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".openlogicool");

    public static string DatabasePath => Combine("input-studio.db");

    public static string Combine(params string[] parts) => Path.Combine([Location, .. parts]);
}

internal sealed record DataImportResult(string From, string To, int Files, long Bytes, string? DatabaseSha256);

/// <summary>以前の保存先のデータを、現在の保存先へ写す。写すだけで、元のデータは変更しない。</summary>
internal static class HostDataImport
{
    public static DataImportResult Import(string from, string to)
    {
        from = Path.GetFullPath(from);
        to = Path.GetFullPath(to);
        if (!Directory.Exists(from)) throw new DirectoryNotFoundException($"取り込み元がありません: {from}");
        if (string.Equals(from.TrimEnd('\\', '/'), to.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("取り込み元と保存先が同じ場所です。");
        var files = Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories)
            .Select(source => (Source: source, Relative: Path.GetRelativePath(from, source))).ToArray();
        if (files.Length == 0) throw new InvalidOperationException($"取り込み元にファイルがありません: {from}");
        // 既にあるデータを上書きしない。1つでも重なれば、何も写さずに止める。
        var existing = files.Where(file => File.Exists(Path.Combine(to, file.Relative))).Select(file => file.Relative).Take(5).ToArray();
        if (existing.Length > 0)
            throw new IOException("保存先に同じ名前のファイルがあるため、取り込みを止めました。上書きしません: " + string.Join(", ", existing));
        long bytes = 0;
        foreach (var file in files)
        {
            var target = Path.Combine(to, file.Relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file.Source, target, overwrite: false);
            var length = new FileInfo(file.Source).Length;
            if (new FileInfo(target).Length != length)
                throw new IOException($"写したファイルの大きさが元と一致しません: {file.Relative}");
            bytes += length;
        }
        var database = Path.Combine(from, "input-studio.db");
        string? hash = null;
        if (File.Exists(database))
        {
            hash = Sha256(database);
            if (Sha256(Path.Combine(to, "input-studio.db")) != hash)
                throw new IOException("写した設定DBの内容が元と一致しません。");
        }
        return new(from, to, files.Length, bytes, hash);
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
