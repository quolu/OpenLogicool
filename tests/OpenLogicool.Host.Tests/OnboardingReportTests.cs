using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class OnboardingReportTests
{
    private static OnboardingObservations Observations(
        int g13Count = 1,
        int g600Count = 1,
        bool coexistingDetected = false,
        bool backupExists = true,
        int backupFileCount = 21) =>
        new(
            [
                new CoexistingSoftwareObservation("LGS", coexistingDetected),
                new CoexistingSoftwareObservation("G HUB", false),
                new CoexistingSoftwareObservation("Logi Options+", false),
            ],
            g13Count,
            g600Count,
            new OnboardingBackupObservation(backupExists, backupFileCount),
            ProfileCount: 2,
            AppAssociationCount: 1,
            WorkspaceCount: 1);

    [Fact]
    public void One_side_unconnected_is_stated_explicitly()
    {
        var lines = OnboardingReport.Build(Observations(g13Count: 0, g600Count: 1));

        Assert.Contains(lines, line => line.Contains("G13: 未接続") && line.Contains("もう一方だけでも利用開始できます"));
        Assert.DoesNotContain(lines, line => line.Contains("G600: 未接続"));
    }

    [Fact]
    public void Both_sides_unconnected_is_stated_explicitly()
    {
        var lines = OnboardingReport.Build(Observations(g13Count: 0, g600Count: 0));

        Assert.Contains(lines, line => line.Contains("両方未接続"));
    }

    [Fact]
    public void Both_sides_connected_has_no_unconnected_wording()
    {
        var lines = OnboardingReport.Build(Observations(g13Count: 1, g600Count: 2));

        Assert.DoesNotContain(lines, line => line.Contains("未接続"));
    }

    [Fact]
    public void Coexisting_software_detection_adds_a_caution_line()
    {
        var lines = OnboardingReport.Build(Observations(coexistingDetected: true));

        Assert.Contains(lines, line => line.Contains("LGS: 検出"));
        Assert.Contains(lines, line =>
            line.Contains("入力の二重処理・onboard write の競合に注意") &&
            line.Contains("Journey A-3"));
    }

    [Fact]
    public void No_coexisting_software_detected_has_no_caution_line()
    {
        var lines = OnboardingReport.Build(Observations(coexistingDetected: false));

        Assert.Contains(lines, line => line.Contains("LGS: 非検出"));
        Assert.DoesNotContain(lines, line => line.Contains("競合に注意"));
    }

    [Fact]
    public void Backup_present_shows_file_count_and_restore_pointer()
    {
        var lines = OnboardingReport.Build(Observations(backupExists: true, backupFileCount: 21));

        Assert.Contains(lines, line =>
            line.Contains("G600 完全 backup: あり") &&
            line.Contains("21 file") &&
            line.Contains("g600-restore-retry"));
    }

    [Fact]
    public void Backup_absent_shows_migration_safety_gate_guidance_and_honest_caveat()
    {
        var lines = OnboardingReport.Build(Observations(backupExists: false));

        Assert.Contains(lines, line =>
            line.Contains("G600 完全 backup: なし") &&
            line.Contains("確認できない") &&
            line.Contains("repo 直下で確認すること") &&
            line.Contains("Migration Safety Gate") &&
            line.Contains("docs/migration-safety-gate.md"));
    }

    [Fact]
    public void Current_configuration_counts_are_reported()
    {
        var lines = OnboardingReport.Build(Observations());

        Assert.Contains(lines, line => line.Contains("profiles: 2 件"));
        Assert.Contains(lines, line => line.Contains("app associations: 1 件"));
        Assert.Contains(lines, line => line.Contains("workspaces: 1 件"));
    }
}
