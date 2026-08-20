using OpenLogicool.Contracts.Capture;

namespace OpenLogicool.Capture;

/// <summary>CAP-004/005 の根拠付き matrix。選択は必ず指定 backend の行だけを返し、fallback はしない。</summary>
public sealed class CaptureCapabilityMatrix
{
    private readonly IReadOnlyDictionary<(CaptureBackend Backend, CaptureTargetKind Target, CaptureCondition Condition), CaptureCapability> entries;

    public CaptureCapabilityMatrix(IEnumerable<CaptureCapability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        entries = capabilities.ToDictionary(
            capability => (capability.Backend, capability.Target, capability.Condition));
    }

    public static CaptureCapabilityMatrix CreateReferenceMatrix() => new(ReferenceCapabilities);

    public IReadOnlyCollection<CaptureCapability> Capabilities => entries.Values.ToArray();

    public CaptureBackendDecision Select(
        CaptureBackend backend,
        CaptureTargetKind target,
        CaptureCondition condition)
    {
        if (!entries.TryGetValue((backend, target, condition), out var capability))
        {
            return new CaptureBackendDecision(
                backend,
                target,
                condition,
                CaptureEvidenceLevel.Unverified,
                CaptureRouteAvailability.Unavailable,
                "この backend と条件の support matrix 行は未登録です。");
        }

        return new CaptureBackendDecision(
            capability.Backend,
            capability.Target,
            capability.Condition,
            capability.Evidence,
            capability.RouteAvailability,
            capability.Reason);
    }

    private static readonly CaptureCapability[] ReferenceCapabilities =
    [
        new(CaptureBackend.WindowsGraphicsCapture, CaptureTargetKind.Window, CaptureCondition.Windowed,
            CaptureEvidenceLevel.Confirmed, CaptureRouteAvailability.Available,
            "Windows 11 reference machine のメモ帳 window で WGC frame を確認済みです。"),
        new(CaptureBackend.WindowsGraphicsCapture, CaptureTargetKind.Window, CaptureCondition.Minimized,
            CaptureEvidenceLevel.Unsupported, CaptureRouteAvailability.Unavailable,
            "最小化 window は item 有効でも frame 供給が停止するため、restore まで capture しません。"),
        new(CaptureBackend.WindowsGraphicsCapture, CaptureTargetKind.Window, CaptureCondition.Borderless,
            CaptureEvidenceLevel.Unverified, CaptureRouteAvailability.Unavailable,
            "borderless window は未実測です。"),
        new(CaptureBackend.WindowsGraphicsCapture, CaptureTargetKind.Window, CaptureCondition.Fullscreen,
            CaptureEvidenceLevel.Unverified, CaptureRouteAvailability.Unavailable,
            "fullscreen window は未実測です。"),
        new(CaptureBackend.WindowsGraphicsCapture, CaptureTargetKind.Window, CaptureCondition.NonDefaultDpi,
            CaptureEvidenceLevel.Unverified, CaptureRouteAvailability.Unavailable,
            "DPI 変更中の window capture は未実測です。"),
        new(CaptureBackend.WindowsGraphicsCapture, CaptureTargetKind.Window, CaptureCondition.Hdr,
            CaptureEvidenceLevel.Unverified, CaptureRouteAvailability.Unavailable,
            "HDR window capture は未実測です。"),
        new(CaptureBackend.WindowsGraphicsCapture, CaptureTargetKind.Window, CaptureCondition.MultipleMonitors,
            CaptureEvidenceLevel.Unverified, CaptureRouteAvailability.Unavailable,
            "multi-monitor 跨ぎ window capture は未実測です。"),
        new(CaptureBackend.WindowsGraphicsCapture, CaptureTargetKind.Window, CaptureCondition.Occluded,
            CaptureEvidenceLevel.Unverified, CaptureRouteAvailability.Unavailable,
            "遮蔽された window capture は未実測です。"),
        new(CaptureBackend.DesktopDuplication, CaptureTargetKind.Display, CaptureCondition.Windowed,
            CaptureEvidenceLevel.Confirmed, CaptureRouteAvailability.ProbedOnly,
            "reference display の probe は確認済みですが、製品 backend 化は t03 の採否待ちです。"),
        new(CaptureBackend.GdiBitBlt, CaptureTargetKind.Display, CaptureCondition.Windowed,
            CaptureEvidenceLevel.Confirmed, CaptureRouteAvailability.ProbedOnly,
            "virtual desktop の probe は確認済みですが、製品 backend 化は t03 の採否待ちです。"),
    ];
}
