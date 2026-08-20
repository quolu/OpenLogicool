namespace OpenLogicool.Contracts.Capture;

public enum CaptureTargetKind
{
    Window,
    Display,
}

public enum CaptureCondition
{
    Windowed,
    Borderless,
    Fullscreen,
    NonDefaultDpi,
    Hdr,
    MultipleMonitors,
    Occluded,
    Minimized,
}

public enum CaptureEvidenceLevel
{
    Confirmed,
    StrongInference,
    Unverified,
    Unsupported,
}

public enum CaptureRouteAvailability
{
    Available,
    ProbedOnly,
    Unavailable,
}

public sealed record CaptureCapability(
    CaptureBackend Backend,
    CaptureTargetKind Target,
    CaptureCondition Condition,
    CaptureEvidenceLevel Evidence,
    CaptureRouteAvailability RouteAvailability,
    string Reason);

public sealed record CaptureBackendDecision(
    CaptureBackend Backend,
    CaptureTargetKind Target,
    CaptureCondition Condition,
    CaptureEvidenceLevel Evidence,
    CaptureRouteAvailability Availability,
    string Reason)
{
    public bool CanCapture => Availability == CaptureRouteAvailability.Available;
}
