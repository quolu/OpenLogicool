using System.Text.Json;
using System.Text.Json.Serialization;
using HidSharp;
using HidSharp.Reports;
using OpenLogicool.Probe;

// OpenLogicool Phase 0 / Deliverable 0B: Device probe.
// enumerate、g600-backup、record は read-only の既存 command。write command は Migration Safety Gate 専用。

const int LogitechVendorId = 0x046D;

var command = args.Length > 0 ? args[0] : "enumerate";

return command switch
{
    "enumerate" => Enumerate(),
    "g600-backup" => G600Backup(),
    "g600-writeback" => G600Writeback(args[1..]),
    "g600-led-apply-restore" => G600LedApplyRestore(args[1..]),
    "g600-slot" => G600Slot(args[1..]),
    "record" => OpenLogicool.Probe.RawInputRecorder.Run(
        args.Length > 1 ? args[1] : "session",
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "probe-output")),
    _ => Fail($"unknown command: {command}. available: enumerate, g600-backup, record [label], g600-writeback, g600-led-apply-restore, g600-slot"),
};

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static int Enumerate()
{
    var devices = DeviceList.Local.GetHidDevices(LogitechVendorId).ToList();
    var records = new List<HidDeviceRecord>();

    foreach (var device in devices)
    {
        var record = new HidDeviceRecord
        {
            DevicePath = device.DevicePath,
            VendorId = $"0x{device.VendorID:X4}",
            ProductId = $"0x{device.ProductID:X4}",
            ReleaseNumber = device.ReleaseNumberBcd.ToString("X4"),
            MaxInputReportLength = TryGet(() => device.GetMaxInputReportLength()),
            MaxOutputReportLength = TryGet(() => device.GetMaxOutputReportLength()),
            MaxFeatureReportLength = TryGet(() => device.GetMaxFeatureReportLength()),
            Manufacturer = TryGetString(device.GetManufacturer),
            ProductName = TryGetString(device.GetProductName),
            SerialNumber = TryGetString(device.GetSerialNumber) is null ? null : "(redacted)",
        };

        try
        {
            var descriptor = device.GetReportDescriptor();
            record.RawDescriptorLength = device.GetRawReportDescriptor().Length;
            record.TopLevelCollections = descriptor.DeviceItems.Select(item => new TopLevelCollectionRecord
            {
                Usages = item.Usages.GetAllValues()
                    .Select(u => $"0x{u:X8} ({DescribeUsage(u)})")
                    .ToList(),
                InputReports = DescribeReports(descriptor, item, ReportType.Input),
                OutputReports = DescribeReports(descriptor, item, ReportType.Output),
                FeatureReports = DescribeReports(descriptor, item, ReportType.Feature),
            }).ToList();
        }
        catch (Exception ex)
        {
            record.DescriptorError = $"{ex.GetType().Name}: {ex.Message}";
        }

        records.Add(record);
    }

    var result = new EnumerationResult
    {
        Probe = "enumerate",
        CapturedAtUtc = DateTime.UtcNow.ToString("O"),
        Machine = Environment.MachineName,
        OsVersion = Environment.OSVersion.VersionString,
        DeviceCount = records.Count,
        Devices = records,
    };

    var json = JsonSerializer.Serialize(result, JsonOptions.Value);
    Console.WriteLine(json);
    return 0;
}

