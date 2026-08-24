using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.AI;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Exploration;
using OpenLogicool.GameLab;
using OpenLogicool.Persistence;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.GameLab.Discovery.Tests;

public sealed class HiddenOracleDiscoveryTests
{
    [Fact]
    public void Pixel_only_recognizer_exposes_no_oracle_state_or_action_seed()
    {
        var game = new HiddenOracleDiscoveryGame();
        var scene = new ZeroSeedFrameRecognizer().Observe(game.Capture(), "observation-1");
        var serialized = JsonSerializer.Serialize(scene);

        Assert.Equal(StateIdentityStatus.Novel, scene.StateIdentity);
        Assert.Equal(2, scene.Affordances.Count);
        Assert.All(scene.Affordances, candidate => Assert.Equal(["click"], candidate.AllowedPrimitives));
        Assert.DoesNotContain("oracle-alpha", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("oracle-beta", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("oracle-gamma", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_database_discovers_three_nodes_two_edges_no_change_and_loop()
    {
        using var database = new DiscoveryDatabase();
        var game = new HiddenOracleDiscoveryGame();
        using var session = database.Open(game, "discovery-session");
        var scene = session.ObserveAndCommit();
        _ = session.EnsureNode(scene, scene.ObservationId);

        var noChange = session.Probe(scene, scene.Affordances[1]);
        Assert.Equal(ExplorationOutcomeKind.NoChange, noChange.Evidence.Outcome);
        session.CommitTransition(scene, noChange.After, noChange.Evidence);

        var toSecond = session.Probe(noChange.After, noChange.After.Affordances[0]);
        Assert.Equal(ExplorationOutcomeKind.Novel, toSecond.Evidence.Outcome);
        session.CommitTransition(noChange.After, toSecond.After, toSecond.Evidence);

        var toThird = session.Probe(toSecond.After, toSecond.After.Affordances[0]);
        session.CommitTransition(toSecond.After, toThird.After, toThird.Evidence);

        var loop = session.Probe(toThird.After, toThird.After.Affordances[0]);
        session.CommitTransition(toThird.After, loop.After, loop.Evidence);

        var revision = session.StructureStore.LoadRevision(DiscoverySession.GameId, DiscoverySession.Environment);
        var audit = game.ReadOracleAudit();
        Assert.Equal(3, revision.ScreenGraph.Nodes.Count);
        Assert.True(revision.ScreenGraph.Edges.Count >= 2);
        Assert.Contains(revision.ScreenGraph.Edges, edge =>
            edge.SourceStateId == edge.DestinationStateId
            && edge.OutcomeCounts.Any(count => count.Outcome == ExplorationOutcomeKind.NoChange));
        Assert.Equal(3, audit.VisitedStateIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(1, audit.NoChangeClicks);
        Assert.Equal("oracle-alpha", audit.VisitedStateIds[^1]);
        Assert.All(revision.ScreenGraph.Nodes, node => Assert.Equal(StructureVerificationState.Candidate, node.VerificationState));
    }

    [Fact]
    public void Crash_after_input_is_not_retried_and_restarts_as_outcome_unknown()
    {
        using var database = new DiscoveryDatabase();
        var game = new HiddenOracleDiscoveryGame();
        using (var session = database.Open(game, "crash-session"))
        {
            var scene = session.ObserveAndCommit();
            _ = session.EnsureNode(scene, scene.ObservationId);
            var proposal = session.Propose(scene, scene.Affordances[0]);
            game.ArmCrashAfterNextClick();

            Assert.Throws<InvalidOperationException>(() => session.Dispatch(proposal, scene.Affordances[0]));
            Assert.Equal(1, game.ReadOracleAudit().AcceptedClicks);
        }

        using var connection = database.OpenConnection();
        var structure = new SqliteGameStructureStore(connection)
            .LoadRevision(DiscoverySession.GameId, DiscoverySession.Environment);
        var runStore = new SqliteRunJournalStore(connection);
        var gate = AttemptDispatchGate.Recover(runStore, RunJournal.Restore(runStore, new NoopLog()));
        Assert.Single(structure.Dispatches);
        Assert.Equal(ExplorationOutcomeKind.OutcomeUnknown, structure.Dispatches[0].Outcome);
        Assert.Equal(AttemptState.OutcomeUnknown, Assert.Single(gate.Attempts).State);
    }

    [Fact]
    public void Capture_loss_records_unavailable_without_confirmation_or_retry()
    {
        using var database = new DiscoveryDatabase();
        var game = new HiddenOracleDiscoveryGame();
        using var session = database.Open(game, "capture-loss-session");
        var scene = session.ObserveAndCommit();
        _ = session.EnsureNode(scene, scene.ObservationId);
        var proposal = session.Propose(scene, scene.Affordances[0]);
        session.Dispatch(proposal, scene.Affordances[0]);
        game.SetCaptureAvailability(CaptureAvailability.Unavailable);
        var after = session.ObserveWithoutCommit();

        var evidence = session.Record(proposal, after, ExplorationOutcomeKind.Unavailable, stableFrames: 0, stableMs: 0);

        Assert.Equal(ExplorationOutcomeKind.Unavailable, evidence.Outcome);
        Assert.Equal(1, game.ReadOracleAudit().AcceptedClicks);
        Assert.Equal(AttemptState.OutcomeUnknown, session.RecoverGate().Attempts.Single().State);
        Assert.DoesNotContain(
            session.RunStore.ReadRun(session.RunId),
            item => item.PayloadType == RunEventPayloadTypes.Confirmation);
    }

    [Fact]
    public void Stale_frame_stops_with_zero_dispatch()
    {
        using var database = new DiscoveryDatabase();
        var game = new HiddenOracleDiscoveryGame();
        game.SetFreshness(999);
        using var session = database.Open(game, "stale-session");
        var scene = session.ObserveAndCommit();

        var decision = session.Coordinator.Propose(session.Admission(scene, scene.Affordances[0], "proposal-stale"), Time(2));

        Assert.Equal(ExplorationAdmissionStatus.Stopped, decision.Status);
        Assert.Equal(ExplorationStopReason.StaleFrame, decision.Reason);
        Assert.Equal(0, game.ReadOracleAudit().AcceptedClicks);
    }

    [Fact]
    public void Budget_exhaustion_stops_with_zero_dispatch()
    {
        using var database = new DiscoveryDatabase();
        var game = new HiddenOracleDiscoveryGame();
        using var session = database.Open(game, "budget-session");
        var scene = session.ObserveAndCommit();
        var admission = session.Admission(scene, scene.Affordances[0], "proposal-budget") with
        {
            ElapsedMilliseconds = 60_001,
        };

        var decision = session.Coordinator.Propose(admission, Time(2));

        Assert.Equal(ExplorationStopReason.BudgetExhausted, decision.Reason);
        Assert.Equal(0, game.ReadOracleAudit().AcceptedClicks);
    }

    [Fact]
    public void Recovery_loss_stops_with_zero_dispatch()
    {
        using var database = new DiscoveryDatabase();
        var game = new HiddenOracleDiscoveryGame();
        using var session = database.Open(game, "recovery-session");
        var scene = session.ObserveAndCommit();
        var admission = session.Admission(scene, scene.Affordances[0], "proposal-recovery") with
        {
            Risk = session.Risk(scene.Affordances[0]) with { RecoveryEdgeIds = ["recovery:missing"] },
        };

        var decision = session.Coordinator.Propose(admission, Time(2));

        Assert.Equal(ExplorationStopReason.RecoveryLost, decision.Reason);
        Assert.Equal(0, game.ReadOracleAudit().AcceptedClicks);
    }

    [Fact]
    public void Independent_sessions_promote_candidate_to_replayed_then_verified()
    {
        using var database = new DiscoveryDatabase();
        string stateId;
        using (var discovery = database.Open(new HiddenOracleDiscoveryGame(), "discovery-session"))
        {
            var before = discovery.ObserveAndCommit();
            stateId = discovery.EnsureNode(before, before.ObservationId);
            var result = discovery.Probe(before, before.Affordances[0]);
            discovery.CommitTransition(before, result.After, result.Evidence);
        }

        string replayEvidence;
        using (var replay = database.Open(new HiddenOracleDiscoveryGame(), "replay-session-1"))
        {
            var before = replay.ObserveAndCommit();
            var reidentifiedStateId = replay.FindStateId(before);
            Assert.Equal(stateId, reidentifiedStateId);
            replayEvidence = replay.Probe(before, before.Affordances[0]).Evidence.EvidenceId;
            var revision = replay.Verification.Promote(
                Verification(reidentifiedStateId, StructureVerificationState.Replayed, "discovery-session", replay.RunId, replayEvidence),
                DiscoverySession.GameId,
                DiscoverySession.Environment);
            Assert.Equal(StructureVerificationState.Replayed, revision.ScreenGraph.Nodes.Single(node => node.StateId == stateId).VerificationState);
        }

        using (var verify = database.Open(new HiddenOracleDiscoveryGame(), "replay-session-2"))
        {
            var before = verify.ObserveAndCommit();
            var reidentifiedStateId = verify.FindStateId(before);
            Assert.Equal(stateId, reidentifiedStateId);
            var evidence = verify.Probe(before, before.Affordances[0]).Evidence.EvidenceId;
            var revision = verify.Verification.Promote(
                Verification(reidentifiedStateId, StructureVerificationState.Verified, "discovery-session", verify.RunId, evidence),
                DiscoverySession.GameId,
                DiscoverySession.Environment);
            Assert.Equal(StructureVerificationState.Verified, revision.ScreenGraph.Nodes.Single(node => node.StateId == stateId).VerificationState);
        }

        Assert.NotEqual(replayEvidence, string.Empty);
    }

    [Fact]
    public void Verification_rejects_skipped_state_and_same_session()
    {
        using var database = new DiscoveryDatabase();
        using var discovery = database.Open(new HiddenOracleDiscoveryGame(), "discovery-session");
        var before = discovery.ObserveAndCommit();
        var stateId = discovery.EnsureNode(before, before.ObservationId);

        Assert.Throws<InvalidOperationException>(() => discovery.Verification.Promote(
            Verification(
                stateId,
                StructureVerificationState.Verified,
                "discovery-session",
                "replay-session",
                "missing-evidence"),
            DiscoverySession.GameId,
            DiscoverySession.Environment));
        Assert.Throws<ArgumentException>(() => discovery.Verification.Promote(
            Verification(
                stateId,
                StructureVerificationState.Replayed,
                "discovery-session",
                "discovery-session",
                "missing-evidence"),
            DiscoverySession.GameId,
            DiscoverySession.Environment));
        Assert.Equal(
            StructureVerificationState.Candidate,
            discovery.StructureStore.LoadRevision(DiscoverySession.GameId, DiscoverySession.Environment)
                .ScreenGraph.Nodes.Single(node => node.StateId == stateId).VerificationState);
    }

    private static StructureVerificationRequest Verification(
        string stateId,
        StructureVerificationState requested,
        string discoverySession,
        string replaySession,
        string evidenceId) => new(
        ContractSchemaVersions.Revision03,
        StructureEntityKind.Node,
        stateId,
        requested,
        discoverySession,
        replaySession,
        [evidenceId],
        $"correlation:{replaySession}",
        evidenceId,
        Time(20),
        Time(20));

    private static DateTimeOffset Time(long seconds) => DateTimeOffset.UnixEpoch.AddSeconds(seconds);

    private sealed class DiscoveryDatabase : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"openlogicool-hidden-oracle-{Guid.NewGuid():N}.db");

        public DiscoverySession Open(IGameLabVisualSurface surface, string runId) =>
            new(OpenConnection(), surface, runId);

        public SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection($"Data Source={path}");
            connection.Open();
            new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
            return connection;
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private sealed class DiscoverySession : IDisposable
    {
        public const string GameId = "hidden-oracle-game";
        public const string Environment = "hidden-oracle-env";
        private readonly SqliteConnection connection;
        private readonly IGameLabVisualSurface surface;
        private readonly ZeroSeedFrameRecognizer recognizer = new();
        private readonly InMemoryStableStructureIdRegistry ids = new();
        private readonly SequenceIds eventIds;
        private int observationSequence;
        private int proposalSequence;

        public DiscoverySession(SqliteConnection connection, IGameLabVisualSurface surface, string runId)
        {
            this.connection = connection;
            this.surface = surface;
            RunId = runId;
            eventIds = new SequenceIds(runId);
            StructureStore = new SqliteGameStructureStore(connection);
            RunStore = new SqliteRunJournalStore(connection);
            var journal = new RunJournal(RunStore, new NoopLog());
            AttemptGate = new AttemptDispatchGate(journal);
            Policy = new ExplorationPolicy(
                ContractSchemaVersions.Revision03,
                $"policy:{runId}",
                "app:hidden-oracle",
                "window:hidden-oracle",
                Environment,
                "generic-visual-surface",
                ["click"],
                ["purchase", "delete", "account-change"],
                new ExplorationBudget(ContractSchemaVersions.Revision03, 12, 5_000, 60_000),
                false,
                $"consent:{runId}",
                "recovery:reset",
                new ExplorationStopPolicy(ContractSchemaVersions.Revision03, 500, 4, 3, 3),
                ["capture-unavailable", "stale", "budget", "recovery-lost"]);
            Coordinator = new ExplorationCoordinator(
                StructureStore,
                journal,
                AttemptGate,
                new ExplorationRunBinding(
                    ContractSchemaVersions.Revision03,
                    runId,
                    GameId,
                    Environment,
                    "zero-seed-exploration",
                    "zero-seed-exploration-v1",
                    1),
                Policy,
                eventIds);
            Knowledge = new StructureKnowledgeController(StructureStore, ids, eventIds);
            Verification = new StructureVerificationController(StructureStore, eventIds);
        }

        public string RunId { get; }
        public ExplorationPolicy Policy { get; }
        public ExplorationCoordinator Coordinator { get; }
        public AttemptDispatchGate AttemptGate { get; }
        public SqliteGameStructureStore StructureStore { get; }
        public SqliteRunJournalStore RunStore { get; }
        public StructureKnowledgeController Knowledge { get; }
        public StructureVerificationController Verification { get; }

        public ObservedScene ObserveAndCommit()
        {
            var scene = ObserveWithoutCommit();
            Coordinator.CommitObservation(scene, Time(observationSequence));
            return scene;
        }

        public ObservedScene ObserveWithoutCommit() =>
            recognizer.Observe(surface.Capture(), $"{RunId}:observation:{++observationSequence}");

        public ExplorationProposal Propose(ObservedScene scene, AffordanceCandidate candidate)
        {
            var proposalId = $"{RunId}:proposal:{++proposalSequence}";
            var admission = Admission(scene, candidate, proposalId);
            var decision = Coordinator.Propose(admission, Time(2 + proposalSequence));
            Assert.Equal(ExplorationAdmissionStatus.Allowed, decision.Status);
            return admission.Proposal;
        }

        public void Dispatch(ExplorationProposal proposal, AffordanceCandidate candidate)
        {
            var bounds = candidate.Locator.NormalizedBounds;
            Coordinator.Dispatch(
                proposal.ProposalId,
                () => surface.Click(bounds[0] + (bounds[2] / 2), bounds[1] + (bounds[3] / 2)),
                Time(5 + proposalSequence));
        }

        public ProbeResult Probe(ObservedScene before, AffordanceCandidate candidate)
        {
            var proposal = Propose(before, candidate);
            Dispatch(proposal, candidate);
            var after = ObserveWithoutCommit();
            var outcome = string.Equals(before.StateHypothesisId, after.StateHypothesisId, StringComparison.Ordinal)
                ? ExplorationOutcomeKind.NoChange
                : ExplorationOutcomeKind.Novel;
            return new ProbeResult(after, Record(proposal, after, outcome, 3, 300));
        }

        public TransitionEvidence Record(
            ExplorationProposal proposal,
            ObservedScene after,
            ExplorationOutcomeKind outcome,
            int stableFrames,
            long stableMs) => Coordinator.RecordOutcome(new ExplorationOutcomeReport(
            ContractSchemaVersions.Revision03,
            proposal.ProposalId,
            after,
            outcome,
            stableFrames,
            stableMs,
            $"{RunId}:transition:{proposalSequence}",
            1_000 + proposalSequence,
            1_500 + proposalSequence,
            Time(10 + proposalSequence)));

        public string EnsureNode(ObservedScene scene, string evidenceId)
        {
            var current = StructureStore.LoadRevision(GameId, Environment);
            var existing = current.ScreenGraph.Nodes.SingleOrDefault(node =>
                node.SceneSignatureIds.Contains(scene.StateHypothesisId!, StringComparer.Ordinal));
            if (existing is not null)
            {
                return existing.StateId;
            }

            var alias = $"candidate:{scene.SceneId}";
            var stateId = ids.Issue(StructureEntityKind.Node);
            var operation = new StructureDeltaOperation(
                ContractSchemaVersions.Revision03,
                StructureDeltaKind.CreateNode,
                alias,
                null,
                null,
                null,
                null);
            var node = new StructureScreenNode(
                ContractSchemaVersions.Revision03,
                stateId,
                Environment,
                [scene.StateHypothesisId!],
                [],
                [evidenceId],
                null,
                StructureVerificationState.Candidate);
            var mutation = new StructureMutation(
                ContractSchemaVersions.Revision03,
                StructureMutationKind.UpsertNode,
                StructureEntityKind.Node,
                stateId,
                [],
                node,
                null,
                null,
                null,
                null,
                null,
                [evidenceId],
                "pixel signatureからnode candidateを作成");
            _ = Knowledge.Commit(
                Delta(
                    current.RevisionId,
                    [operation],
                    new Dictionary<string, string>(StringComparer.Ordinal) { [alias] = stateId },
                    [new MaterializedStructureDeltaOperation(operation, mutation)],
                    [evidenceId]),
                GameId,
                Environment);
            _ = Coordinator.SynchronizeStructureRevision();
            return stateId;
        }

        public string FindStateId(ObservedScene scene) =>
            StructureStore.LoadRevision(GameId, Environment).ScreenGraph.Nodes
                .Single(node => node.SceneSignatureIds.Contains(scene.StateHypothesisId!, StringComparer.Ordinal))
                .StateId;

        public void CommitTransition(ObservedScene before, ObservedScene after, TransitionEvidence evidence)
        {
            var sourceId = EnsureNode(before, evidence.EvidenceId);
            var destinationId = EnsureNode(after, evidence.EvidenceId);
            var current = StructureStore.LoadRevision(GameId, Environment);
            var candidate = before.Affordances.Single(item => item.CandidateId == evidence.AffordanceCandidateId);
            var edgeId = ids.Issue(StructureEntityKind.Edge);
            var operation = new StructureDeltaOperation(
                ContractSchemaVersions.Revision03,
                StructureDeltaKind.AttributeEdge,
                sourceId,
                destinationId,
                null,
                null,
                null);
            var edge = new StructureScreenEdge(
                ContractSchemaVersions.Revision03,
                edgeId,
                sourceId,
                destinationId,
                before.StateHypothesisId,
                candidate.CandidateId,
                candidate.Locator.LocatorRevision,
                evidence.Primitive,
                "deterministic-low-risk",
                [],
                true,
                before.ObservationId,
                after.ObservationId,
                new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 3, 300, 5_000),
                [new StructureOutcomeCount(evidence.Outcome, 1)],
                [evidence.EvidenceId],
                StructureVerificationState.Candidate);
            var mutation = new StructureMutation(
                ContractSchemaVersions.Revision03,
                StructureMutationKind.UpsertEdge,
                StructureEntityKind.Edge,
                edgeId,
                [],
                null,
                edge,
                null,
                null,
                null,
                null,
                [evidence.EvidenceId],
                "前後Observationからedge candidateを作成");
            _ = Knowledge.Commit(
                Delta(
                    current.RevisionId,
                    [operation],
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    [new MaterializedStructureDeltaOperation(operation, mutation)],
                    [evidence.EvidenceId]),
                GameId,
                Environment);
            _ = Coordinator.SynchronizeStructureRevision();
        }

        public ExplorationProposalAdmission Admission(
            ObservedScene scene,
            AffordanceCandidate candidate,
            string proposalId) => new(
            new ExplorationContext(
                ContractSchemaVersions.Revision03,
                $"context:{proposalId}",
                Policy,
                scene,
                Coordinator.CurrentStructureRevisionId,
                scene.Affordances.Select(item => item.CandidateId).ToArray(),
                ["recovery:reset"],
                new ExplorationBudget(
                    ContractSchemaVersions.Revision03,
                    Coordinator.RemainingProbes,
                    5_000,
                    60_000)),
            new ExplorationProposal(
                ContractSchemaVersions.Revision03,
                proposalId,
                scene.ObservationId,
                Coordinator.CurrentStructureRevisionId,
                candidate.CandidateId,
                "click",
                "frame差分を再観測する",
                Enum.GetValues<ExplorationOutcomeKind>(),
                new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 3, 300, 5_000),
                ["capture-unavailable", "budget", "recovery-lost"]),
            Risk(candidate),
            true,
            true,
            1_000,
            100);

        public ExplorationRiskAssessment Risk(AffordanceCandidate candidate) => new(
            ContractSchemaVersions.Revision03,
            candidate.CandidateId,
            ExplorationRiskLevel.Low,
            [],
            true,
            true,
            ["recovery:reset"],
            "hidden-oracle-risk-v1");

        public AttemptDispatchGate RecoverGate() =>
            AttemptDispatchGate.Recover(RunStore, RunJournal.Restore(RunStore, new NoopLog()));

        public void Dispose() => connection.Dispose();

        private StructureDeltaCommitRequest Delta(
            string revisionId,
            IReadOnlyList<StructureDeltaOperation> operations,
            IReadOnlyDictionary<string, string> aliases,
            IReadOnlyList<MaterializedStructureDeltaOperation> materialized,
            IReadOnlyList<string> evidenceIds) => new(
            ContractSchemaVersions.Revision03,
            new StructureDeltaProposal(
                ContractSchemaVersions.Revision03,
                eventIds.Next("delta"),
                revisionId,
                evidenceIds,
                operations),
            aliases,
            materialized,
            eventIds.Next("correlation"),
            evidenceIds[0],
            Time(15),
            Time(15));
    }

    private sealed record ProbeResult(ObservedScene After, TransitionEvidence Evidence);

    private sealed class SequenceIds(string scope) : IExplorationIdSource
    {
        private int value;
        public string Next(string prefix) => $"{scope}:{prefix}:{++value}";
    }

    private sealed class NoopLog : IEngineeringLogSink
    {
        public void Record(EngineeringLogEntry entry)
        {
        }
    }
}
