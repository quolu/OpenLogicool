using System.Security.Cryptography;

namespace OpenLogicool.Packaging;

/// <summary>配布物に含める依存 component の SBOM 行。</summary>
public sealed record SbomComponent(string Name, string Version, string License, string NoticeSource);

/// <summary>配布 artifact の SHA-256。署名や timestamp ではない。</summary>
public sealed record ArtifactHash(string FileName, string Sha256);

/// <summary>hash を計算する artifact input。配布 I/O は呼出側の責務。</summary>
public sealed record ArtifactInput(string FileName, ReadOnlyMemory<byte> Content);

/// <summary>t06 の package identity に同梱する SBOM／notice 表現。</summary>
public sealed record SbomNoticeBundle(
    PackageIdentity PackageIdentity,
    IReadOnlyList<SbomComponent> Components,
    IReadOnlyList<ArtifactHash> ArtifactHashes,
    IReadOnlyList<string> IncludedFiles,
    bool SignatureCreated);

/// <summary>
/// SBOM、Third-Party Notices、artifact hash の pure builder。
/// public package の署名と方式選択は Authenticode／EXP-DIST-01 の別 gate であり、ここでは作成しない。
/// </summary>
public static class SbomNotices
{
    public static IReadOnlyList<SbomComponent> KnownComponents { get; } =
    [
        new("Microsoft.Data.Sqlite", "10.0.10", "MIT", "https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.10"),
        new("SQLitePCLRaw.lib.e_sqlite3", "2.1.12", "Apache-2.0", "https://www.nuget.org/packages/SQLitePCLRaw.lib.e_sqlite3/2.1.12"),
        new("Vortice.Direct3D11", "3.6.2", "MIT", "https://www.nuget.org/packages/Vortice.Direct3D11/3.6.2"),
        new("Vortice.DXGI", "3.6.2", "MIT", "https://www.nuget.org/packages/Vortice.DXGI/3.6.2"),
        new("SharpGen.Runtime", "2.2.0-beta", "MIT", "https://www.nuget.org/packages/SharpGen.Runtime/2.2.0-beta"),
    ];

    public static SbomNoticeBundle CreateForCurrentDevelopment(IEnumerable<ArtifactInput> artifacts) =>
        Create(PackageIdentities.CurrentDevelopment(), KnownComponents, artifacts);

    public static SbomNoticeBundle Create(
        PackageIdentity packageIdentity,
        IEnumerable<SbomComponent> components,
        IEnumerable<ArtifactInput> artifacts)
    {
        ArgumentNullException.ThrowIfNull(packageIdentity);
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(artifacts);

        var componentRows = components.ToArray();
        if (componentRows.Any(component =>
                string.IsNullOrWhiteSpace(component.Name)
                || string.IsNullOrWhiteSpace(component.Version)
                || string.IsNullOrWhiteSpace(component.License)
                || string.IsNullOrWhiteSpace(component.NoticeSource)))
        {
            throw new ArgumentException("SBOM component は name、version、license、notice source をすべて持たなければなりません。", nameof(components));
        }

        if (componentRows.Select(component => (component.Name, component.Version)).Distinct().Count() != componentRows.Length)
        {
            throw new ArgumentException("SBOM component の name/version が重複しています。", nameof(components));
        }

        var artifactRows = artifacts.ToArray();
        if (artifactRows.Any(artifact => string.IsNullOrWhiteSpace(artifact.FileName)))
        {
            throw new ArgumentException("artifact file name は空にできません。", nameof(artifacts));
        }

        if (artifactRows.Select(artifact => artifact.FileName).Distinct(StringComparer.Ordinal).Count() != artifactRows.Length)
        {
            throw new ArgumentException("artifact file name が重複しています。", nameof(artifacts));
        }

        var hashes = artifactRows
            .Select(artifact => new ArtifactHash(
                artifact.FileName,
                Convert.ToHexString(SHA256.HashData(artifact.Content.Span))))
            .ToArray();

        return new(
            packageIdentity,
            componentRows,
            hashes,
            ["sbom.json", "THIRD-PARTY-NOTICES.md"],
            SignatureCreated: false);
    }
}
