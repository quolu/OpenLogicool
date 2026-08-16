using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

/// <summary>
/// APP-008: identity 不明時に Unknown Application へ明示遷移し、直前 profile を黙って継続しないことの
/// focused test。ForegroundStateClassifier は ProfileSwitchJudge/AppProfileResolver が返す MatchKind を
/// 分類するだけの pure function であり、profile 選択規則そのものは ProfileSwitchDiagnosticsTests が担保する。
/// </summary>
public sealed class ForegroundStateTests
{
    private static ProfileSwitchKindOutcome Outcome(string deviceKind, string matchKind, string selected = "p", string? previous = "p") =>
        new(deviceKind, matchKind, selected, previous, Changed: selected != previous);

    [Fact]
    public void All_kinds_identity_unavailable_classifies_as_unknown_application()
    {
        var outcomes = new[]
        {
            Outcome("G13", "identity-unavailable"),
            Outcome("G600", "identity-unavailable"),
        };

        Assert.Equal(ForegroundState.UnknownApplication, ForegroundStateClassifier.Classify(outcomes));
    }

    [Fact]
    public void Any_kind_matched_by_package_or_path_classifies_as_known_matched()
    {
        var packageMatched = new[] { Outcome("G13", "default"), Outcome("G600", "package") };
        var pathMatched = new[] { Outcome("G13", "path"), Outcome("G600", "default") };

        Assert.Equal(ForegroundState.KnownMatched, ForegroundStateClassifier.Classify(packageMatched));
        Assert.Equal(ForegroundState.KnownMatched, ForegroundStateClassifier.Classify(pathMatched));
    }

    [Fact]
    public void All_kinds_default_only_classifies_as_known_default()
    {
        var outcomes = new[] { Outcome("G13", "default"), Outcome("G600", "default") };

        Assert.Equal(ForegroundState.KnownDefault, ForegroundStateClassifier.Classify(outcomes));
    }

    [Fact]
    public void HasTransitioned_is_true_on_first_observation_and_on_actual_state_change_only()
    {
        // 初回観測（previous が無い）は「run 起動時に初期状態も1行表示」のため必ず遷移扱い
        Assert.True(ForegroundStateClassifier.HasTransitioned(null, ForegroundState.KnownDefault));

        // 同一状態の継続では遷移なし（ResidentInputHost が同一状態継続で log を出さない根拠）
        Assert.False(ForegroundStateClassifier.HasTransitioned(ForegroundState.KnownMatched, ForegroundState.KnownMatched));

        // Unknown への遷移・Unknown からの復帰の両方を検出する
        Assert.True(ForegroundStateClassifier.HasTransitioned(ForegroundState.KnownMatched, ForegroundState.UnknownApplication));
        Assert.True(ForegroundStateClassifier.HasTransitioned(ForegroundState.UnknownApplication, ForegroundState.KnownDefault));
    }

    [Fact]
    public void Empty_outcomes_classifies_as_known_default()
    {
        Assert.Equal(ForegroundState.KnownDefault, ForegroundStateClassifier.Classify(Array.Empty<ProfileSwitchKindOutcome>()));
    }

    [Fact]
    public void MatchKind_string_overload_agrees_with_outcome_overload()
    {
        var outcomes = new[] { Outcome("G13", "identity-unavailable"), Outcome("G600", "identity-unavailable") };
        var matchKinds = new[] { "identity-unavailable", "identity-unavailable" };

        Assert.Equal(ForegroundStateClassifier.Classify(outcomes), ForegroundStateClassifier.Classify(matchKinds));
    }

    // --- 「Unknown 遷移時に直前一致 profile を選ばない」の固定（APP-008 の中核） ---

    private static MappingProfileDocument Document(string profileId, string deviceKind) =>
        new(
            ContractSchemaVersions.Revision01,
            profileId,
            deviceKind,
            ProfileRevision: "rev-1",
            MappingRevision: "map-1",
            DefaultLayerId: "base",
            LayerIds: ["base"],
            LatchSelectors: [],
            HoldSelectors: [],
            Bindings: [new MappingBindingEntry("G9", "base", ["Key:F13"])]);

    private static AppProfileResolver BuildResolver() => AppProfileResolver.Build(
        [Document("p-default", "G600"), Document("p-game", "G600")],
        [
            new AppProfileAssociation(ContractSchemaVersions.Revision01, "*", "G600", "p-default", AppMatcherKind.Path),
            new AppProfileAssociation(ContractSchemaVersions.Revision01, @"c:\game\game.exe", "G600", "p-game", AppMatcherKind.Path),
        ]);

    [Fact]
    public void When_identity_becomes_unavailable_after_a_match_the_default_profile_is_selected_not_the_previous_match()
    {
        var resolver = BuildResolver();
        var matchedIdentity = new ForegroundApplicationIdentity(
            AppProfileResolver.NormalizePath(@"c:\game\game.exe"), null, 100, null);
        var previousProfileByKind = new Dictionary<string, string> { ["G600"] = "p-default" };

        // 1 tick 目: game.exe に一致し p-game が選ばれる（KnownMatched）
        var matchedDecision = ProfileSwitchJudge.Decide(1, null, previousProfileByKind, matchedIdentity, resolver);
        var matchedOutcome = Assert.Single(matchedDecision.Outcomes);
        Assert.Equal("p-game", matchedOutcome.SelectedProfileId);
        Assert.Equal(ForegroundState.KnownMatched, ForegroundStateClassifier.Classify(matchedDecision.Outcomes));

        // 2 tick 目: foreground identity が取得不能になった（process handle open 不能等）
        var afterMatchProfileByKind = new Dictionary<string, string> { ["G600"] = matchedOutcome.SelectedProfileId };
        var unavailableDecision = ProfileSwitchJudge.Decide(2, matchedIdentity, afterMatchProfileByKind, null, resolver);
        var unavailableOutcome = Assert.Single(unavailableDecision.Outcomes);

        // 直前一致の 'p-game' を黙って継続せず、既定 'p-default' へ明示的に切り替わる
        Assert.Equal("p-default", unavailableOutcome.SelectedProfileId);
        Assert.NotEqual("p-game", unavailableOutcome.SelectedProfileId);
        Assert.Equal("identity-unavailable", unavailableOutcome.MatchKind);
        Assert.True(unavailableOutcome.Changed);
        Assert.Equal(ForegroundState.UnknownApplication, ForegroundStateClassifier.Classify(unavailableDecision.Outcomes));
    }
}
