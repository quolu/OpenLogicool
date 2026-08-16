using System.Diagnostics;
using System.IO;

namespace OpenLogicool.Host;

/// <summary>
/// onboarding report の観測 collector（thin I/O）。OnboardingReport（pure）が消費する
/// OnboardingObservations の断片を集める。判断・表示文言はここに置かない。
/// </summary>
public static class OnboardingObservationCollector
{
    // ProcessName の完全一致（大文字小文字無視）で検出する。
    // full path 取得は行わない（権限で失敗しうるため・Packet 固有の罠）。
    private static readonly (string ProcessName, string DisplayName)[] KnownCoexistingProcesses =
    [
        ("lcore", "LGS"),
        ("lghub", "G HUB"),
        ("lghub_agent", "G HUB"),
        ("lghub_updater", "G HUB"),
        ("logioptionsplus", "Logi Options+"),
    ];

    public static IReadOnlyList<CoexistingSoftwareObservation> DetectCoexistingSoftware()
    {
        var runningProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                // GetProcesses のスナップショット後に対象 process が終了していると
                // ProcessName アクセスで InvalidOperationException が出ることがある
                // （OS 列挙と読み取りの間の race・read-only 境界の失敗として許容する）。
                try
                {
                    runningProcessNames.Add(process.ProcessName);
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        var displayNamesInOrder = KnownCoexistingProcesses
            .Select(entry => entry.DisplayName)
            .Distinct(StringComparer.Ordinal);

        return displayNamesInOrder
            .Select(displayName => new CoexistingSoftwareObservation(
                displayName,
                KnownCoexistingProcesses
                    .Where(entry => entry.DisplayName == displayName)
                    .Any(entry => runningProcessNames.Contains(entry.ProcessName))))
            .ToList();
    }

    public static OnboardingBackupObservation CollectBackupStatus()
    {
        const string relativeBackupPath = "probe-output/mig01-backup-20260815";
        var fullPath = Path.GetFullPath(relativeBackupPath);
        if (!Directory.Exists(fullPath))
        {
            return new OnboardingBackupObservation(Exists: false, FileCount: 0);
        }

        var fileCount = Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories).Length;
        return new OnboardingBackupObservation(Exists: true, FileCount: fileCount);
    }
}
