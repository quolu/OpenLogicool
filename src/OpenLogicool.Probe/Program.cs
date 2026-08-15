using System.Text.Json;
using System.Text.Json.Serialization;
using HidSharp;
using HidSharp.Reports;

// OpenLogicool Phase 0 / Deliverable 0B: Device read-only probe.
// すべての操作は read-only（列挙・descriptor 読取・文字列読取）。device への write は行わない。

const int LogitechVendorId = 0x046D;

var command = args.Length > 0 ? args[0] : "enumerate";

return command switch
{
    "enumerate" => Enumerate(),
    "g600-backup" => G600Backup(),
    _ => Fail($"unknown command: {command}. available: enumerate, g600-backup"),
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
    using (stream)
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
