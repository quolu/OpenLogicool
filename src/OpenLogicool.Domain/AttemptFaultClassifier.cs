using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Domain;

/// <summary>DispatchArmed 以降に検出され得る fault の種別（campaign t07 の fault point）。crash は
/// 検出でなく再起動復元（gate の Recover——§6.7 契約2）が扱うため、この列挙には無い。</summary>
public enum AttemptFaultPoint
{
    /// <summary>handled stop（PER-005 停止・handled shutdown）。</summary>
    HandledStop,

    /// <summary>対象 window の喪失。</summary>
    TargetWindowLost,

    /// <summary>SendInput の部分成功（Input Emitter の fault。定義上、外部入力 API は呼ばれている）。</summary>
    PartialSendInput,
}

/// <summary>fault 検出時点で、外部入力 API の呼出について runtime が何を保証できるか。</summary>
public enum ExternalInputCallState
{
    /// <summary>一度も呼んでいないことを runtime 自身が保証できる。</summary>
    ProvablyNotCalled,

    /// <summary>呼んだ、または呼んだか分からない。</summary>
    CalledOrUnknown,
}

/// <summary>
/// DispatchArmed 以降の fault を §6.7 の終端へ写像する pure 分類器（t07・NFR-012）。
/// 保証できる中止だけが Disarmed であり、保証できない場合はすべて OutcomeUnknown。
/// 「呼んでいない保証」と矛盾する組合せ（partial SendInput）は分類せず例外にする——
/// 矛盾した保証主張を黙って安全側へ丸めると、保証の出所の誤りが隠れる。
/// </summary>
public static class AttemptFaultClassifier
{
    public static AttemptState Classify(AttemptFaultPoint faultPoint, ExternalInputCallState callState)
    {
        if (faultPoint == AttemptFaultPoint.PartialSendInput)
        {
            if (callState == ExternalInputCallState.ProvablyNotCalled)
            {
                throw new ArgumentException(
                    "partial SendInput は外部入力 API が呼ばれた事実そのものであり、「未呼出の保証」と両立しません。",
                    nameof(callState));
            }

            return AttemptState.OutcomeUnknown;
        }

        return callState == ExternalInputCallState.ProvablyNotCalled
            ? AttemptState.Disarmed
            : AttemptState.OutcomeUnknown;
    }
}
