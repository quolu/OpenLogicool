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
    IReadOnlyList<MappingBindingEntry> Bindings);

/// <summary>mapping profile の保存 port（実装は Persistence、意味 owner は Profiles）。</summary>
public interface IMappingProfileStore
{
    /// <summary>ProfileId で upsert する。</summary>
    void Upsert(MappingProfileDocument document);

    /// <summary>保存済み document を ProfileId 昇順で返す。</summary>
    IReadOnlyList<MappingProfileDocument> ListAll();
}
