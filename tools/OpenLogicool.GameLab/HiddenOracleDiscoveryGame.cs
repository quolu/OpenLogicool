using System.Security.Cryptography;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.GameLab;

public sealed record GameLabVisualFrame(
    string SourceId,
    long Sequence,
    long TransformRevision,
    long FreshnessMilliseconds,
    CaptureAvailability Availability,
    int Width,
    int Height,
    IReadOnlyList<byte> GrayPixels);

public interface IGameLabVisualSurface
{
    GameLabVisualFrame Capture();

    void Click(double normalizedX, double normalizedY);
}

public sealed record HiddenOracleAudit(
    IReadOnlyList<string> VisitedStateIds,
    int AcceptedClicks,
    int NoChangeClicks,
    int TransitionClicks);

/// <summary>
/// oracle graphを内部に隠し、runtimeへpixel frameとgeneric clickだけを公開する決定的GameLab。
/// </summary>
public sealed class HiddenOracleDiscoveryGame : IGameLabVisualSurface
{
    private const int Width = 16;
    private const int Height = 12;
    private readonly List<string> visited = ["oracle-alpha"];
    private OracleState state = OracleState.Alpha;
    private long sequence;
    private bool crashAfterNextClick;
    private CaptureAvailability availability = CaptureAvailability.Available;
    private long freshnessMilliseconds = 10;
    private int acceptedClicks;
    private int noChangeClicks;
    private int transitionClicks;

    public GameLabVisualFrame Capture()
    {
        sequence++;
        return new GameLabVisualFrame(
            "window:hidden-oracle",
            sequence,
            1,
            freshnessMilliseconds,
            availability,
            Width,
            Height,
            Render());
    }

    public void Click(double normalizedX, double normalizedY)
    {
        var x = (int)Math.Floor(normalizedX * Width);
        var y = (int)Math.Floor(normalizedY * Height);
        var before = state;
        state = state switch
        {
            OracleState.Alpha when Contains(x, y, 2, 3, 4, 3) => OracleState.Beta,
            OracleState.Alpha when Contains(x, y, 10, 3, 4, 3) => OracleState.Alpha,
            OracleState.Beta when Contains(x, y, 6, 6, 4, 3) => OracleState.Gamma,
            OracleState.Gamma when Contains(x, y, 1, 8, 4, 3) => OracleState.Alpha,
            _ => state,
        };

        acceptedClicks++;
        if (state == before)
        {
            noChangeClicks++;
        }
        else
        {
            transitionClicks++;
            visited.Add(OracleId(state));
        }

        if (crashAfterNextClick)
        {
            crashAfterNextClick = false;
            throw new InvalidOperationException("hidden-oracle crash after input boundary");
        }
    }

    public void ArmCrashAfterNextClick() => crashAfterNextClick = true;

    public void SetCaptureAvailability(CaptureAvailability value) => availability = value;

    public void SetFreshness(long milliseconds) => freshnessMilliseconds = milliseconds;

    public HiddenOracleAudit ReadOracleAudit() =>
        new(visited.ToArray(), acceptedClicks, noChangeClicks, transitionClicks);

    private byte[] Render()
    {
        var pixels = Enumerable.Repeat(state switch
        {
            OracleState.Alpha => (byte)20,
            OracleState.Beta => (byte)60,
            OracleState.Gamma => (byte)100,
            _ => throw new ArgumentOutOfRangeException(),
        }, Width * Height).ToArray();

        switch (state)
        {
            case OracleState.Alpha:
                Draw(pixels, 2, 3, 4, 3);
                Draw(pixels, 10, 3, 4, 3);
                break;
            case OracleState.Beta:
                Draw(pixels, 6, 6, 4, 3);
                break;
            case OracleState.Gamma:
                Draw(pixels, 1, 8, 4, 3);
                break;
        }
        return pixels;
    }

    private static void Draw(byte[] pixels, int x, int y, int width, int height)
    {
        for (var row = y; row < y + height; row++)
        {
            for (var column = x; column < x + width; column++)
            {
                pixels[(row * Width) + column] = 240;
            }
        }
    }

    private static bool Contains(int x, int y, int left, int top, int width, int height) =>
        x >= left && x < left + width && y >= top && y < top + height;