static int G600Backup()
{
    const int G600ProductId = 0xC24A;

    // backup 対象は vendor-defined TLC（feature report を宣言している面）だけ
    var device = DeviceList.Local.GetHidDevices(LogitechVendorId, G600ProductId)
        .FirstOrDefault(d => TryGet(d.GetMaxFeatureReportLength) is > 0);

    if (device is null)
        return Fail("G600 vendor-defined collection (feature reports) not found");

    var descriptor = device.GetReportDescriptor();
    var featureReportIds = descriptor.Reports
        .Where(r => r.ReportType == ReportType.Feature)
        .Select(r => r.ReportID)
        .OrderBy(id => id)
        .ToList();

    if (!device.TryOpen(out var stream))
        return Fail($"cannot open device: {device.DevicePath}");

    var reports = new List<FeatureReportDump>();
    using (stream!)
    {
        foreach (var reportId in featureReportIds)
        {
            var buffer = new byte[device.GetMaxFeatureReportLength()];
            buffer[0] = reportId;
            try
            {
                stream.GetFeature(buffer);
                reports.Add(new FeatureReportDump
                {
                    ReportId = $"0x{reportId:X2}",
                    LengthBytes = buffer.Length,
                    DataHex = Convert.ToHexString(buffer),
                });
            }
            catch (Exception ex)
            {
                reports.Add(new FeatureReportDump
                {
                    ReportId = $"0x{reportId:X2}",
                    LengthBytes = buffer.Length,
                    Error = $"{ex.GetType().Name}: {ex.Message}",
                });
            }
        }
    }

    var result = new G600BackupResult
    {
        Probe = "g600-backup",
        CapturedAtUtc = DateTime.UtcNow.ToString("O"),
        Machine = Environment.MachineName,
        DevicePath = device.DevicePath,
        FirmwareRelease = device.ReleaseNumberBcd.ToString("X4"),
        Reports = reports,
    };

    Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions.Value));
    return reports.Any(r => r.Error is not null) ? 2 : 0;
}

static int G600Writeback(string[] arguments)
{
    if (!G600WriteCommandParser.TryParse("g600-writeback", arguments, out var command, out var error))
        return EmitWriteResult(NewWriteResult("g600-writeback", "argument-validation", error), 1);

    if (!TryOpenWriteSession(command!, out _, out var currentReports, out var stream, out var precondition, out var failure))
        return EmitWriteResult(failure!, 2);

    using (stream)
    {
        if (!precondition!.IsWriteStartAllowed)
            return EmitWriteResult(NewWriteResult("g600-writeback", "backup-precondition", "F3 through F5 do not match backup.", precondition), 2);

        var before = currentReports![0xF3];
        try
        {
            SetWritableFeature(stream!, before);
        }
        catch (Exception ex)
        {
            return EmitWriteResult(NewWriteResult(
                "g600-writeback",
                "writeback-set-feature",
                $"{ex.GetType().Name}: {ex.Message}",
                precondition,
                new[] { Step("writeback", before, null, false) },
                "F3 SetFeature was attempted; no retry or restore was attempted."), 2);
        }

        try
        {
            var after = ReadFeature(stream!, 0xF3);
            var matches = before.AsSpan().SequenceEqual(after);
            return EmitWriteResult(NewWriteResult(
                "g600-writeback",
                "writeback-readback",
                matches ? null : "F3 readback does not match the backup.",
                precondition,
                new[] { Step("writeback", before, after, matches) },
                matches ? "F3 matches the backup after writeback." : "F3 writeback was sent; readback differs and no restore was attempted."),
                matches ? 0 : 2);
        }
        catch (Exception ex)
        {
            return EmitWriteResult(NewWriteResult(
                "g600-writeback",
                "writeback-readback",
                $"{ex.GetType().Name}: {ex.Message}",
                precondition,
                new[] { Step("writeback", before, null, false) },
                "F3 SetFeature was sent; readback failed and no retry or restore was attempted."), 2);
        }
    }
}

