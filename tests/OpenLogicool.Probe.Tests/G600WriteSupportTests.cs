using System.Text.Json;
using OpenLogicool.Probe;
using Xunit;

namespace OpenLogicool.Probe.Tests;

public sealed class G600WriteSupportTests
{
    [Fact]
    public void F6_is_rejected_by_the_command_parser_and_writable_report_guard()
    {
        var parsed = G600WriteCommandParser.TryParse(
            "g600-writeback",
            new[] { "--backup", "backup.json", "--report", "0xF6" },
            out _,
            out _);

        Assert.False(parsed);
        Assert.False(G600WritableFeatureReports.IsAllowed(0xF6));
        Assert.Throws<InvalidOperationException>(() => G600WritableFeatureReports.EnsureAllowed(0xF6));
    }

    [Fact]
    public void F0_difference_is_recorded_but_profile_report_differences_block_write_start()
    {
        var backup = G600BackupSnapshot.Parse(BackupJson());
        var current = new Dictionary<byte, byte[]>
        {
            [0xF0] = Report(0xF0, 0x7E),
            [0xF3] = Report(0xF3, 0x03),
            [0xF4] = Report(0xF4, 0x04),
            [0xF5] = Report(0xF5, 0x05),
        };

        var onlyF0Difference = G600BackupVerifier.Verify(backup, current);

        Assert.True(onlyF0Difference.IsWriteStartAllowed);
        Assert.False(onlyF0Difference.Comparisons.Single(comparison => comparison.ReportId == "0xF0").IsMatch);
        Assert.True(onlyF0Difference.Comparisons.Single(comparison => comparison.ReportId == "0xF0").DifferenceAllowed);

        current[0xF3] = Report(0xF3, 0x7F);
        var profileDifference = G600BackupVerifier.Verify(backup, current);

        Assert.False(profileDifference.IsWriteStartAllowed);
        Assert.False(profileDifference.Comparisons.Single(comparison => comparison.ReportId == "0xF3").IsMatch);
    }

    [Fact]
    public void Apply_probe_flips_only_led_bytes_and_is_reversible()
    {
        var backupF3 = Report(0xF3, 0x03);
        backupF3[1] = 0x10; // LED R
        backupF3[2] = 0x20; // LED G
        backupF3[3] = 0x30; // LED B
        backupF3[4] = 0xAB; // key mapping byte (must be preserved)

        var modified = G600ApplyProbe.BuildLedFlippedF3(backupF3);

        Assert.Equal(154, modified.Length);
        Assert.Equal(0xF3, modified[0]);
        Assert.Equal(0x10 ^ 0xFF, modified[1]);
        Assert.Equal(0x20 ^ 0xFF, modified[2]);
        Assert.Equal(0x30 ^ 0xFF, modified[3]);
        Assert.Equal(0xAB, modified[4]);
        Assert.False(modified.AsSpan().SequenceEqual(backupF3));

        // XOR は可逆: 再度反転すると backup に戻る（restore payload = backup そのもの）
        var reverted = G600ApplyProbe.BuildLedFlippedF3(modified);
        Assert.True(reverted.AsSpan().SequenceEqual(backupF3));
    }

    [Fact]
    public void Apply_probe_rejects_a_report_that_is_not_a_154_byte_f3()
    {
        Assert.Throws<ArgumentException>(() => G600ApplyProbe.BuildLedFlippedF3(new byte[154])); // report id not 0xF3
        Assert.Throws<ArgumentException>(() => G600ApplyProbe.BuildLedFlippedF3(Report(0xF3, 0x03)[..100])); // wrong length
    }

    private static string BackupJson() => JsonSerializer.Serialize(new
    {
        Reports = new[]
        {
            ReportDocument(0xF0, 0x00),
            ReportDocument(0xF3, 0x03),
            ReportDocument(0xF4, 0x04),
            ReportDocument(0xF5, 0x05),
        },
    });

    private static object ReportDocument(byte reportId, byte marker)
    {
        var report = Report(reportId, marker);
        return new
        {
            ReportId = $"0x{reportId:X2}",
            LengthBytes = report.Length,
            DataHex = Convert.ToHexString(report),
        };
    }

    private static byte[] Report(byte reportId, byte marker)
    {
        var report = new byte[154];
        report[0] = reportId;
        report[1] = marker;
        return report;
    }
}
