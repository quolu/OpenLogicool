using OpenLogicool.Input;

namespace OpenLogicool.Host;

internal sealed record ResidentInputSourceCandidate(string DeviceKind, FastPathSource Source);

internal static class ResidentInputSourceSelection
{
    public static IReadOnlyList<FastPathSource> Select(
        IReadOnlyList<ResidentInputSourceCandidate> candidates,
        IEnumerable<string> observedDeviceKinds)
    {
        var observed = observedDeviceKinds.ToHashSet(StringComparer.Ordinal);
        return candidates
            .Where(candidate => observed.Contains(candidate.DeviceKind))
            .Select(candidate => candidate.Source)
            .ToArray();
    }
}
