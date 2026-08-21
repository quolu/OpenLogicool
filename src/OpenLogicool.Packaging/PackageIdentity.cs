namespace OpenLogicool.Packaging;

/// <summary>配布方式の根拠状態。未実測を Supported と表示しない。</summary>
public enum PackagingEvidence
{
    Unverified,
    Confirmed,
}

/// <summary>配布方式。公開方式の採択は EXP-DIST-01 の実測後だけに行う。</summary>
public enum PackagingFormat
{
    Unpackaged,
    Msix,
    SparsePackage,
    Msi,
}

/// <summary>unpackaged 開発配布の最小 layout。</summary>
public sealed record UnpackagedDistributionLayout(
    IReadOnlyList<string> ApplicationFiles,
    bool AutostartConfigured,
    bool UpdateManifestPresent,
    bool StartsDeviceWriteDuringInstallOrUpdate);

/// <summary>
/// package identity の現在地（EXP-DIST-01）。
/// 開発中は unpackaged layout だけを定義する。MSIX／Sparse Package／MSI の公開採択、
/// autostart、update manifest は clean VM 実測前なので未確認のままにする。
/// </summary>
public sealed record PackageIdentity(
    string ProductName,
    PackagingFormat DevelopmentFormat,
    PackagingEvidence PublicPackagingEvidence,
    PackagingEvidence AutostartEvidence,
    PackagingEvidence UpdateManifestEvidence,
    UnpackagedDistributionLayout DevelopmentLayout,
    string PublicPackagingDecision);

public static class PackageIdentities
{
    public static PackageIdentity CurrentDevelopment() => new(
        ProductName: "OpenLogicool",
        DevelopmentFormat: PackagingFormat.Unpackaged,
        PublicPackagingEvidence: PackagingEvidence.Unverified,
        AutostartEvidence: PackagingEvidence.Unverified,
        UpdateManifestEvidence: PackagingEvidence.Unverified,
        DevelopmentLayout: new(
            ApplicationFiles:
            [
                "OpenLogicool.Host.exe",
                "OpenLogicool.Watchdog.exe",
                "OpenLogicool.Host.dll と runtime dependencies",
            ],
            AutostartConfigured: false,
            UpdateManifestPresent: false,
            StartsDeviceWriteDuringInstallOrUpdate: false),
        PublicPackagingDecision: "EXP-DIST-01 の clean VM 実測前のため、MSIX／Sparse Package／MSI は未決定。");
}
