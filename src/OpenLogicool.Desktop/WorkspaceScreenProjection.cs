namespace OpenLogicool.Desktop;

/// <summary>
/// WorkspaceApplyReport（OpenLogicool.Profiles）の1段階と同じ形の表示行。
/// Desktop は architecture 契約で Contracts + Domain だけしか参照できないため、
/// Profiles の型をそのまま使わずこの型で受け取る（Host が変換する）。
/// </summary>
public sealed record WorkspaceStageCell(string Stage, string State, string Detail);

/// <summary>ApplicationRail の1行の入力（Host が ApplicationWorkspaceCatalog と実行中一覧から組み立てる）。</summary>
public sealed record ApplicationRailEntryInput(
    string ApplicationFullPath,
    string DisplayName,
    bool IsRunning,
    bool IsAssociated);

/// <summary>
/// Input Studio 画面1回分の観測値一式（設計 docs/ui-design-phase3.md §3.6）。
/// Desktop は I/O を持たないため、Host が単発観測して組み立てたものをそのまま受け取る。
/// </summary>
public sealed record WorkspaceScreenSnapshot(
    string ForegroundStateLabel,
    string? ForegroundWindowTitle,
    long? SelectedWorkspaceRevisionNumber,
    IReadOnlyList<WorkspaceStageCell> Stages,
    int G13ConnectedCount,
    int G600ConnectedCount,
    IReadOnlyList<ApplicationRailEntryInput> RailEntries);

/// <summary>
/// WorkspaceChrome のヘッダ欄一式。EditingLabel／LiveAssignmentLabel＋app pill／save chip が
/// mock（docs/ui-mocks/main.html）の画面本体（t09 第4段）。残りは次段の診断画面向けに保持する
/// （CurrentEffectiveLabel・TargetWindowLabel・AppliedRevisionLabel・ExecutionModeLabel・StageCells）。
/// </summary>
public sealed record WorkspaceChromeView(
    string EditingLabel,
    string LiveAssignmentLabel,
    string CurrentEffectiveLabel,
    string TargetWindowLabel,
    string AppliedRevisionLabel,
    string ExecutionModeLabel,
    IReadOnlyList<WorkspaceStageCell> StageCells);

/// <summary>ApplicationRail の1行の表示モデル。</summary>
public sealed record ApplicationRailRowView(
    string ApplicationFullPath,
    string DisplayName,
    bool IsRunning,
    bool IsAssociated,
    bool IsSelected);

/// <summary>device 台帳の接続要約（片側未接続でも編集は可、と明示する——設計 §5.2）。</summary>
public sealed record DeviceConnectionSummaryView(string DeviceKind, string ConnectionLabel);

/// <summary>Input Studio 新シェルの表示モデル一式（案A・段階1〜2の範囲）。</summary>
public sealed record WorkspaceScreenView(
    WorkspaceChromeView Chrome,
    IReadOnlyList<ApplicationRailRowView> RailRows,
    IReadOnlyList<DeviceConnectionSummaryView> DeviceConnections);

/// <summary>
/// snapshot → 表示モデルの pure 変換（設計 §3.2 Projection 層）。
/// 編集対象（selectedApplicationFullPath）は呼び出し側（Window）が保持する選択状態であり、
/// snapshot の観測値（現在有効・foreground）が変わっても Project はそれを動かさない——
/// これが「Alt+Tab で編集対象を失わない」構造そのもの（設計 §2.3）。
/// </summary>
public static class WorkspaceScreenProjection
{
    /// <summary>Phase 3 は実行 mode を固定表示する（設計 §1 ヘッダ表）。</summary>
    public const string FixedExecutionModeLabel = "手動入力";

    public static WorkspaceScreenView Project(WorkspaceScreenSnapshot snapshot, string selectedApplicationFullPath)
    {
        var selectedEntry = snapshot.RailEntries
            .FirstOrDefault(entry => entry.ApplicationFullPath == selectedApplicationFullPath);
        var editingLabel = selectedEntry?.DisplayName ?? selectedApplicationFullPath;

        var targetWindowLabel = snapshot.ForegroundWindowTitle is { } title
            ? $"{title}（{snapshot.ForegroundStateLabel}）"
            : $"取得不能（{snapshot.ForegroundStateLabel}）";

        var revisionLabel = snapshot.SelectedWorkspaceRevisionNumber is { } revisionNumber
            ? $"revision {revisionNumber}"
            : "下書き";

        // mock の「いまゲームに届いている割当」= 現在 foreground にある window の表示名（取得できたときだけ）。
        var liveAssignmentLabel = snapshot.ForegroundWindowTitle is { } liveTitle
            ? $"{liveTitle} 用"
            : "取得不能";

        var chrome = new WorkspaceChromeView(
            editingLabel,
            liveAssignmentLabel,
            snapshot.ForegroundStateLabel,
            targetWindowLabel,
            revisionLabel,
            FixedExecutionModeLabel,
            snapshot.Stages);

        var railRows = snapshot.RailEntries
            .Select(entry => new ApplicationRailRowView(
                entry.ApplicationFullPath,
                entry.DisplayName,
                entry.IsRunning,
                entry.IsAssociated,
                IsSelected: entry.ApplicationFullPath == selectedApplicationFullPath))
            .ToArray();

        var deviceConnections = new[]
        {
            new DeviceConnectionSummaryView("G13", DescribeConnection(snapshot.G13ConnectedCount)),
            new DeviceConnectionSummaryView("G600", DescribeConnection(snapshot.G600ConnectedCount)),
        };

        return new WorkspaceScreenView(chrome, railRows, deviceConnections);
    }

    private static string DescribeConnection(int connectedCount) =>
        connectedCount > 0 ? $"接続中（{connectedCount} 台）" : "未接続（編集は可）";
}
