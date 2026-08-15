using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenLogicool.CaptureProbe;

internal static class ProbeOutput
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // bin/<Config>/<TFM>/ から repo root/probe-output へ（既存 OpenLogicool.Probe と同じ 5 段上り）。
    public static string OutputDirectory => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "probe-output"));

    public static string NewFileBase(string command) =>
        $"capture-{command}-{DateTime.Now:yyyyMMdd-HHmmss}";

    public static CaptureResult NewResult(string probe, string backend) => new()
    {
        Probe = probe,
        CapturedAtUtc = DateTime.UtcNow.ToString("O"),
        Machine = Environment.MachineName,
        Backend = backend,
    };

    public static FrameRecord FailedFrame(int sequence, double monotonicMs, Exception ex) => new()
    {
        Sequence = sequence,
        MonotonicMs = monotonicMs,
        Error = ErrorRecord.FromException(ex),
    };

    public static int WriteAndReport(CaptureResult result, string fileBase)
    {
        Directory.CreateDirectory(OutputDirectory);
        var jsonPath = Path.Combine(OutputDirectory, $"{fileBase}.json");
        var json = JsonSerializer.Serialize(result, JsonOptions);
        File.WriteAllText(jsonPath, json);
        Console.WriteLine(json);
        Console.WriteLine($"[capture-probe] wrote {jsonPath}");
        // frame 単位の失敗も非 0 で外へ出す（JSON にだけ残して成功扱いにしない）。
        return result.Error is not null || result.Frames.Any(f => f.Error is not null) ? 2 : 0;
    }
}
