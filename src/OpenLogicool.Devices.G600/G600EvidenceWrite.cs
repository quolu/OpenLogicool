namespace OpenLogicool.Devices.G600;

/// <summary>
/// G600 feature report の1回分の fresh handle。呼び出し側が close する。
/// </summary>
public interface IG600FeatureHandle : IDisposable
{
    void SetFeature(byte[] report);

    byte[] GetFeature(byte reportId);
}

/// <summary>
/// 毎回 fresh open する feature 入口。同一 handle を write と verify で使い回さない。
/// </summary>
public interface IG600FeatureAccess
{
    bool TryOpen(out IG600FeatureHandle? handle);
}

public sealed record G600EvidenceWriteResult(bool Matched, int Attempts, string? OpenError);

/// <summary>
/// evidence-based write 作法（fresh open・settle・handle 非再利用・fresh verify・一致まで再送）。
/// 正: rag/openlogicool/g600-write-protocol-2026-08-15.md と probe g600-apply-verify。
/// </summary>
public static class G600EvidenceWrite
{
    public const int DefaultSettleMs = 2000;
    public const int DefaultMaxAttempts = 8;
    public const byte ProfileReportIdF3 = 0xF3;

    public static void EnsureWritableProfile(byte[] report)
    {
        if (report.Length != G600SideRemap.ReportLength)
        {
            throw new ArgumentException(
                $"G600 feature report must be {G600SideRemap.ReportLength} bytes.",
                nameof(report));
        }

        if (report[0] is not (0xF0 or 0xF3 or 0xF4 or 0xF5))
        {
            throw new InvalidOperationException($"report ID 0x{report[0]:X2} is not writable.");
        }
    }

    public static G600EvidenceWriteResult TryWrite(
        IG600FeatureAccess access,
        byte[] desired,
        Action<int> sleep,
        int maxAttempts = DefaultMaxAttempts,
        int settleMs = DefaultSettleMs)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(sleep);
        EnsureWritableProfile(desired);

        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        }

        if (settleMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settleMs));
        }

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (!access.TryOpen(out var writeHandle) || writeHandle is null)
            {
                return new(false, attempt, $"could not open G600 for write (attempt {attempt}).");
            }

            using (writeHandle)
            {
                sleep(settleMs);
                writeHandle.SetFeature(desired);
            }

            if (!access.TryOpen(out var verifyHandle) || verifyHandle is null)
            {
                return new(false, attempt, $"write sent but could not reopen for verify (attempt {attempt}).");
            }

            byte[] lastRead;
            using (verifyHandle)
            {
                sleep(settleMs);
                lastRead = verifyHandle.GetFeature(desired[0]);
            }

            if (lastRead.AsSpan().SequenceEqual(desired))
            {
                return new(true, attempt, null);
            }
        }

        return new(false, maxAttempts, null);
    }

    public static byte[]? TryRead(IG600FeatureAccess access, byte reportId, Action<int> sleep, int settleMs = DefaultSettleMs)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(sleep);

        if (!access.TryOpen(out var handle) || handle is null)
        {
            return null;
        }

        using (handle)
        {
            sleep(settleMs);
            return handle.GetFeature(reportId);
        }
    }
}
