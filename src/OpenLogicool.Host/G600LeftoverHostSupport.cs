using System.IO;
using OpenLogicool.Devices.G600;

namespace OpenLogicool.Host;

/// <summary>残置 session の Host 側組み立てと表示。判断本体は Devices.G600 の policy。</summary>
public static class G600LeftoverHostSupport
{
    public static G600LegacySuppressionMode SuppressionModeFor(ResidentOutputRoute route) => route switch
    {
        ResidentOutputRoute.SendInput => G600LegacySuppressionMode.IntermediateUsage,
        ResidentOutputRoute.SerialHid => G600LegacySuppressionMode.NoOutput,
        _ => throw new ArgumentOutOfRangeException(nameof(route), route, "unknown resident output route"),
    };

    public static bool IsCoexistenceRunning() =>
        OnboardingObservationCollector.DetectCoexistingSoftware().Any(software => software.Detected);

    public static G600LeftoverSession CreateSession(string databasePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException($"database path has no directory: {databasePath}");
        }

        return G600LeftoverSession.CreateDefault(directory, IsCoexistenceRunning);
    }

    public static string Describe(G600LeftoverResult result)
    {
        var write = result.Wrote
            ? result.ByteMatched
                ? $"write 成立（attempt {result.Attempts}）"
                : $"write 不一致（attempt {result.Attempts}）"
            : "write なし";
        var open = result.OpenError is null ? string.Empty : $" / {result.OpenError}";
        return $"leftover {result.Kind}: {result.Reason} [{write}{open}]";
    }
}
