using System.Globalization;
using System.Text.Json;

namespace OpenLogicool.Probe;

public enum G600WriteCommandKind
{
    Writeback,
    LedApplyRestore,
    Slot,
}

public sealed record G600WriteCommand(
    G600WriteCommandKind Kind,
    string BackupPath,
    int? TargetSlot);

public static class G600WriteCommandParser
{
    public static bool TryParse(
        string commandName,
        IReadOnlyList<string> arguments,
        out G600WriteCommand? command,
        out string error)
    {
        command = null;
        error = string.Empty;

        if (arguments.Any(argument => string.Equals(argument, "0xF6", StringComparison.OrdinalIgnoreCase)))
        {
            error = "report ID 0xF6 is never writable.";
            return false;
        }

        var kind = commandName switch
        {
            "g600-writeback" => G600WriteCommandKind.Writeback,
            "g600-led-apply-restore" => G600WriteCommandKind.LedApplyRestore,
            "g600-slot" => G600WriteCommandKind.Slot,
            _ => throw new ArgumentOutOfRangeException(nameof(commandName), commandName, null),
        };

        string? backupPath = null;
        int? targetSlot = null;

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (argument == "--backup" && index + 1 < arguments.Count)
            {
                backupPath = arguments[++index];
                continue;
            }

            if (argument == "--target" && index + 1 < arguments.Count && kind == G600WriteCommandKind.Slot)
            {
                if (!int.TryParse(arguments[++index], NumberStyles.None, CultureInfo.InvariantCulture, out var parsedTarget)
                    || parsedTarget is < 0 or > 2)
                {
                    error = "--target must be 0, 1, or 2.";
                    return false;
                }

                targetSlot = parsedTarget;
                continue;
            }

            error = $"unsupported argument: {argument}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(backupPath))
        {
            error = "--backup <path> is required.";
            return false;
        }

        if (kind == G600WriteCommandKind.Slot && targetSlot is null)
        {
            error = "g600-slot requires --target <0|1|2>.";
            return false;
        }

        command = new G600WriteCommand(kind, backupPath, targetSlot);
        return true;
    }
}

public sealed record G600BackupReport(string ReportId, int LengthBytes, string? DataHex, string? Error);

public sealed class G600BackupDocument
{
    public required List<G600BackupReport> Reports { get; init; }
}

public sealed class G600BackupSnapshot
{
    private static readonly byte[] RequiredReportIds = [0xF0, 0xF3, 0xF4, 0xF5];

    private G600BackupSnapshot(IReadOnlyDictionary<byte, byte[]> reports) => Reports = reports;

    public IReadOnlyDictionary<byte, byte[]> Reports { get; }

    public static G600BackupSnapshot Load(string path) => Parse(File.ReadAllText(path));

    public static G600BackupSnapshot Parse(string json)
    {
        var document = JsonSerializer.Deserialize<G600BackupDocument>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidDataException("backup JSON is null.");

        var reports = new Dictionary<byte, byte[]>();
        foreach (var report in document.Reports)
        {
            if (report.DataHex is null || report.Error is not null)
            {
                continue;
            }

            if (!TryParseReportId(report.ReportId, out var reportId))
            {
                throw new InvalidDataException($"invalid backup report ID: {report.ReportId}");
            }

            var bytes = Convert.FromHexString(report.DataHex);
            if (report.LengthBytes != bytes.Length || bytes.Length != 154 || bytes[0] != reportId)
            {
                throw new InvalidDataException($"backup report {report.ReportId} is not a 154-byte matching feature report.");
            }

            reports.Add(reportId, bytes);
        }

        foreach (var reportId in RequiredReportIds)
        {
            if (!reports.ContainsKey(reportId))
            {
                throw new InvalidDataException($"backup does not contain report 0x{reportId:X2}.");
            }
        }

        return new G600BackupSnapshot(reports);
    }

    public byte[] GetRequiredReport(byte reportId) =>
        Reports.TryGetValue(reportId, out var report)
            ? report
            : throw new InvalidOperationException($"report 0x{reportId:X2} is not in the backup.");

    private static bool TryParseReportId(string reportId, out byte value)
    {
        var digits = reportId.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? reportId[2..]
            : reportId;
        return byte.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
    }
}

public sealed record G600BackupComparison(
    string ReportId,
    string BackupHex,
    string? CurrentHex,
    bool IsMatch,
    bool DifferenceAllowed);

public sealed record G600BackupPrecondition(
    IReadOnlyList<G600BackupComparison> Comparisons,
    bool IsWriteStartAllowed);

public static class G600BackupVerifier
{
    private static readonly byte[] ComparedReportIds = [0xF0, 0xF3, 0xF4, 0xF5];

    public static G600BackupPrecondition Verify(
        G600BackupSnapshot backup,
        IReadOnlyDictionary<byte, byte[]> currentReports)
    {
        var comparisons = new List<G600BackupComparison>();
        var isWriteStartAllowed = true;

        foreach (var reportId in ComparedReportIds)
        {
            var backupBytes = backup.GetRequiredReport(reportId);
            var hasCurrent = currentReports.TryGetValue(reportId, out var current);
            var isMatch = hasCurrent && backupBytes.AsSpan().SequenceEqual(current);
            var differenceAllowed = reportId == 0xF0;
            comparisons.Add(new G600BackupComparison(
                $"0x{reportId:X2}",
                Convert.ToHexString(backupBytes),
                hasCurrent ? Convert.ToHexString(current!) : null,
                isMatch,
                differenceAllowed));

            if (!differenceAllowed && !isMatch)
            {
                isWriteStartAllowed = false;
            }
        }

        return new G600BackupPrecondition(comparisons, isWriteStartAllowed);
    }
}

public static class G600ApplyProbe
{
    // apply 実証用の検証 payload: backup F3 の LED RGB（report ID を含む offset 1–3、g600-profile-decode）を
    // XOR 0xFF で反転する。反転値は必ず backup と異なり、鍵割当など他 byte は保つ。restore で元へ戻せる。
    public static byte[] BuildLedFlippedF3(byte[] backupF3)
    {
        if (backupF3.Length != 154 || backupF3[0] != 0xF3)
            throw new ArgumentException("backup F3 must be a 154-byte report starting with 0xF3.", nameof(backupF3));

        var modified = backupF3.ToArray();
        modified[1] ^= 0xFF;
        modified[2] ^= 0xFF;
        modified[3] ^= 0xFF;
        return modified;
    }
}

public static class G600SlotProbe
{
    // F0 slot 表現（rag/openlogicool/g600-write-protocol-2026-08-15.md・libratbag 一次コード）:
    // write は {F0, 0x80|(index<<4), 0, 0}、read の第2 byte は (index<<4) | 状態flags。
    // index 3 は無効（入力全喪失の実機ログあり）のため 0..2 だけを扱う。
    public static int ReadSlotIndex(byte f0Byte1) => (f0Byte1 >> 4) & 0x03;

    public static byte[] BuildSlotSwitch(int targetSlot)
    {
        if (targetSlot is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(targetSlot), targetSlot, "slot index must be 0, 1, or 2.");

        var report = new byte[154];
        report[0] = 0xF0;
        report[1] = (byte)(0x80 | (targetSlot << 4));
        return report;
    }
}

public static class G600WritableFeatureReports
{
    public static bool IsAllowed(byte reportId) => reportId is 0xF0 or 0xF3 or 0xF4 or 0xF5;

    public static void EnsureAllowed(byte reportId)
    {
        if (!IsAllowed(reportId))
        {
            throw new InvalidOperationException($"report ID 0x{reportId:X2} is not writable.");
        }
    }
}
