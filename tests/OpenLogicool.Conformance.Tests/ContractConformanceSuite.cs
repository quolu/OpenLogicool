using OpenLogicool.Contracts.AI;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Perception;

namespace OpenLogicool.Conformance.Tests;

public static class ContractConformanceSuite
{
    public static void Verify(ObservationResult observation)
    {
        if (observation.Status == ObservationStatus.Known && observation.StateCandidates.Count == 0)
        {
            throw new InvalidOperationException("Known ObservationResult には state candidate が必要です。");
        }

        if (observation.Status == ObservationStatus.Unavailable && observation.UnavailableReason is null)
        {
            throw new InvalidOperationException("Unavailable ObservationResult には unavailable reason が必要です。");
        }

        if (observation.StateCandidates.Any(candidate => candidate.Confidence < 0 || candidate.Confidence > 1))
        {
            throw new InvalidOperationException("state candidate confidence は [0, 1] の範囲でなければなりません。");
        }
    }

    public static void Verify(NextActionProposal proposal)
    {
        if (proposal.Mode == ProposalMode.VerifiedRun && proposal.Action is not VerifiedRunAction)
        {
            throw new InvalidOperationException("VerifiedRun proposal の action は VerifiedRunAction でなければなりません。");
        }

        if (proposal.Mode == ProposalMode.Teach && proposal.Action is not TeachAction)
        {
            throw new InvalidOperationException("Teach proposal の action は TeachAction でなければなりません。");
        }

        if (proposal.Validity.FrameSequence < 0 || proposal.Validity.TransformRevision < 0)
        {
            throw new InvalidOperationException("proposal validity は負にできません。");
        }
    }

    public static void Verify(CapturedFrame frame)
    {
        if (frame.FreshnessMs < 0 || frame.LastChangeMs < 0)
        {
            throw new InvalidOperationException("frame freshness と last change は負にできません。");
        }

        if (frame.Width <= 0 || frame.Height <= 0)
        {
            throw new InvalidOperationException("frame width と height は正でなければなりません。");
        }
    }
}
