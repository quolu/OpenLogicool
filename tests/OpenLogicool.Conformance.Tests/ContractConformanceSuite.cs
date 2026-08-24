using OpenLogicool.Contracts.AI;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Perception;

namespace OpenLogicool.Conformance.Tests;

public static class ContractConformanceSuite
{
    public static void Verify(ObservationResult observation)
    {
        if (string.IsNullOrWhiteSpace(observation.ObservationId))
        {
            throw new InvalidOperationException("ObservationResult には observationId が必要です（RunEvent からの参照キー）。");
        }

        if (observation.CaptureAvailability == CaptureAvailability.Available
            && observation.CaptureFailureReason is not null)
        {
            throw new InvalidOperationException("Available ObservationResult は capture failure reason を持てません。");
        }

        if (observation.CaptureAvailability != CaptureAvailability.Available
            && string.IsNullOrWhiteSpace(observation.CaptureFailureReason))
        {
            throw new InvalidOperationException("Unavailable／Stale ObservationResult には capture failure reason が必要です。");
        }

        if (observation.CaptureAvailability != CaptureAvailability.Available
            && (observation.StateIdentity != StateIdentityStatus.InsufficientEvidence
                || observation.StateCandidates.Count > 0))
        {
            throw new InvalidOperationException("Unavailable／Stale ObservationResult はstateを同定できません。");
        }

        if (observation.StateIdentity == StateIdentityStatus.Known && observation.StateCandidates.Count != 1)
        {
            throw new InvalidOperationException("Known ObservationResult には唯一のstate candidateが必要です。");
        }

        if (observation.StateIdentity == StateIdentityStatus.Ambiguous && observation.StateCandidates.Count < 2)
        {
            throw new InvalidOperationException("Ambiguous はstate candidateを2つ以上保持する必要があります。");
        }

        if (observation.StateIdentity is StateIdentityStatus.Novel or StateIdentityStatus.InsufficientEvidence
            && observation.StateCandidates.Count > 0)
        {
            throw new InvalidOperationException("Novel／InsufficientEvidence は既知state candidateを持てません。");
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
