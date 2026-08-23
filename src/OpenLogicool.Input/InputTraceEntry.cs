using OpenLogicool.Contracts.Devices.Shared;

namespace OpenLogicool.Input;

/// <summary>
/// fast path が処理した1件の物理 input（button edge）の観測記録（test field・Journey A-6/B-6）。
/// fast path 本体の解決結果をそのまま写すだけの読み取り専用データで、trace 自体は判定に関与しない。
/// </summary>
public sealed record InputTraceEntry(
    string DeviceInstanceId,
    string ControlId,
    PhysicalInputEdge Edge,
    string LayerId,
    IReadOnlyList<string> OutputTokens,
    bool Emitted,
    double InputMonotonicMs,
    double DispatchCompletedMonotonicMs,
    double DispatchLatencyMs,
    long Sequence);
