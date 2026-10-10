using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Host;

/// <summary>クリックが送出された後のキーを、次の入力可能な観測へ引き継ぐ。</summary>
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
        if (receipt.Status == GameInteractionDispatchStatus.Dispatched && choice.AfterClickKey is not null)
            Pending = choice with
            {
                Action = VisualProgressAction.Key, Key = choice.AfterClickKey, Point = null, AfterClickKey = null,
            };
        else if (receipt.Status == GameInteractionDispatchStatus.Dispatched && choice == Pending)
            Pending = null;
        return receipt;
    }
}
