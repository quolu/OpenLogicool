using OpenLogicool.Contracts.Profiles;

namespace OpenLogicool.Desktop;

/// <summary>
/// 常駐 fast path（<c>ui --resident</c>）が同居しているときだけ渡される橋渡し（設計 t09 第4段残作業④）。
/// 保存直後に新規 down から即時反映する（device write はしない＝MAP-010）。Desktop は Input/Profiles を
/// 参照できない（architecture 契約）ため、実装（compile・RequestProfileChange 呼び出し）は Host 側に置く。
/// </summary>
public interface IResidentApplyIntent
{
    /// <summary>document を compile し、常駐中の対象 device へ新 profile を即時反映する。</summary>
    void ApplyIfResident(WorkspaceDocument document);

    /// <summary>fast path が処理した直近の input（動作チェック表示行＋実機ボタンでの割当指定用）を取り出す（無ければ空）。</summary>
    IReadOnlyList<ResidentTraceEvent> DrainTraceEvents();
}

/// <summary>
/// fast path が処理した1件の物理 input。DisplayLine は動作チェック strip 向けの表示行
/// （down のみ。up は null）。DeviceKind は "G13"／"G600"（判定不能なら instance ID のまま）。
/// </summary>
public sealed record ResidentTraceEvent(string DeviceKind, string ControlId, bool IsDown, string? DisplayLine);
