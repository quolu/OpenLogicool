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
    "g600-f3-compensated-restore" => G600F3CompensatedRestore(args[1..]),
    "g600-restore-retry" => G600RestoreRetry(args[1..]),
    "g600-apply-verify" => G600ApplyVerify(args[1..]),
    "g600-slot-cycle" => G600SlotCycle(args[1..]),
    "record" => OpenLogicool.Probe.RawInputRecorder.Run(
        args.Length > 1 ? args[1] : "session",
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "probe-output")),
    _ => Fail($"unknown command: {command}. available: enumerate, g600-backup, record [label], g600-writeback, g600-led-apply-restore, g600-slot, g600-restore-retry, g600-apply-verify"),
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

// evidence-based restore（rag/openlogicool/g600-write-protocol-2026-08-15.md）。
// 公開運用知: fresh open・open後 settle・handle 非再利用・fresh open で readback・一致まで再送。
// backup が完全なので不一致でも後退なし。write 対象は F3/F4/F5 のみ（F6 は EnsureAllowed で拒否）。
static int G600RestoreRetry(string[] arguments)
{
    const string probe = "g600-restore-retry";
    string? backupPath = null;
    var targets = new List<byte>();
    var maxAttempts = 8;
    var settleMs = 2000;
    for (var i = 0; i < arguments.Length; i++)
    {
        switch (arguments[i])
        {
            case "--backup" when i + 1 < arguments.Length: backupPath = arguments[++i]; break;
            case "--report" when i + 1 < arguments.Length:
                foreach (var token in arguments[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var id = Convert.ToByte(token.Replace("0x", "", StringComparison.OrdinalIgnoreCase), 16);
                    targets.Add(id);
                }
                break;
            case "--max-attempts" when i + 1 < arguments.Length: maxAttempts = int.Parse(arguments[++i]); break;
            case "--settle-ms" when i + 1 < arguments.Length: settleMs = int.Parse(arguments[++i]); break;
        }
    }
    if (backupPath is null)
        return EmitWriteResult(NewWriteResult(probe, "argument-validation", "--backup <path> is required."), 1);
    if (targets.Count == 0)
    {
        targets.Add(0xF3);
        targets.Add(0xF5);
    }
    foreach (var id in targets)
    {
        if (id is not (0xF3 or 0xF4 or 0xF5))
            return EmitWriteResult(NewWriteResult(probe, "argument-validation", $"report 0x{id:X2} is not a restorable profile report (F3/F4/F5 only)."), 1);
    }

    G600BackupSnapshot snapshot;
    try
    {
        snapshot = G600BackupSnapshot.Load(backupPath);
    }
    catch (Exception ex)
    {
        return EmitWriteResult(NewWriteResult(probe, "backup-load", $"{ex.GetType().Name}: {ex.Message}"), 1);
    }

    var perReport = new List<G600WriteStep>();
    var allMatched = true;

    foreach (var reportId in targets)
    {
        var backupBytes = snapshot.Reports[reportId];
        var (matched, openError) = WriteFeatureWithRetry(reportId, backupBytes, maxAttempts, settleMs, $"0x{reportId:X2}", perReport);
        if (openError is not null)
            return EmitWriteResult(NewWriteResult(probe, "device-open", openError, steps: perReport), 2);
        if (!matched)
            allMatched = false;
    }

    return EmitWriteResult(NewWriteResult(
        probe,
        "restore-retry",
        allMatched ? null : "one or more target reports did not match backup within max attempts. Backup is intact; retry or use LGS.",
        steps: perReport,
        deviceState: allMatched
            ? "All target reports match backup after retry restore (fresh-open verified)."
            : "Some target reports still differ; no regression risk (backup complete)."),
        allMatched ? 0 : 2);
}

// onboard apply 実証（Migration Safety Gate DEV-010 / Input Studio onboard write の前提）。
// clean 状態（F3/F4/F5 が backup 一致）を fresh open で確認 → 意図的に改変した F3 を evidence-based 作法で書き、
// fresh verify で反映を確認 → apply の成否に関わらず backup へ restore し、fresh verify で一致確認。
// backup が完全なので apply が失敗しても後退なし。write 対象は F3 のみ、改変は LED RGB だけ（鍵割当は保つ）。
static int G600ApplyVerify(string[] arguments)
{
    const string probe = "g600-apply-verify";
    string? backupPath = null;
    var maxAttempts = 8;
    var settleMs = 2000;
    for (var i = 0; i < arguments.Length; i++)
    {
        switch (arguments[i])
        {
            case "--backup" when i + 1 < arguments.Length: backupPath = arguments[++i]; break;
            case "--max-attempts" when i + 1 < arguments.Length: maxAttempts = int.Parse(arguments[++i]); break;
            case "--settle-ms" when i + 1 < arguments.Length: settleMs = int.Parse(arguments[++i]); break;
        }
    }
    if (backupPath is null)
        return EmitWriteResult(NewWriteResult(probe, "argument-validation", "--backup <path> is required."), 1);

    G600BackupSnapshot snapshot;
    try
    {
        snapshot = G600BackupSnapshot.Load(backupPath);
    }
    catch (Exception ex)
    {
        return EmitWriteResult(NewWriteResult(probe, "backup-load", $"{ex.GetType().Name}: {ex.Message}"), 1);
    }

    var backupF3 = snapshot.Reports[0xF3];
    var modified = G600ApplyProbe.BuildLedFlippedF3(backupF3);

    // precondition: F3/F4/F5 が backup 一致（clean 状態）かを確認。合致しなければ何も書かない。
    var device = DeviceList.Local.GetHidDevices(LogitechVendorId, 0xC24A)
        .FirstOrDefault(candidate => TryGet(candidate.GetMaxFeatureReportLength) is > 0);
    if (device is null || !device.TryOpen(out var preStream))
        return EmitWriteResult(NewWriteResult(probe, "device-open", "G600 vendor-defined collection could not be opened."), 2);

    var current = new Dictionary<byte, byte[]>();
    using (preStream)
    {
        try
        {
            foreach (var id in new byte[] { 0xF3, 0xF4, 0xF5 })
                current[id] = ReadFeature(preStream, id);
        }
        catch (Exception ex)
        {
            return EmitWriteResult(NewWriteResult(probe, "precondition-read", $"{ex.GetType().Name}: {ex.Message}"), 2);
        }
    }

    foreach (var id in new byte[] { 0xF3, 0xF4, 0xF5 })
    {
        if (!current[id].AsSpan().SequenceEqual(snapshot.Reports[id]))
            return EmitWriteResult(NewWriteResult(
                probe,
                "precondition",
                $"report 0x{id:X2} does not match backup; device is not in a clean backup state. Nothing was written.",
                steps: new[] { Step($"precondition-0x{id:X2}", snapshot.Reports[id], current[id], false) },
                deviceState: "No write attempted; restore first with g600-restore-retry if the device is dirty."), 2);
    }

    var steps = new List<G600WriteStep>();

    // apply: 改変 payload を evidence-based 作法で書き、fresh verify で反映を確認
    var (applyMatched, applyOpenError) = WriteFeatureWithRetry(0xF3, modified, maxAttempts, settleMs, "apply", steps);
    if (applyOpenError is not null)
        return EmitWriteResult(NewWriteResult(probe, "apply-open", applyOpenError,
            steps: steps,
            deviceState: "apply phase could not open the device; restore not attempted. Backup is intact."), 2);

    // restore: apply の成否に関わらず backup へ必ず戻す
    var (restoreMatched, restoreOpenError) = WriteFeatureWithRetry(0xF3, backupF3, maxAttempts, settleMs, "restore", steps);
    if (restoreOpenError is not null)
        return EmitWriteResult(NewWriteResult(probe, "restore-open", restoreOpenError,
            steps: steps,
            deviceState: "restore phase could not reopen the device. Backup is intact; re-run g600-restore-retry."), 2);

    var ok = applyMatched && restoreMatched;
    var state = (applyMatched, restoreMatched) switch
    {
        (true, true) => "Apply landed byte-exact and restore returned F3 to backup (fresh-open verified). Onboard apply capability proven.",
        (false, true) => "Apply did not land within max attempts, but restore returned F3 to backup. No regression.",
        (true, false) => "Apply landed but restore did not match backup within max attempts. Run g600-restore-retry; backup is intact.",
        (false, false) => "Neither apply nor restore matched within max attempts; backup is intact.",
    };

    return EmitWriteResult(NewWriteResult(
        probe,
        "apply-verify",
        ok ? null : "apply or restore did not reach a byte-exact match within max attempts.",
        steps: steps,
        deviceState: state),
        ok ? 0 : 2);
}

// EXP-G600-03: F0 active slot 切替の実証。
// libratbag 形式 {F0, 0x80|(index<<4), 0, 0} で別 slot へ切替 → fresh read で index bit を照合 →
// F3/F4/F5 無傷を確認 → baseline slot へ復帰 → 同照合。index 3 は BuildSlotSwitch が拒否。
// F0 は runtime 状態（read 値の下位 nibble は状態 flags で揺れる）ため、照合は上位 nibble の index bit だけで行う。
static int G600SlotCycle(string[] arguments)
{
    const string probe = "g600-slot-cycle";
    string? backupPath = null;
    int? requestedTarget = null;
    var settleMs = 2000;
    for (var i = 0; i < arguments.Length; i++)
    {
        switch (arguments[i])
        {
            case "--backup" when i + 1 < arguments.Length: backupPath = arguments[++i]; break;
            case "--target" when i + 1 < arguments.Length: requestedTarget = int.Parse(arguments[++i]); break;
            case "--settle-ms" when i + 1 < arguments.Length: settleMs = int.Parse(arguments[++i]); break;
        }
    }
    if (backupPath is null)
        return EmitWriteResult(NewWriteResult(probe, "argument-validation", "--backup <path> is required."), 1);
    if (requestedTarget is < 0 or > 2)
        return EmitWriteResult(NewWriteResult(probe, "argument-validation", "--target must be 0, 1, or 2."), 1);

    G600BackupSnapshot snapshot;
    try
    {
        snapshot = G600BackupSnapshot.Load(backupPath);
    }
    catch (Exception ex)
    {
        return EmitWriteResult(NewWriteResult(probe, "backup-load", $"{ex.GetType().Name}: {ex.Message}"), 1);
    }

    var steps = new List<G600WriteStep>();

    // baseline: fresh open で F0 と F3/F4/F5 を読み、profile 3面が backup 一致（clean）であることを確認
    byte[] baselineF0;
    {
        var device = DeviceList.Local.GetHidDevices(LogitechVendorId, 0xC24A)
            .FirstOrDefault(candidate => TryGet(candidate.GetMaxFeatureReportLength) is > 0);
        if (device is null || !device.TryOpen(out var stream))
            return EmitWriteResult(NewWriteResult(probe, "device-open", "G600 vendor-defined collection could not be opened."), 2);
        using (stream)
        {
            System.Threading.Thread.Sleep(settleMs);
            baselineF0 = ReadFeature(stream, 0xF0);
            foreach (var id in new byte[] { 0xF3, 0xF4, 0xF5 })
            {
                var current = ReadFeature(stream, id);
                if (!current.AsSpan().SequenceEqual(snapshot.Reports[id]))
                    return EmitWriteResult(NewWriteResult(probe, "precondition",
                        $"report 0x{id:X2} does not match backup; not starting from a clean state. Nothing was written.",
                        steps: new[] { Step($"precondition-0x{id:X2}", snapshot.Reports[id], current, false) }), 2);
            }
        }
    }

    var baselineSlot = G600SlotProbe.ReadSlotIndex(baselineF0[1]);
    if (baselineSlot > 2)
        return EmitWriteResult(NewWriteResult(probe, "precondition",
            $"baseline F0 slot index is {baselineSlot} (invalid); refusing to derive targets from an abnormal state.",
            steps: new[] { Step("baseline-f0", baselineF0, baselineF0, false) }), 2);
    var targetSlot = requestedTarget ?? (baselineSlot + 1) % 3;
    if (targetSlot == baselineSlot)
        return EmitWriteResult(NewWriteResult(probe, "argument-validation",
            $"target slot {targetSlot} equals the baseline slot; the switch would be unobservable.",
            steps: new[] { Step("baseline-f0", baselineF0, baselineF0, true) }), 1);

    steps.Add(Step("baseline-f0", baselineF0, baselineF0, true));

    // switch → verify → 復帰 → verify。各段 fresh open・settle・index bit 照合。
    foreach (var (phase, slot) in new[] { ("switch", targetSlot), ("return", baselineSlot) })
    {
        var payload = G600SlotProbe.BuildSlotSwitch(slot);

        var writeDevice = DeviceList.Local.GetHidDevices(LogitechVendorId, 0xC24A)
            .FirstOrDefault(candidate => TryGet(candidate.GetMaxFeatureReportLength) is > 0);
        if (writeDevice is null || !writeDevice.TryOpen(out var writeStream))
            return EmitWriteResult(NewWriteResult(probe, $"{phase}-open", $"could not open G600 for {phase} write.", steps: steps,
                deviceState: phase == "return" ? "slot switched but return write could not start; re-run with --target to restore." : null), 2);
        using (writeStream)
        {
            System.Threading.Thread.Sleep(settleMs);
            SetWritableFeature(writeStream, payload);
        }

        var verifyDevice = DeviceList.Local.GetHidDevices(LogitechVendorId, 0xC24A)
            .FirstOrDefault(candidate => TryGet(candidate.GetMaxFeatureReportLength) is > 0);
        if (verifyDevice is null || !verifyDevice.TryOpen(out var verifyStream))
            return EmitWriteResult(NewWriteResult(probe, $"{phase}-verify-open", $"{phase} write sent but could not reopen for verify.", steps: steps), 2);
        byte[] afterF0;
        var profilesIntact = true;
        using (verifyStream)
        {
            System.Threading.Thread.Sleep(settleMs);
            afterF0 = ReadFeature(verifyStream, 0xF0);
            foreach (var id in new byte[] { 0xF3, 0xF4, 0xF5 })
            {
                if (!ReadFeature(verifyStream, id).AsSpan().SequenceEqual(snapshot.Reports[id]))
                    profilesIntact = false;
            }
        }

        var indexMatches = G600SlotProbe.ReadSlotIndex(afterF0[1]) == slot;
        steps.Add(Step($"{phase}-to-slot{slot}", payload, afterF0, indexMatches));
        if (!profilesIntact)
            return EmitWriteResult(NewWriteResult(probe, $"{phase}-profile-check",
                "F3/F4/F5 changed during slot switching. Stopped; restore profiles with g600-restore-retry.",
                steps: steps), 2);
        if (!indexMatches)
            return EmitWriteResult(NewWriteResult(probe, $"{phase}-verify",
                $"F0 slot index after {phase} is {G600SlotProbe.ReadSlotIndex(afterF0[1])}, expected {slot}. No further write attempted.",
                steps: steps,
                deviceState: phase == "switch"
                    ? "Slot did not switch as hypothesized; baseline slot is likely still active. Profiles intact."
                    : "Switch succeeded but return did not; re-run with --target to restore the baseline slot. Profiles intact."), 2);
    }

    return EmitWriteResult(NewWriteResult(
        probe,
        "slot-cycle",
        null,
        steps: steps,
        deviceState: $"F0 slot switched {baselineSlot}->{targetSlot}->{baselineSlot} with index-bit verification on fresh opens; F3/F4/F5 intact throughout."),
        0);
}

// incident 2026-08-15-g600-f3-writeback-shift の補償 restore（オーナー裁定 案A）。
// 前提: direct SET_FEATURE は data 先頭へ 0x00 を1個挿入し末尾1 byteを落として格納する（1観測）。
// 開始条件: F3 が「既知のずれ値」と byte 一致し、F4/F5 が backup 一致。それ以外は何も書かず停止。
static int G600F3CompensatedRestore(string[] arguments)
{
    const string probe = "g600-f3-compensated-restore";
    string? backupPath = null;
    for (var index = 0; index < arguments.Length; index++)
    {
        if (arguments[index] == "--backup" && index + 1 < arguments.Length)
            backupPath = arguments[++index];
    }
    if (backupPath is null)
        return EmitWriteResult(NewWriteResult(probe, "argument-validation", "--backup <path> is required."), 1);

    G600BackupSnapshot snapshot;
    try
    {
        snapshot = G600BackupSnapshot.Load(backupPath);
    }
    catch (Exception ex)
    {
        return EmitWriteResult(NewWriteResult(probe, "backup-load", $"{ex.GetType().Name}: {ex.Message}"), 1);
    }

    var backupF3 = snapshot.Reports[0xF3];
    // 既知のずれ値: [F3][00][B1..B152]（B153 が押し出し）
    var expectedShifted = new byte[154];
    expectedShifted[0] = 0xF3;
    expectedShifted[1] = 0x00;
    backupF3.AsSpan(1, 152).CopyTo(expectedShifted.AsSpan(2));
    // 補償 payload: 格納結果 = [00][sent_data 先頭152] となる観測に合わせ、sent_data = [B2..B153][pad 00]
    var compensated = new byte[154];
    compensated[0] = 0xF3;
    backupF3.AsSpan(2, 152).CopyTo(compensated.AsSpan(1));
    compensated[153] = 0x00;

    var device = DeviceList.Local.GetHidDevices(LogitechVendorId, 0xC24A)
        .FirstOrDefault(candidate => TryGet(candidate.GetMaxFeatureReportLength) is > 0);
    if (device is null)
        return EmitWriteResult(NewWriteResult(probe, "device-open", "G600 vendor-defined collection was not found."), 2);

    if (!device.TryOpen(out var stream))
        return EmitWriteResult(NewWriteResult(probe, "device-open", $"cannot open device: {device.DevicePath}"), 2);

    byte[] currentF3;
    using (stream)
    {
        try
        {
            currentF3 = ReadFeature(stream, 0xF3);
            var currentF4 = ReadFeature(stream, 0xF4);
            var currentF5 = ReadFeature(stream, 0xF5);

            if (!currentF4.AsSpan().SequenceEqual(snapshot.Reports[0xF4]) ||
                !currentF5.AsSpan().SequenceEqual(snapshot.Reports[0xF5]))
                return EmitWriteResult(NewWriteResult(probe, "precondition", "F4/F5 do not match backup; device state is not the known incident state. Nothing was written.",
                    steps: new[] { Step("verify-f4-f5", snapshot.Reports[0xF4], currentF4, false) }), 2);

            if (!currentF3.AsSpan().SequenceEqual(expectedShifted))
                return EmitWriteResult(NewWriteResult(probe, "precondition", "current F3 does not equal the known shifted value; the shift model does not hold. Nothing was written.",
                    steps: new[] { Step("verify-known-shift", expectedShifted, currentF3, false) }), 2);

            SetWritableFeature(stream, compensated);
        }
        catch (Exception ex)
        {
            return EmitWriteResult(NewWriteResult(probe, "compensated-write", $"{ex.GetType().Name}: {ex.Message}",
                deviceState: "compensated SetFeature may or may not have reached the device; no retry attempted."), 2);
        }
    }

    // 検証は必ず fresh open（incident の教訓: 同一 stream の直後 read は証拠にしない）
    if (!device.TryOpen(out var verifyStream))
        return EmitWriteResult(NewWriteResult(probe, "verify-open", "compensated write sent, but device could not be reopened for fresh verification."), 2);

    using (verifyStream)
    {
        try
        {
            var after = ReadFeature(verifyStream, 0xF3);
            var matches = after.AsSpan().SequenceEqual(backupF3);
            return EmitWriteResult(NewWriteResult(
                probe,
                "fresh-verify",
                matches ? null : "F3 after compensated restore does not match backup. No further write attempted.",
                steps: new[]
                {
                    Step("compensated-write", compensated, null, true),
                    Step("fresh-verify", backupF3, after, matches),
                },
                deviceState: matches
                    ? "F3 matches backup after compensated restore (fresh open)."
                    : "F3 differs from backup; shift model refuted or non-deterministic. Stopped."),
                matches ? 0 : 2);
        }
        catch (Exception ex)
        {
            return EmitWriteResult(NewWriteResult(probe, "fresh-verify", $"{ex.GetType().Name}: {ex.Message}",
                deviceState: "compensated write sent; fresh verification read failed."), 2);
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

// evidence-based write 作法（rag/openlogicool/g600-write-protocol-2026-08-15.md）を1箇所に集約する。
// 各 attempt: fresh open → settle → SET_FEATURE → close、続いて fresh open → settle → GET_FEATURE → close で verify。
// 同一 stream の直後 read は証拠にしない。一致するか maxAttempts に達するまで再送。open 失敗は OpenError で返す。
static (bool Matched, string? OpenError) WriteFeatureWithRetry(
    byte reportId, byte[] desiredBytes, int maxAttempts, int settleMs, string stepPrefix, List<G600WriteStep> steps)
{
    var matched = false;
    for (var attempt = 1; attempt <= maxAttempts && !matched; attempt++)
    {
        var writeDevice = DeviceList.Local.GetHidDevices(LogitechVendorId, 0xC24A)
            .FirstOrDefault(candidate => TryGet(candidate.GetMaxFeatureReportLength) is > 0);
        if (writeDevice is null || !writeDevice.TryOpen(out var writeStream))
            return (false, $"could not open G600 for write (report 0x{reportId:X2}, {stepPrefix} attempt {attempt}).");
        using (writeStream)
        {
            System.Threading.Thread.Sleep(settleMs);
            SetWritableFeature(writeStream, desiredBytes);
        }

        var verifyDevice = DeviceList.Local.GetHidDevices(LogitechVendorId, 0xC24A)
            .FirstOrDefault(candidate => TryGet(candidate.GetMaxFeatureReportLength) is > 0);
        if (verifyDevice is null || !verifyDevice.TryOpen(out var verifyStream))
            return (false, $"write sent but could not reopen for verify (report 0x{reportId:X2}, {stepPrefix} attempt {attempt}).");
        byte[] lastRead;
        using (verifyStream)
        {
            System.Threading.Thread.Sleep(settleMs);
            lastRead = ReadFeature(verifyStream, reportId);
        }

        matched = lastRead.AsSpan().SequenceEqual(desiredBytes);
        steps.Add(Step($"{stepPrefix}-attempt{attempt}", desiredBytes, lastRead, matched));
    }

    return (matched, null);
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
