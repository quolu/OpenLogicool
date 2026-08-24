using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Perception;

namespace OpenLogicool.GameLab;

public enum VerificationLevel { Confirmed, StrongInference, Unverified, Unsupported }

public sealed record GameOperatorFailureInput(
    CaptureFault? CaptureFault,
    CaptureAvailability? CaptureAvailability,
    StateIdentityStatus? StateIdentity,
    bool UsesAbsoluteCoordinatesOnly,
    VerificationLevel Verification);

public sealed record GameOperatorFailureMessage(string Title, string Detail, string NextAction, VerificationLevel Verification);

/// <summary>capture／認識の失敗を、別 backend へ隠れて切替えず利用者へ明示する pure 表示モデル。</summary>
public static class GameOperatorFailureView
{
    public static IReadOnlyList<GameOperatorFailureMessage> Project(GameOperatorFailureInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var messages = new List<GameOperatorFailureMessage>();
        if (input.CaptureFault is not null)
        {
            messages.Add(new("画面取得を停止しました", $"{FaultLabel(input.CaptureFault.Kind)}: {input.CaptureFault.Detail}", "対象を確認して再校正してください。別の取得方式へは自動で切り替えません。", input.Verification));
        }

        if (input.CaptureAvailability is CaptureAvailability.Unavailable or CaptureAvailability.Stale
            || input.StateIdentity is StateIdentityStatus.Novel
                or StateIdentityStatus.Ambiguous
                or StateIdentityStatus.InsufficientEvidence)
        {
            messages.Add(new("画面状態を確定できません", "認識結果が自動操作の条件を満たしていません。", "画面と認識条件を確認し、再観測してください。", input.Verification));
        }

        if (input.UsesAbsoluteCoordinatesOnly)
        {
            messages.Add(new("この操作は画面配置に依存します", "絶対座標だけを使う操作は、解像度・DPI・window 配置の変化でずれる可能性があります。", "対象画面を確認してから手動で実行してください。", input.Verification));
        }

        if (input.Verification is VerificationLevel.Unverified or VerificationLevel.Unsupported)
        {
            messages.Add(new("対応状況は未確認です", "一つの実 game で成功しても一般対応とは表示しません。", "環境ごとの確認結果を参照してください。", input.Verification));
        }

        return messages;
    }

    private static string FaultLabel(CaptureFaultKind kind) => kind switch
    {
        CaptureFaultKind.Black => "黒画面",
        CaptureFaultKind.Stale => "古い画面",
        CaptureFaultKind.Drop => "frame 欠落",
        CaptureFaultKind.Resize => "画面サイズ変更",
        CaptureFaultKind.DeviceLost => "取得デバイス喪失",
        CaptureFaultKind.BackendChanged => "取得方式変更",
        CaptureFaultKind.Occluded => "画面遮蔽",
        CaptureFaultKind.Minimized => "最小化",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
