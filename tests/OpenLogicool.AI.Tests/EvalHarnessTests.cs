using OpenLogicool.AI;
using OpenLogicool.Contracts.AI;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using Xunit;

namespace OpenLogicool.AI.Tests;

public sealed class EvalHarnessTests
{
    [Fact]
    public void 凍結した既知と未知の評価値を集計する()
    {
        var report = EvalHarness.Measure(
            [
                Case("phase5:menu", "action.open-menu"),
                Case("phase5:unknown", expectedActionKey: null),
            ],
            new FixedEvaluator(
                Response("action.open-menu", 11, .02m),
                new EvaluationResponse(null, 7, 0m)));

        Assert.Equal(2, report.ProcessedCases);
        Assert.Equal(1m, report.KnownActionAccuracy);
        Assert.Equal(1m, report.UnknownRejectionRate);
        Assert.Equal(18, report.TotalLatencyMs);
        Assert.Equal(.02m, report.TotalCostUsd);
        Assert.False(report.Cancelled);
    }

    [Fact]
    public void cancel_済みなら評価器を呼ばない()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var evaluator = new FixedEvaluator(Response("action.open-menu", 1, 0m));

        var report = EvalHarness.Measure([Case("phase5:menu", "action.open-menu")], evaluator, cancellation.Token);

        Assert.True(report.Cancelled);
        Assert.Equal(0, report.ProcessedCases);
        Assert.Equal(0, evaluator.CallCount);
    }

    private static FrozenEvaluationCase Case(string corpusItemId, string? expectedActionKey) => new(
        corpusItemId,
        new PlannerContext(
            ContractSchemaVersions.Revision01,
            "次の画面へ進む",
            corpusItemId,
            ["action.open-menu"],
            "Phase 5 frozen corpus",
            new PlannerBudget(ContractSchemaVersions.Revision01, 1, 1m)),
        expectedActionKey);

    private static EvaluationResponse Response(string actionId, long latencyMs, decimal costUsd) => new(
        new NextActionProposal(
            ContractSchemaVersions.Revision01,
            $"proposal:{actionId}",
            Case("proposal", actionId).Context,
            ProposalMode.VerifiedRun,
            new VerifiedRunAction(ContractSchemaVersions.Revision01, actionId),
            new ProposalPrecondition(ContractSchemaVersions.Revision01, "state:before", 10),
            new ProposalExpectedOutcome(
                ContractSchemaVersions.Revision01,
                "state:after",
                new StabilityWindow(ContractSchemaVersions.Revision01, 1, 1)),
            new ProposalStopCondition(ContractSchemaVersions.Revision01, 100, "pause"),
            new ProposalValidity(ContractSchemaVersions.Revision01, 1, 1)),
        latencyMs,
        costUsd);

    private sealed class FixedEvaluator(params EvaluationResponse[] responses) : IFrozenProposalEvaluator
    {
        private readonly Queue<EvaluationResponse> _responses = new(responses);

        public int CallCount { get; private set; }

        public EvaluationResponse Evaluate(PlannerContext context, CancellationToken cancellationToken)
        {
            CallCount++;
            return _responses.Dequeue();
        }
    }
}