static int G600LedApplyRestore(string[] arguments)
{
    if (!G600WriteCommandParser.TryParse("g600-led-apply-restore", arguments, out var command, out var error))
        return EmitWriteResult(NewWriteResult("g600-led-apply-restore", "argument-validation", error), 1);

    if (!TryOpenWriteSession(command!, out _, out var currentReports, out var stream, out var precondition, out var failure))
        return EmitWriteResult(failure!, 2);

    using (stream!)
    {
        if (!precondition!.IsWriteStartAllowed)
            return EmitWriteResult(NewWriteResult("g600-led-apply-restore", "backup-precondition", "F3 through F5 do not match backup.", precondition), 2);

        var original = currentReports![0xF3];
        var ledApplied = original.ToArray();
        // g600-profile-decode: report ID を含む offset 1–3 が LED RGB。
        ledApplied[1] = 0xFF;
        ledApplied[2] = 0x00;
        ledApplied[3] = 0xFF;
        var steps = new List<G600WriteStep>();

        try
        {
            SetWritableFeature(stream!, ledApplied);
        }
        catch (Exception ex)
        {
            steps.Add(Step("apply-led-rgb", original, null, false));
            return EmitWriteResult(NewWriteResult(
                "g600-led-apply-restore",
                "apply-led-set-feature",
                $"{ex.GetType().Name}: {ex.Message}",
                precondition,
                steps,
                "F3 LED apply was attempted; backup restore was not attempted."), 2);
        }

        byte[] afterApply;
        try
        {
            afterApply = ReadFeature(stream!, 0xF3);
        }
        catch (Exception ex)
        {
            steps.Add(Step("apply-led-rgb", original, null, false));
            return EmitWriteResult(NewWriteResult(
                "g600-led-apply-restore",
                "apply-led-readback",
                $"{ex.GetType().Name}: {ex.Message}",
                precondition,
                steps,
                "F3 LED apply was sent; readback failed and backup restore was not attempted."), 2);
        }

        var applyMatches = ledApplied.AsSpan().SequenceEqual(afterApply);
        steps.Add(Step("apply-led-rgb", ledApplied, afterApply, applyMatches));
        if (!applyMatches)
        {
            return EmitWriteResult(NewWriteResult(
                "g600-led-apply-restore",
                "apply-led-readback",
                "F3 readback did not match the LED-only modified bytes.",
                precondition,
                steps,
                "F3 LED apply was sent; backup restore was not attempted."), 2);
        }

        try
        {
            SetWritableFeature(stream!, original);
        }
        catch (Exception ex)
        {
            steps.Add(Step("restore-backup", original, null, false));
            return EmitWriteResult(NewWriteResult(
                "g600-led-apply-restore",
                "restore-set-feature",
                $"{ex.GetType().Name}: {ex.Message}",
                precondition,
                steps,
                "LED apply is confirmed; backup restore was attempted but did not complete."), 2);
        }

        try
        {
            var afterRestore = ReadFeature(stream!, 0xF3);
            var restoreMatches = original.AsSpan().SequenceEqual(afterRestore);
            steps.Add(Step("restore-backup", original, afterRestore, restoreMatches));
            return EmitWriteResult(NewWriteResult(
                "g600-led-apply-restore",
                "restore-readback",
                restoreMatches ? null : "F3 restore readback does not match the backup.",
                precondition,
                steps,
                restoreMatches ? "F3 matches the backup after restore." : "Backup restore was sent; F3 readback differs."),
                restoreMatches ? 0 : 2);
        }
        catch (Exception ex)
        {
            steps.Add(Step("restore-backup", original, null, false));
            return EmitWriteResult(NewWriteResult(
                "g600-led-apply-restore",
                "restore-readback",
                $"{ex.GetType().Name}: {ex.Message}",
                precondition,
                steps,
                "Backup restore was sent; F3 readback failed."), 2);
        }
    }
}

static int G600Slot(string[] arguments)
{
    if (!G600WriteCommandParser.TryParse("g600-slot", arguments, out var command, out var error))
        return EmitWriteResult(NewWriteResult("g600-slot", "argument-validation", error), 1);

    if (!TryOpenWriteSession(command!, out _, out _, out var stream, out var precondition, out var failure))
        return EmitWriteResult(failure!, 2);

    using (stream!)
    {
        if (!precondition!.IsWriteStartAllowed)
            return EmitWriteResult(NewWriteResult("g600-slot", "backup-precondition", "F3 through F5 do not match backup.", precondition), 2);

        try
        {
            var first = ReadFeature(stream!, 0xF0);
            var second = ReadFeature(stream!, 0xF0);
            var stable = first.AsSpan().SequenceEqual(second);
            var reason = stable
                ? $"F0 byte[1] is 0x{first[1]:X2}, but its mapping to target slot {command!.TargetSlot} is unconfirmed. No SetFeature was sent."
                : "F0 changed between the two reads. No SetFeature was sent.";

            return EmitWriteResult(NewWriteResult(
                "g600-slot",
                "slot-interpretation",
                reason,
                precondition,
                new[]
                {
                    Step("f0-first-read", first, first, stable),
                    Step("f0-second-read", first, second, stable),
                },
                "F0 active-slot representation is indeterminate; no write or restore was attempted."), 3);
        }
        catch (Exception ex)
        {
            return EmitWriteResult(NewWriteResult(
                "g600-slot",
                "slot-interpretation",
                $"{ex.GetType().Name}: {ex.Message}",
                precondition,
                null,
                "F0 interpretation could not be completed; no write or restore was attempted."), 3);
        }
    }
}

