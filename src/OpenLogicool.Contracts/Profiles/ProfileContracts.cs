namespace OpenLogicool.Contracts.Profiles;

public sealed record ApplicationIdentity(
    string SchemaVersion,
    string ApplicationId,
    string FullPath,
    string? PackageIdentity,
    long ProcessGeneration,
    string? WindowMatcher);

public sealed record BindingRevision(
    string SchemaVersion,
    string ApplicationId,
    string DeviceInstanceId,
    string LayerId,
    string MappingRevision,
    IReadOnlyList<string> Outputs);

/// <summary>document 内の1 binding: (control, layer) → output token 列。</summary>
public sealed record MappingBindingEntry(
    string ControlId,
    string LayerId,
    IReadOnlyList<string> Outputs);

/// <summary>document 内の layer selector: control → 対象 layer。</summary>
public sealed record LayerSelectorEntry(
    string ControlId,
    string LayerId);

/// <summary>
/// mapping profile の永続化 wire type（OPS-002）。
/// device 種別（DeviceKind: "G13"／"G600"）ごとの mapping 一式を保存・復元する。
/// 内容の検証は Domain の MappingProfile 構築時に行い、document 自体は形だけを持つ。
/// </summary>
public sealed record MappingProfileDocument(
    string SchemaVersion,
    string ProfileId,
    string DeviceKind,
    string ProfileRevision,
    string MappingRevision,
    string DefaultLayerId,
    IReadOnlyList<string> LayerIds,
    IReadOnlyList<LayerSelectorEntry> LatchSelectors,
    IReadOnlyList<LayerSelectorEntry> HoldSelectors,
    IReadOnlyList<MappingBindingEntry> Bindings,
    WorkspaceG13LcdSetting? G13Lcd = null);

/// <summary>mapping profile の保存 port（実装は Persistence、意味 owner は Profiles）。</summary>
public interface IMappingProfileStore
{
    /// <summary>ProfileId で upsert する。</summary>
    void Upsert(MappingProfileDocument document);

    /// <summary>保存済み document を ProfileId 昇順で返す。</summary>
    IReadOnlyList<MappingProfileDocument> ListAll();
}

/// <summary>foreground app の観測 identity（APP-004）。取得できなかった要素は null で保持し、偽装しない。</summary>
public sealed record ForegroundApplicationIdentity(
    string? NormalizedFullPath,    // 小文字正規化 full path。取得不能なら null
    string? PackageFamilyName,     // MSIX/Store app の package family name。非 package app・取得不能は null
    int ProcessId,
    DateTime? ProcessStartTimeUtc); // process 世代（同名 EXE 再起動の区別・診断用。照合には使わない）

/// <summary>AppProfileAssociation.MatcherKind の許容値。</summary>
public static class AppMatcherKind
{
    /// <summary>ApplicationFullPath 列を正規化済み小文字 full path として照合する（既定・従来動作）。</summary>
    public const string Path = "path";

    /// <summary>ApplicationFullPath 列を正規化済み小文字 package family name として照合する（APP-004）。</summary>
    public const string Package = "package";
}

/// <summary>
/// foreground app と profile の関連付け（app-first 切替の永続化 wire type）。
/// ApplicationFullPath は MatcherKind に応じて正規化済み小文字 full path または package family name、
/// もしくは既定を表す "*"（既定は常に MatcherKind="path" として扱う）。
/// </summary>
public sealed record AppProfileAssociation(
    string SchemaVersion,
    string ApplicationFullPath,
    string DeviceKind,
    string ProfileId,
    string MatcherKind = AppMatcherKind.Path);

/// <summary>app→profile 関連付けの保存 port（実装は Persistence、意味 owner は Profiles）。</summary>
public interface IAppAssociationStore
{
    /// <summary>(ApplicationFullPath, DeviceKind) で upsert する。</summary>
    void Upsert(AppProfileAssociation association);

    /// <summary>保存済み関連付けを (ApplicationFullPath, DeviceKind) 昇順で返す。</summary>
    IReadOnlyList<AppProfileAssociation> ListAll();
}
