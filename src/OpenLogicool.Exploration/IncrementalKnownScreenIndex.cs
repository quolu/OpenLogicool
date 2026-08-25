using System.Security.Cryptography;
using System.Text;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Exploration;

public sealed record KnownScreenActionReference(
    string PageStateId,
    string ActionId,
    string? DestinationStateId);

public interface IIncrementalKnownScreenIndex
{
    KnownScreenActionReference RememberControl(
        ObservedScene page,
        AffordanceCandidate control,
        string evidenceId);

    KnownScreenActionReference RememberDestination(
        ObservedScene before,
        AffordanceCandidate control,
        IReadOnlyList<ObservedScene> destinationSamples,
        string evidenceId);
}

/// <summary>目的達成中に見つけた一つのcontrolだけを、AIなし再照合用profileへ逐次追記する。</summary>
public sealed class IncrementalKnownScreenIndex(
    ILearnedSceneProfileStore store,
    string gameId,
    string environmentScope,
    string processName,
    string? windowTitle = null) : IIncrementalKnownScreenIndex
{
    public KnownScreenActionReference RememberControl(
        ObservedScene page,
        AffordanceCandidate control,
        string evidenceId)
    {
        Validate(page, control, evidenceId);
        var document = store.Load(gameId, environmentScope) ?? Empty(evidenceId);
        var (state, states) = EnsureState(document.States, page, evidenceId);
        var action = Action(state.StateId, control, evidenceId, destinationStateId: null);
        var merged = state with
        {
            Affordances = state.Affordances
                .Where(existing => !string.Equals(existing.CandidateId, action.CandidateId, StringComparison.Ordinal))
                .Append(action)
                .ToArray(),
            EvidenceIds = state.EvidenceIds.Append(evidenceId).Distinct(StringComparer.Ordinal).ToArray(),
        };
        store.Upsert(document with
        {
            States = Replace(states, merged),
            EvidenceIds = document.EvidenceIds.Append(evidenceId).Distinct(StringComparer.Ordinal).ToArray(),
        });
        return new KnownScreenActionReference(state.StateId, action.CandidateId, null);
    }

    public KnownScreenActionReference RememberDestination(
        ObservedScene before,
        AffordanceCandidate control,
        IReadOnlyList<ObservedScene> destinationSamples,
        string evidenceId)
    {
        ArgumentNullException.ThrowIfNull(destinationSamples);
        if (destinationSamples.Count == 0)
        {
            throw new ArgumentException("行き先索引には1件以上の安定観測が必要です。", nameof(destinationSamples));
        }
        var source = RememberControl(before, control, evidenceId);
        var document = store.Load(gameId, environmentScope)
            ?? throw new InvalidOperationException("保存直後のknown screen indexを読み出せません。");
        var (destination, states) = EnsureState(document.States, destinationSamples, evidenceId);
        var sourceState = states.Single(state => state.StateId == source.PageStateId);
        var action = sourceState.Affordances.Single(item => item.CandidateId == source.ActionId) with
        {
            DestinationStateId = destination.StateId,
            EvidenceIds = sourceState.Affordances.Single(item => item.CandidateId == source.ActionId)
                .EvidenceIds.Append(evidenceId).Distinct(StringComparer.Ordinal).ToArray(),
        };
        var updatedSource = sourceState with
        {
            Affordances = sourceState.Affordances
                .Where(item => item.CandidateId != source.ActionId)
                .Append(action)
                .ToArray(),
        };
        store.Upsert(document with
        {
            States = Replace(Replace(states, destination), updatedSource),
            EvidenceIds = document.EvidenceIds.Append(evidenceId).Distinct(StringComparer.Ordinal).ToArray(),
        });
        return source with { DestinationStateId = destination.StateId };
    }

    private (LearnedStateSceneSignature State, IReadOnlyList<LearnedStateSceneSignature> States) EnsureState(
        IReadOnlyList<LearnedStateSceneSignature> states,
        ObservedScene scene,
        string evidenceId) => EnsureState(states, [scene], evidenceId);

    private (LearnedStateSceneSignature State, IReadOnlyList<LearnedStateSceneSignature> States) EnsureState(
        IReadOnlyList<LearnedStateSceneSignature> states,
        IReadOnlyList<ObservedScene> scenes,
        string evidenceId)
    {
        var anchors = Anchors(scenes, evidenceId);
        var stateId = StateId(anchors);
        var existing = states.SingleOrDefault(state => state.StateId == stateId);
        if (existing is not null)
        {
            return (existing, states);
        }
        var created = new LearnedStateSceneSignature(
            stateId,
            "known-screen-index-v1",
            anchors,
            [],
            [evidenceId]);
        return (created, states.Append(created).ToArray());
    }

    private static IReadOnlyList<LearnedSceneAnchor> Anchors(
        IReadOnlyList<ObservedScene> scenes,
        string evidenceId)
    {
        var regions = scenes[0].DiscoveryEvidence?.LocalGroundingRegions
            ?? throw new InvalidOperationException("ページ索引には同一frameのlocal OCR regionが必要です。");
        var anchors = regions
            .Where(region => Normalize(region.Text).Length >= 2)
            .Where(region => !region.Text.Any(char.IsDigit))
            .Where(region => region.EvidenceRegion.NormalizedBounds[1] >= 0.03)
            .GroupBy(region => Normalize(region.Text), StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(region => region.EvidenceRegion.NormalizedBounds[3])
                .ThenByDescending(region => Area(region.EvidenceRegion.NormalizedBounds))
                .First())
            .Where(region => scenes.Skip(1).All(scene =>
                scene.DiscoveryEvidence?.LocalGroundingRegions?.Any(other =>
                    string.Equals(Normalize(other.Text), Normalize(region.Text), StringComparison.Ordinal)
                    && PositionMatches(
                        region.EvidenceRegion.NormalizedBounds,
                        other.EvidenceRegion.NormalizedBounds)) == true))
            .OrderByDescending(region => ContainsNonAsciiLetter(region.Text))
            .ThenByDescending(region => region.EvidenceRegion.NormalizedBounds[3])
            .ThenByDescending(region => Area(region.EvidenceRegion.NormalizedBounds))
            .Take(2)
            .Select(region => new LearnedSceneAnchor(
                region.Text,
                region.EvidenceRegion.NormalizedBounds.ToArray(),
                evidenceId))
            .ToArray();
        if (anchors.Length != 2)
        {
            throw new InvalidOperationException("ページ索引に必要な異なるOCR anchorを2件取得できません。");
        }
        return anchors;
    }

    private static bool PositionMatches(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        var leftX = left[0] + left[2] / 2;
        var leftY = left[1] + left[3] / 2;
        var rightX = right[0] + right[2] / 2;
        var rightY = right[1] + right[3] / 2;
        return Math.Abs(leftX - rightX) <= 0.04 && Math.Abs(leftY - rightY) <= 0.04;
    }

    private static LearnedAffordanceSignature Action(
        string stateId,
        AffordanceCandidate control,
        string evidenceId,
        string? destinationStateId)
    {
        var label = control.SemanticLabel
            ?? throw new InvalidOperationException("既知操作索引は文字labelを持つcontrolだけを保存します。");
        var primitive = control.AllowedPrimitives.Contains(GameInteractionOperations.Click, StringComparer.Ordinal)
            ? GameInteractionOperations.Click
            : control.AllowedPrimitives.First();
        var actionId = Id("known-action", $"{stateId}\n{Normalize(label)}\n{primitive}");
        return new LearnedAffordanceSignature(
            actionId,
            control.Locator.LocatorRevision,
            label,
            control.Locator.NormalizedBounds.ToArray(),
            [primitive],
            [evidenceId],
            destinationStateId);
    }

    private LearnedSceneProfileDocument Empty(string evidenceId) =>
        new(
            ContractSchemaVersions.Revision03,
            Id("known-profile", $"{gameId}\n{environmentScope}"),
            "known-screen-index-v1",
            gameId,
            environmentScope,
            processName,
            windowTitle,
            1_000,
            0.04,
            [],
            [evidenceId]);

    private static IReadOnlyList<LearnedStateSceneSignature> Replace(
        IReadOnlyList<LearnedStateSceneSignature> states,
        LearnedStateSceneSignature replacement) =>
        states.Where(state => state.StateId != replacement.StateId).Append(replacement).ToArray();

    private static string StateId(IReadOnlyList<LearnedSceneAnchor> anchors) =>
        Id("known-screen", string.Join('\n', anchors
            .OrderBy(anchor => Normalize(anchor.Text), StringComparer.Ordinal)
            .Select(anchor => $"{Normalize(anchor.Text)}|{Band(anchor.NormalizedBounds[0] + anchor.NormalizedBounds[2] / 2)}|{Band(anchor.NormalizedBounds[1] + anchor.NormalizedBounds[3] / 2)}")));

    private static string Id(string prefix, string value) =>
        $"{prefix}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()}";

    private static int Band(double value) => Math.Clamp((int)Math.Floor(value * 8), 0, 7);
    private static double Area(IReadOnlyList<double> bounds) => bounds[2] * bounds[3];
    private static bool ContainsNonAsciiLetter(string value) =>
        value.Any(character => character > 0x7F && char.IsLetter(character));
    private static string Normalize(string value) =>
        string.Concat(value.Normalize(NormalizationForm.FormKC)
            .Where(character => !char.IsWhiteSpace(character) && !char.IsPunctuation(character)))
            .ToUpperInvariant();

    private static void Validate(ObservedScene page, AffordanceCandidate control, string evidenceId)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(control);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceId);
        if (control.ObservationId != page.ObservationId
            || control.FrameSequence != page.Frame.Sequence
            || control.TransformRevision != page.Frame.TransformRevision)
        {
            throw new InvalidOperationException("controlは保存対象pageの同一Observationへ束縛されていません。");
        }
    }
}
