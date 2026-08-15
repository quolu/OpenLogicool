namespace OpenLogicool.CaptureProbe;

// Phase 0 capture probe の共通 JSON モデル。
// backend 間のフォールバックはしない: 各コマンドは自分の backend だけを試し、
// 失敗は ErrorRecord として verbatim に記録する。

internal sealed class CaptureResult
{
    public required string Probe { get; init; }
    public required string CapturedAtUtc { get; init; }
    public required string Machine { get; init; }
    public required string Backend { get; init; }
    public object? Target { get; set; }
    public List<FrameRecord> Frames { get; } = [];
    public ErrorRecord? Error { get; set; }
}

internal sealed class FrameRecord
{
    public required int Sequence { get; init; }
    public required double MonotonicMs { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? PixelFormat { get; init; }
    public double? AverageLuminance { get; init; }
    public string? PngFile { get; init; }
    public ErrorRecord? Error { get; init; }
}

internal sealed class ErrorRecord
{
    public required string Type { get; init; }
    public required string Message { get; init; }

    public static ErrorRecord FromException(Exception ex) => new()
    {
        Type = ex.GetType().FullName ?? ex.GetType().Name,
        Message = ex.Message,
    };
}
