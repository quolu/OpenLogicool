namespace OpenLogicool.Contracts.Profiles;

/// <summary>workspace 内の Semantic Action 1件: 意図の名前と、発火時に送る output token 列。</summary>
public sealed record WorkspaceActionEntry(
    string ActionId,
    string Name,
    IReadOnlyList<string> Outputs);

/// <summary>workspace 内の device 種別ごとの layer 構成（MappingProfileDocument と同じ語彙）。</summary>
public sealed record WorkspaceDeviceLayout(
    string DeviceKind,
    string DefaultLayerId,
    IReadOnlyList<string> LayerIds,
    IReadOnlyList<LayerSelectorEntry> LatchSelectors,
    IReadOnlyList<LayerSelectorEntry> HoldSelectors);

/// <summary>Semantic Action を device の (control, layer) へ結び付ける 1 binding（MAP-001）。</summary>
public sealed record WorkspaceActionBinding(
    string ActionId,
    string DeviceKind,
    string ControlId,
    string LayerId);

public enum WorkspaceG13LcdContentKind
{
    Image,
    Text,
}

/// <summary>
/// workspace適用中にG13 LCDへ出す内容。FramebufferBase64は160×43の1-bit 960-byte frameを保持し、
/// runtimeは画像decodeやfont描画を行わない。SourceName／TextはInput Studioの再編集表示用。
/// </summary>
public sealed record WorkspaceG13LcdSetting(
    WorkspaceG13LcdContentKind Kind,
    string FramebufferBase64,
    string? SourceName,
    string? Text);

/// <summary>
/// Action-centric binding editor の編集単位（APP-003）の wire type。
/// Semantic Action の一覧・device 種別ごとの layer 構成・action→control の binding を一冊で持ち、
/// compile で device 種別ごとの MappingProfileDocument（ProfileId = "{WorkspaceId}-{DeviceKind}"）を得る。
/// </summary>
public sealed record WorkspaceDocument(
    string SchemaVersion,
    string WorkspaceId,
    string ProfileRevision,
    string MappingRevision,
    IReadOnlyList<WorkspaceActionEntry> Actions,
    IReadOnlyList<WorkspaceDeviceLayout> Devices,
    IReadOnlyList<WorkspaceActionBinding> Bindings,
    WorkspaceG13LcdSetting? G13Lcd = null);

/// <summary>保存済み workspace revision 1件（MAP-009）。RevisionNumber は workspace ごとの連番。</summary>
public sealed record WorkspaceRevisionRecord(
    long RevisionNumber,
    string SavedAtUtc,
    WorkspaceDocument Document);

/// <summary>
/// workspace revision の append-only 保存 port（MAP-009。実装は Persistence、意味 owner は Profiles）。
/// revision は上書きせず追記だけを行い、undo は過去 revision を新 revision として再適用する。
/// </summary>
public interface IWorkspaceRevisionStore
{
    /// <summary>document を新 revision として追記し、採番した RevisionNumber を返す。</summary>
    long Append(WorkspaceDocument document, string savedAtUtc);

    /// <summary>指定 workspace の revision を RevisionNumber 昇順で返す。</summary>
    IReadOnlyList<WorkspaceRevisionRecord> ListRevisions(string workspaceId);
}
