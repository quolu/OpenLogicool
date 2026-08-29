using System.IO;
using System.Text.Json;

namespace OpenLogicool.Host;

public sealed record MacroTargetSettings(string SchemaVersion, string ProcessName)
{
    public const string CurrentSchemaVersion = "1.0";
}

/// <summary>マクロの対象game profileをアプリ側に固定するmachine-local設定。</summary>
public sealed class MacroTargetSettingsStore
{
    public const string FileName = "macro-target-settings.json";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string path;

    public MacroTargetSettingsStore(string directory, string fileName = FileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        path = Path.Combine(directory, fileName);
    }

    public static MacroTargetSettingsStore ForDatabase(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        return new(
            Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("database directoryがありません。"),
            $"{Path.GetFileName(fullPath)}.{FileName}");
    }

    public MacroTargetSettings? Load()
    {
        if (!File.Exists(path)) return null;
        var value = JsonSerializer.Deserialize<MacroTargetSettings>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("macro target settingsがnullです。");
        if (value.SchemaVersion != MacroTargetSettings.CurrentSchemaVersion
            || string.IsNullOrWhiteSpace(value.ProcessName))
            throw new InvalidDataException("macro target settingsが不正です。");
        return value;
    }

    public MacroTargetSettings Save(string processName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);
        var value = new MacroTargetSettings(
            MacroTargetSettings.CurrentSchemaVersion,
            NormalizeProcessName(processName));
        File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
        return value;
    }

    /// <summary>
    /// exe path を渡されても process 名として扱えるようにする。
    /// 落とすのは末尾の .exe だけで、process 名に含まれるドット（"Some.Game" 等）は残す。
    /// 拡張子として一律に切ると、ドットを含む process 名が別名になって対象を見失う。
    /// </summary>
    private static string NormalizeProcessName(string processName)
    {
        var trimmed = Path.GetFileName(processName.Trim());
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^4]
            : trimmed;
    }
}