static bool TryOpenWriteSession(
    G600WriteCommand command,
    out G600BackupSnapshot? snapshot,
    out IReadOnlyDictionary<byte, byte[]>? currentReports,
    out HidStream? stream,
    out G600BackupPrecondition? precondition,
    out G600WriteResult? failure)
{
    snapshot = null;
    currentReports = null;
    stream = null;
    precondition = null;
    failure = null;

    try
    {
        snapshot = G600BackupSnapshot.Load(command.BackupPath);
    }
    catch (Exception ex)
    {
        failure = NewWriteResult(WriteProbeName(command.Kind), "backup-load", $"{ex.GetType().Name}: {ex.Message}");
        return false;
    }

    var device = DeviceList.Local.GetHidDevices(LogitechVendorId, 0xC24A)
        .FirstOrDefault(candidate => TryGet(candidate.GetMaxFeatureReportLength) is > 0);
    if (device is null)
    {
        failure = NewWriteResult(WriteProbeName(command.Kind), "device-open", "G600 vendor-defined collection was not found.");
        return false;
    }

    if (!device.TryOpen(out stream))
    {
        failure = NewWriteResult(WriteProbeName(command.Kind), "device-open", $"cannot open device: {device.DevicePath}");
        return false;
    }

    try
    {
        var current = new Dictionary<byte, byte[]>();
        foreach (var reportId in new byte[] { 0xF0, 0xF3, 0xF4, 0xF5 })
        {
            current.Add(reportId, ReadFeature(stream, reportId));
        }

        precondition = G600BackupVerifier.Verify(snapshot, current);
        currentReports = current;
        return true;
    }
    catch (Exception ex)
    {
        stream.Dispose();
        stream = null;
        failure = NewWriteResult(WriteProbeName(command.Kind), "backup-precondition-read", $"{ex.GetType().Name}: {ex.Message}");
        return false;
    }
}

static string WriteProbeName(G600WriteCommandKind kind) => kind switch
{
    G600WriteCommandKind.Writeback => "g600-writeback",
    G600WriteCommandKind.LedApplyRestore => "g600-led-apply-restore",
    G600WriteCommandKind.Slot => "g600-slot",
    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
};

static byte[] ReadFeature(HidStream stream, byte reportId)
{
    var buffer = new byte[154];
    buffer[0] = reportId;
    stream.GetFeature(buffer);
    return buffer;
}

static void SetWritableFeature(HidStream stream, byte[] report)
{
    if (report.Length != 154)
        throw new InvalidOperationException("G600 feature reports must be 154 bytes.");

    G600WritableFeatureReports.EnsureAllowed(report[0]);
    stream.SetFeature(report);
}

static G600WriteStep Step(string name, byte[] expected, byte[]? actual, bool isMatch) =>
    new()
    {
        Name = name,
        ExpectedHex = Convert.ToHexString(expected),
        ActualHex = actual is null ? null : Convert.ToHexString(actual),
        IsMatch = isMatch,
    };

static G600WriteResult NewWriteResult(
    string probe,
    string stage,
    string? error,
    G600BackupPrecondition? precondition = null,
    IEnumerable<G600WriteStep>? steps = null,
    string? deviceState = null) =>
    new()
    {
        Probe = probe,
        CapturedAtUtc = DateTime.UtcNow.ToString("O"),
        Stage = stage,
        Error = error,
        BackupPrecondition = precondition,
        Steps = steps?.ToList() ?? [],
        DeviceState = deviceState,
    };

