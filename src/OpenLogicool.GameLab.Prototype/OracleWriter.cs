using System.IO;
using System.Text.Json;

namespace OpenLogicool.GameLab.Prototype;

/// <summary>
/// oracle JSONL の書出し。GameStateMachine から独立させ、失敗を握りつぶさない
/// （File I/O の例外はそのまま呼び出し元へ伝播する）。
/// </summary>
public static class OracleWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // bin/<Config>/<TFM>/ から repo root/probe-output へ（既存 probe と同じ 5 段上り。
    // src/OpenLogicool.CaptureProbe/ProbeOutput.cs を参照。プロジェクト間参照は張らずコピー）。
    public static string OutputDirectory => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "probe-output"));

    public static string NewFilePath(int seed)
    {
        Directory.CreateDirectory(OutputDirectory);
        var fileName = $"gamelab-oracle-{seed}-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl";
        return Path.Combine(OutputDirectory, fileName);
    }

    public static void Append(string filePath, OracleEntry entry)
    {
        var line = JsonSerializer.Serialize(entry, JsonOptions);
        File.AppendAllText(filePath, line + Environment.NewLine);
    }

    public static void AppendAll(string filePath, IEnumerable<OracleEntry> entries)
    {
        foreach (var entry in entries)
        {
            Append(filePath, entry);
        }
    }
}
