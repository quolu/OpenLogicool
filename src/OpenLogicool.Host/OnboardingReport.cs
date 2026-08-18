namespace OpenLogicool.Host;

/// <summary>
/// 共存ソフト1件の検出結果。DisplayName は表示名（"LGS"／"G HUB"／"Logi Options+"）。
/// </summary>
public sealed record CoexistingSoftwareObservation(string DisplayName, bool Detected);

/// <summary>
/// G600 完全 backup 導線（probe-output/mig01-backup-20260815/）の観測結果。
/// Exists=false は「真に存在しない」と「cwd が repo 直下でないため確認できない」の両方を含む
/// （Directory.Exists では区別できないため、表示側で両方の可能性を正直に示す）。
/// </summary>
public sealed record OnboardingBackupObservation(bool Exists, int FileCount);

/// <summary>
/// onboarding report の入力（観測値のみ・I/O なし）。
/// </summary>
public sealed record OnboardingObservations(
    IReadOnlyList<CoexistingSoftwareObservation> CoexistingSoftware,
    int G13DeviceCount,
    int G600DeviceCount,
    OnboardingBackupObservation Backup,
    int ProfileCount,
    int AppAssociationCount,
    int WorkspaceCount);

/// <summary>
/// 初回導入の判断材料（Journey A の機能中核・Phase 3）。pure builder——観測値だけから
/// read-only 表示用の行を組み立てる。I/O は一切行わない。
/// </summary>
public static class OnboardingReport
{
    public static IReadOnlyList<string> Build(OnboardingObservations observations)
    {
        var lines = new List<string>();

        lines.Add("== 共存ソフト検出 ==");
        foreach (var software in observations.CoexistingSoftware)
        {
            lines.Add($"  {software.DisplayName}: {(software.Detected ? "検出" : "非検出")}");
        }

        if (observations.CoexistingSoftware.Any(software => software.Detected))
        {
            lines.Add("  注意: 入力の二重処理・onboard write の競合に注意——OpenLogicool ownership 移行は共存 read-only と分けて判断する（計画 Journey A-3）");
        }

        lines.Add("== device 接続 ==");
        lines.Add($"  G13: {observations.G13DeviceCount} 件");
        lines.Add($"  G600: {observations.G600DeviceCount} 件");
        if (observations.G13DeviceCount == 0 && observations.G600DeviceCount == 0)
        {
            lines.Add("  両方未接続——device を接続してから再確認すること");
        }
        else
        {
            if (observations.G13DeviceCount == 0)
            {
                lines.Add("  G13: 未接続（もう一方だけでも利用開始できます）");
            }

            if (observations.G600DeviceCount == 0)
            {
                lines.Add("  G600: 未接続（もう一方だけでも利用開始できます）");
            }
        }

        lines.Add("== G600 backup 導線 ==");
        lines.Add(observations.Backup.Exists
            ? $"  G600 完全 backup: あり（{observations.Backup.FileCount} file・SHA-256 封入・restore は probe g600-restore-retry が正）"
            : "  G600 完全 backup: なし——確認できない（repo 外から実行時は repo 直下で確認すること）。G600 への write を行う前に Migration Safety Gate（docs/migration-safety-gate.md）に従い backup を作成すること。");
        lines.Add(observations.CoexistingSoftware.Any(software => software.Detected)
            ? "  出荷割当の無効化: 共存ソフト検出中は常駐開始でも書かない（二重入力のまま）"
            : "  出荷割当の無効化: 常駐開始時に G600 の本体割当を無効化し、停止時に戻す（LGS なし運用）");

        lines.Add("== 設定の現在地 ==");
        lines.Add($"  profiles: {observations.ProfileCount} 件");
        lines.Add($"  app associations: {observations.AppAssociationCount} 件");
        lines.Add($"  workspaces: {observations.WorkspaceCount} 件");
        lines.Add("  詳細は `diagnostics` command を参照");

        return lines;
    }
}
