namespace OpenLogicool.GameLab.Prototype;

/// <summary>
/// oracle JSONL の 1 行（docs/gamelab-prototype-spec.md §Oracle）。
/// System.Text.Json の CamelCase 命名で "seq","monotonicMs","stateId","cause" になる。
/// </summary>
public sealed record OracleEntry(int Seq, double MonotonicMs, string StateId, string Cause);