    private static string OracleId(OracleState value) => value switch
    {
        OracleState.Alpha => "oracle-alpha",
        OracleState.Beta => "oracle-beta",
        OracleState.Gamma => "oracle-gamma",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private enum OracleState
    {
        Alpha,
        Beta,
        Gamma,
    }
}

/// <summary>
/// game固有state／targetを知らず、pixel signatureと明るい連結領域だけからsceneを作る。
/// </summary>
public sealed class ZeroSeedFrameRecognizer
{
    public ObservedScene Observe(GameLabVisualFrame frame, string observationId)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentException.ThrowIfNullOrWhiteSpace(observationId);
        if (frame.GrayPixels.Count != frame.Width * frame.Height)
        {
            throw new ArgumentException("frame pixel countが寸法と一致しません。", nameof(frame));
        }

        var signature = Convert.ToHexString(SHA256.HashData(frame.GrayPixels.ToArray())).ToLowerInvariant();
        var frameReference = new CapturedFrameReference(
            ContractSchemaVersions.Revision03,
            frame.SourceId,
            CaptureBackend.WindowsGraphicsCapture,
            frame.Sequence,
            frame.Sequence * 16,
            DateTimeOffset.UnixEpoch.AddMilliseconds(frame.Sequence * 16),
            frame.TransformRevision,
            frame.FreshnessMilliseconds,
            frame.Sequence * 16);
        var affordances = frame.Availability == CaptureAvailability.Available
            ? FindComponents(frame, observationId, signature)
            : [];
        return new ObservedScene(
            ContractSchemaVersions.Revision03,
            $"scene:{signature}",
            observationId,
            frameReference,
            frame.Availability,
            frame.Availability == CaptureAvailability.Available
                ? StateIdentityStatus.Novel
                : StateIdentityStatus.InsufficientEvidence,
            frame.Availability == CaptureAvailability.Available ? $"visual:{signature}" : null,
            [],
            affordances,
            "zero-seed-components-v1");
    }

    private static IReadOnlyList<AffordanceCandidate> FindComponents(
        GameLabVisualFrame frame,
        string observationId,
        string signature)
    {
        var visited = new bool[frame.GrayPixels.Count];
        var candidates = new List<AffordanceCandidate>();
        for (var index = 0; index < frame.GrayPixels.Count; index++)
        {
            if (visited[index] || frame.GrayPixels[index] < 220)
            {
                continue;
            }

            var points = Flood(frame, index, visited);
            var minX = points.Min(point => point.X);
            var minY = points.Min(point => point.Y);
            var maxX = points.Max(point => point.X);
            var maxY = points.Max(point => point.Y);
            var bounds = new[]
            {
                minX / (double)frame.Width,
                minY / (double)frame.Height,
                (maxX - minX + 1) / (double)frame.Width,
                (maxY - minY + 1) / (double)frame.Height,
            };
            var locatorRevision = $"component:{minX}:{minY}:{maxX}:{maxY}";
            var candidateId = $"affordance:{signature[..12]}:{minX}:{minY}:{maxX}:{maxY}";
            var region = new EvidenceRegion(
                ContractSchemaVersions.Revision03,
                "rect",
                bounds,
                "pixel-component-v1");
            candidates.Add(new AffordanceCandidate(
                ContractSchemaVersions.Revision03,
                candidateId,
                observationId,
                frame.Sequence,
                frame.TransformRevision,
                frame.SourceId,
                new AffordanceLocator(ContractSchemaVersions.Revision03, "component", bounds, locatorRevision),
                [region],
                1,
                ["click"]));
        }
        return candidates.OrderBy(candidate => candidate.Locator.NormalizedBounds[0]).ToArray();
    }

    private static IReadOnlyList<(int X, int Y)> Flood(GameLabVisualFrame frame, int start, bool[] visited)
    {
        var points = new List<(int X, int Y)>();
        var queue = new Queue<int>();
        queue.Enqueue(start);
        visited[start] = true;
        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            var x = index % frame.Width;
            var y = index / frame.Width;
            points.Add((x, y));
            foreach (var (nextX, nextY) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
            {
                if (nextX < 0 || nextY < 0 || nextX >= frame.Width || nextY >= frame.Height)
                {
                    continue;
                }
                var next = (nextY * frame.Width) + nextX;
                if (!visited[next] && frame.GrayPixels[next] >= 220)
                {
                    visited[next] = true;
                    queue.Enqueue(next);
                }
            }
        }
        return points;
    }
}
