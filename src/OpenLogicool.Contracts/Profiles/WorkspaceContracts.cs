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
    IReadOnlyList<WorkspaceActionBinding> Bindings);
