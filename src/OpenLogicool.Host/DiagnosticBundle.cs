using System.IO;
using System.Text.Json;

namespace OpenLogicool.Host;

/// <summary>既定 diagnostic bundle の利用者向け preview。</summary>
public sealed class DiagnosticBundlePreview
{
    internal DiagnosticBundlePreview(string bundlePath, string manifestJson)
    {
        BundlePath = bundlePath;
        ManifestJson = manifestJson;
    }

    /// <summary>生成時に書く bundle ファイルの絶対パス。</summary>
    public string BundlePath { get; }

    /// <summary>生成前に確認できる、bundle に書く manifest 本文。</summary>
    public string ManifestJson { get; }
}

/// <summary>
/// 既定 diagnostic bundle の最小実装（NFR-011）。
/// 既存 diagnostics CLI の状態収集は再実装せず、既定 bundle には固定 manifest だけを入れる。
/// そのため画面、OCR、prompt、journal、crash dump、secret、個人データを探索・収集・保存しない。
/// </summary>
public static class DiagnosticBundle
{
    private const string ProductName = "OpenLogicool";
    private const string SchemaVersion = "1.0";

    /// <summary>filesystem へ書かず、生成予定の manifest を preview する。</summary>
    public static DiagnosticBundlePreview Preview(string destinationDirectory, DateTimeOffset generatedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        var directory = Path.GetFullPath(destinationDirectory);
        var fileName = $"openlogicool-diagnostic-{generatedAtUtc.UtcDateTime:yyyyMMdd-HHmmss}.json";
        var manifest = new DiagnosticBundleManifest(
            SchemaVersion,
            ProductName,
            generatedAtUtc.ToUniversalTime().ToString("O"),
            Included: ["bundle schema", "product", "generated time"],
            Excluded: ["screen", "OCR", "prompt", "journal body", "crash dump", "secret", "personal data"]);
        var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });

        return new(Path.Combine(directory, fileName), manifestJson);
    }

    /// <summary>preview で確認した manifest だけをローカルへ書く。</summary>
    public static void Create(DiagnosticBundlePreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        Directory.CreateDirectory(Path.GetDirectoryName(preview.BundlePath)!);
        File.WriteAllText(preview.BundlePath, preview.ManifestJson);
    }

    /// <summary>この API が preview した bundle 1件だけを削除する。</summary>
    public static void Delete(DiagnosticBundlePreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        if (File.Exists(preview.BundlePath))
        {
            File.Delete(preview.BundlePath);
        }
    }

    private sealed record DiagnosticBundleManifest(
        string SchemaVersion,
        string Product,
        string GeneratedAtUtc,
        IReadOnlyList<string> Included,
        IReadOnlyList<string> Excluded);
}
