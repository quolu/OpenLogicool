using OpenLogicool.Contracts.AI;
using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.AI;

/// <summary>Phase 5 の凍結 corpus を入力として proposal 評価値だけを集計する。</summary>
public sealed record FrozenEvaluationCase(
    string CorpusItemId,
    PlannerContext Context,
    string? ExpectedActionKey);

public sealed record EvaluationResponse(
    NextActionProposal? Proposal,
    long LatencyMs,
    decimal CostUsd);

/// <summary>未選定 provider の代わりに、評価対象を注入するための狭い口。</summary>
public interface IFrozenProposalEvaluator
{
    EvaluationResponse Evaluate(PlannerContext context, CancellationToken cancellationToken);
}

public sealed record FrozenEvaluationReport(
    int ProcessedCases,
    int KnownCases,
    int CorrectKnownActions,
    int UnknownCases,
    int CorrectUnknownRejections,
    long TotalLatencyMs,
    decimal TotalCostUsd,
    bool Cancelled)
{
    public decimal? KnownActionAccuracy => KnownCases == 0
        ? null
        : decimal.Divide(CorrectKnownActions, KnownCases);

    public decimal? UnknownRejectionRate => UnknownCases == 0
        ? null
        : decimal.Divide(CorrectUnknownRejections, UnknownCases);
}

public static class EvalHarness
{
    public static FrozenEvaluationReport Measure(
        IReadOnlyList<FrozenEvaluationCase> frozenCorpus,
        IFrozenProposalEvaluator evaluator,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frozenCorpus);
        ArgumentNullException.ThrowIfNull(evaluator);
        if (frozenCorpus.Count == 0
            || frozenCorpus.Any(item => string.IsNullOrWhiteSpace(item.CorpusItemId))
            || frozenCorpus.Select(item => item.CorpusItemId).Distinct(StringComparer.Ordinal).Count() != frozenCorpus.Count)
        {
            throw new ArgumentException("frozen corpus は一意な item を一件以上持つ必要があります。", nameof(frozenCorpus));
        }

        var knownCases = 0;
        var correctKnownActions = 0;
        var unknownCases = 0;
        var correctUnknownRejections = 0;
        var totalLatencyMs = 0L;
        var totalCostUsd = 0m;
        var processedCases = 0;

        foreach (var item in frozenCorpus)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Report(cancelled: true);
            }

            PlannerProposalSchema.Validate(item.Context);
            var response = evaluator.Evaluate(item.Context, cancellationToken);
            if (response.LatencyMs < 0 || response.CostUsd < 0)
            {
                throw new ArgumentException("評価 response の latency と cost は負にできません。", nameof(evaluator));
            }

            if (response.Proposal is not null)
            {
                PlannerProposalSchema.Validate(response.Proposal);
            }

            processedCases++;
            totalLatencyMs += response.LatencyMs;
            totalCostUsd += response.CostUsd;
            if (item.ExpectedActionKey is null)
            {
                unknownCases++;
                correctUnknownRejections += response.Proposal is null ? 1 : 0;
            }
            else
            {
                knownCases++;
                correctKnownActions += response.Proposal is not null && string.Equals(
                    item.ExpectedActionKey,
                    ActionKey(response.Proposal.Action),
                    StringComparison.Ordinal)
                    ? 1
                    : 0;
            }
        }

        return Report(cancelled: false);

        FrozenEvaluationReport Report(bool cancelled) => new(
            processedCases,
            knownCases,
            correctKnownActions,
            unknownCases,
            correctUnknownRejections,
            totalLatencyMs,
            totalCostUsd,
            cancelled);
    }

    public static string ActionKey(ProposalAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return action switch
        {
            VerifiedRunAction verified => verified.SemanticActionId,
            TeachAction teach => $"{teach.VisualTargetRef}|{teach.Primitive}",
            _ => throw new ArgumentException("未対応の proposal action です。", nameof(action)),
        };
    }
}
