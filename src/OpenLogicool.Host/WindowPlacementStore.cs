using System.IO;
using System.Text.Json;
using OpenLogicool.Desktop;

namespace OpenLogicool.Host;

public sealed record WindowPlacementDocument(string SchemaVersion, Dictionary<string, WindowPlacement> Windows)
{
    public const string CurrentSchemaVersion = "1.0";
}

/// <summary>
/// 窓ごとの位置と大きさを保存するmachine-local設定。DBの隣へ置き、起動元アプリで保存先が分かれないようにする。
/// </summary>
public sealed class WindowPlacementStore
{
    public const string FileName = "window-placement.json";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string path;

    public WindowPlacementStore(string directory, string fileName = FileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        path = Path.Combine(directory, fileName);
    }

    public static WindowPlacementStore ForDatabase(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        return new(
            Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("database directoryがありません。"),
            $"{Path.GetFileName(fullPath)}.{FileName}");
    }

    public WindowPlacement? Load(string windowKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowKey);
        return LoadDocument().Windows.GetValueOrDefault(windowKey);
    }

    public void Save(string windowKey, WindowPlacement placement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowKey);
        ArgumentNullException.ThrowIfNull(placement);
        var document = LoadDocument();
        document.Windows[windowKey] = placement;
        File.WriteAllText(path, JsonSerializer.Serialize(document, Json));
    }

    public WindowPlacementMemory ToMemory() => new(Load, Save);

    private WindowPlacementDocument LoadDocument()
    {
        if (!File.Exists(path))
        {
            return new(WindowPlacementDocument.CurrentSchemaVersion, new(StringComparer.Ordinal));
        }

        var document = JsonSerializer.Deserialize<WindowPlacementDocument>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("window placementがnullです。");
        if (document.SchemaVersion != WindowPlacementDocument.CurrentSchemaVersion || document.Windows is null)
        {
            throw new InvalidDataException("window placementが不正です。");
        }

        return document;
    }
}