static int EmitWriteResult(G600WriteResult result, int exitCode)
{
    var outputDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "probe-output"));
    result.OutputPath = Path.Combine(outputDirectory, $"{result.Probe}-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.json");

    try
    {
        var json = JsonSerializer.Serialize(result, JsonOptions.Value);
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(result.OutputPath, json);
        Console.WriteLine(json);
        return exitCode;
    }
    catch (Exception ex)
    {
        Console.WriteLine(JsonSerializer.Serialize(new G600WriteResult
        {
            Probe = result.Probe,
            CapturedAtUtc = DateTime.UtcNow.ToString("O"),
            Stage = "probe-output-write",
            Error = $"{ex.GetType().Name}: {ex.Message}",
            Steps = [],
        }, JsonOptions.Value));
        return 1;
    }
}

static List<ReportRecord> DescribeReports(ReportDescriptor descriptor, DeviceItem item, ReportType type)
{
    return descriptor.Reports
        .Where(r => r.DeviceItem == item && r.ReportType == type)
        .Select(r => new ReportRecord
        {
            ReportId = r.ReportID,
            LengthBytes = r.Length,
            DataItemCount = r.DataItems.Count,
        })
        .ToList();
}

static string DescribeUsage(uint usage)
{
    var page = usage >> 16;
    var id = usage & 0xFFFF;
    var pageName = page switch
    {
        0x01 => "GenericDesktop",
        0x07 => "Keyboard",
        0x08 => "LED",
        0x09 => "Button",
        0x0C => "Consumer",
        0x59 => "LightingAndIllumination",
        >= 0xFF00 => "VendorDefined",
        _ => $"Page{page:X2}",
    };
    return $"{pageName}/0x{id:X2}";
}

static int? TryGet(Func<int> getter)
{
    try { return getter(); }
    catch { return null; }
}

static string? TryGetString(Func<string> getter)
{
    try { return getter(); }
    catch { return null; }
}

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Value = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

internal sealed class EnumerationResult
{
    public required string Probe { get; init; }
    public required string CapturedAtUtc { get; init; }
    public required string Machine { get; init; }
    public required string OsVersion { get; init; }
    public required int DeviceCount { get; init; }
    public required List<HidDeviceRecord> Devices { get; init; }
}

internal sealed class G600BackupResult
{
    public required string Probe { get; init; }
    public required string CapturedAtUtc { get; init; }
    public required string Machine { get; init; }
    public required string DevicePath { get; init; }
    public required string FirmwareRelease { get; init; }
    public required List<FeatureReportDump> Reports { get; init; }
}

internal sealed class FeatureReportDump
{
    public required string ReportId { get; init; }
    public required int LengthBytes { get; init; }
    public string? DataHex { get; init; }
    public string? Error { get; init; }
}

internal sealed class HidDeviceRecord
{
    public required string DevicePath { get; init; }
    public required string VendorId { get; init; }
    public required string ProductId { get; init; }
    public required string ReleaseNumber { get; init; }
    public int? MaxInputReportLength { get; init; }
    public int? MaxOutputReportLength { get; init; }
    public int? MaxFeatureReportLength { get; init; }
    public string? Manufacturer { get; init; }
    public string? ProductName { get; init; }
    public string? SerialNumber { get; init; }
    public int? RawDescriptorLength { get; set; }
    public List<TopLevelCollectionRecord>? TopLevelCollections { get; set; }
    public string? DescriptorError { get; set; }
}

internal sealed class TopLevelCollectionRecord
{
    public required List<string> Usages { get; init; }
    public required List<ReportRecord> InputReports { get; init; }
    public required List<ReportRecord> OutputReports { get; init; }
    public required List<ReportRecord> FeatureReports { get; init; }
}

internal sealed class ReportRecord
{
    public required int ReportId { get; init; }
    public required int LengthBytes { get; init; }
    public required int DataItemCount { get; init; }
}

internal sealed class G600WriteResult
{
    public required string Probe { get; init; }
    public required string CapturedAtUtc { get; init; }
    public required string Stage { get; init; }
    public string? Error { get; init; }
    public G600BackupPrecondition? BackupPrecondition { get; init; }
    public required List<G600WriteStep> Steps { get; init; }
    public string? DeviceState { get; init; }
    public string? OutputPath { get; set; }
}

internal sealed class G600WriteStep
{
    public required string Name { get; init; }
    public required string ExpectedHex { get; init; }
    public string? ActualHex { get; init; }
    public required bool IsMatch { get; init; }
}
