namespace OpenLogicool.Packaging;

public enum InstallLifecycleAction
{
    Install,
    Update,
    Rollback,
    Repair,
    Uninstall,
}

/// <summary>配布 lifecycle の操作契約。device write は既存 leftover restore 口以外から始めない。</summary>
public sealed record InstallLifecycleStep(
    InstallLifecycleAction Action,
    bool StartsDeviceWrite,
    bool RequiresLeftoverRestore);

public static class InstallLifecycle
{
    /// <summary>
    /// install/update/rollback/repair/uninstall の最小契約。
    /// rollback と uninstall は LGS 復帰のため既存 `leftover restore` を要求するが、
    /// この packaging 面から device API や restore 実装へ直接到達しない。
    /// </summary>
    public static IReadOnlyList<InstallLifecycleStep> DefaultSteps() =>
    [
        new(InstallLifecycleAction.Install, StartsDeviceWrite: false, RequiresLeftoverRestore: false),
        new(InstallLifecycleAction.Update, StartsDeviceWrite: false, RequiresLeftoverRestore: false),
        new(InstallLifecycleAction.Rollback, StartsDeviceWrite: false, RequiresLeftoverRestore: true),
        new(InstallLifecycleAction.Repair, StartsDeviceWrite: false, RequiresLeftoverRestore: false),
        new(InstallLifecycleAction.Uninstall, StartsDeviceWrite: false, RequiresLeftoverRestore: true),
    ];
}
