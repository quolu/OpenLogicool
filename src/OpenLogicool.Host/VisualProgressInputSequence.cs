using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Host;

/// <summary>クリックやキーが送出された後に続けて送るキーを、次の入力可能な観測へ一つずつ引き継ぐ。</summary>
internal sealed class VisualProgressInputSequence(NanoGameInteractionActions actions)
{
    public VisualProgressChoice? Pending { get; private set; }

    public GameInteractionDispatchReceipt Dispatch(VisualProgressChoice choice, ObservationResult current)
    {
        var frame = current.Frame;
        var receipt = choice.Action == VisualProgressAction.Key
            ? actions.KeyTap(new GameInteractionKeyTapRequest(ContractSchemaVersions.Revision03,
                current.ObservationId, frame.Sequence, frame.TransformRevision, frame.SourceId, [choice.Key!]), current)
            : actions.Click(new GameInteractionTargetBinding(ContractSchemaVersions.Revision03,
                current.ObservationId, frame.Sequence, frame.TransformRevision, frame.SourceId,
                choice.RuleId!, "visual-progress-v1", [choice.Point![0] - 0.0005, choice.Point[1] - 0.0005, 0.001, 0.001]), current);
        string[] following = [.. choice.AfterClickKey is null ? [] : new[] { choice.AfterClickKey }, .. choice.ThenKeys ?? []];
        if (receipt.Status == GameInteractionDispatchStatus.Dispatched && following.Length > 0)
            Pending = choice with
            {
                Action = VisualProgressAction.Key, Key = following[0], Point = null, AfterClickKey = null,
                ThenKeys = following.Length > 1 ? following[1..] : null,
            };
        else if (receipt.Status == GameInteractionDispatchStatus.Dispatched && choice == Pending)
            Pending = null;
        return receipt;
    }
}
