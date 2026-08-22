using OpenLogicool.Contracts.Profiles;

namespace OpenLogicool.Desktop;

/// <summary>G600 本体書き込みの現在状態（表示用の文だけを持つ・内部語彙を漏らさない）。</summary>
public sealed record G600OnboardUiState(bool Active, string StatusLine);

public sealed record G600OnboardUiResult(bool Success, string Message);

/// <summary>
/// 方式A: workspace の G600 割当を G600 本体のメモリへ書き込む橋渡し。
/// 合成入力を受け付けないゲーム（NIKKE 実測 2026-08-22）でも、本体書き込みならハードウェアとして
/// 送信されるため割当が効く（G600 のみ・G13 は本体メモリ書き込み非対応）。
/// Desktop は Devices/Input を参照できない（architecture 契約）ため、実装は Host 側に置く。
/// 書き込みは数秒かかる device write のため、呼び出しは UI thread の外で行うこと。
/// </summary>
public interface IG600OnboardIntent
{
    G600OnboardUiState QueryState();

    /// <summary>保存済み document の G600 割当を本体へ書き込む（書き込み前状態を記録してから）。</summary>
    G600OnboardUiResult Apply(WorkspaceDocument document);

    /// <summary>本体を書き込み前の状態へ戻す。</summary>
    G600OnboardUiResult Restore();
}
