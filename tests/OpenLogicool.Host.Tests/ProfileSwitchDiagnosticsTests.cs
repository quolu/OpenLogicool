using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

/// <summary>
/// APP-005: 診断可能化された profile 切替判断（ProfileSwitchJudge・ResolveWithReason・
/// ProfileSwitchDecisionRing）の focused test。fake identity 列で規則そのものは変えず、
/// 判断が正しく構造化されることだけを検証する。
/// </summary>
public sealed class ProfileSwitchDiagnosticsTests
{
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

    private static AppProfileAssociation Association(
        string path, string deviceKind, string profileId, string matcherKind = AppMatcherKind.Path) =>
        new(ContractSchemaVersions.Revision01, path, deviceKind, profileId, matcherKind);

    private static ForegroundApplicationIdentity Identity(
        string? path = null, string? package = null, int processId = 0, DateTime? startTimeUtc = null) =>
        new(
            path is null ? null : AppProfileResolver.NormalizePath(path),
            package,
            processId,
            startTimeUtc);

    private static AppProfileResolver BuildResolver() => AppProfileResolver.Build(
        [Document("p-main", "G600"), Document("p-game", "G600"), Document("p-pkg", "G600")],
        [
            Association("*", "G600", "p-main"),
            Association(@"c:\game\game.exe", "G600", "p-game"),
            Association("chrome_8wekyb3d8bbwe", "G600", "p-pkg", AppMatcherKind.Package),
        ]);

    [Fact]
    public void Package_match_is_reported_with_reason_package()
    {
        var resolver = BuildResolver();
        var (document, matchKind) = resolver.ResolveWithReason("G600", Identity(package: "chrome_8wekyb3d8bbwe"));

        Assert.Equal("p-pkg", document!.ProfileId);
        Assert.Equal("package", matchKind);
    }

    [Fact]
    public void Path_match_is_reported_with_reason_path()
    {
        var resolver = BuildResolver();
        var (document, matchKind) = resolver.ResolveWithReason("G600", Identity(path: @"c:\game\game.exe"));

        Assert.Equal("p-game", document!.ProfileId);
        Assert.Equal("path", matchKind);
    }

    [Fact]
    public void Unmatched_identity_falls_back_to_default_with_reason_default()
    {
        var resolver = BuildResolver();
        var (document, matchKind) = resolver.ResolveWithReason("G600", Identity(path: @"c:\other\other.exe"));

        Assert.Equal("p-main", document!.ProfileId);
        Assert.Equal("default", matchKind);
    }

    [Fact]
    public void Unavailable_identity_falls_back_to_default_with_reason_identity_unavailable()
    {
        var resolver = BuildResolver();

        // identity 自体が null（foreground window 取得不能）
        var (nullDocument, nullMatchKind) = resolver.ResolveWithReason("G600", null);
        Assert.Equal("p-main", nullDocument!.ProfileId);
        Assert.Equal("identity-unavailable", nullMatchKind);

        // identity は非 null だが path・package とも取得できなかった場合（process handle open 不能）
        var (partialDocument, partialMatchKind) = resolver.ResolveWithReason("G600", Identity(processId: 4242));
        Assert.Equal("p-main", partialDocument!.ProfileId);
        Assert.Equal("identity-unavailable", partialMatchKind);
    }

    [Fact]
    public void Decide_reports_generation_change_without_profile_change_for_same_path_different_pid()
    {
        var resolver = BuildResolver();
        var previousIdentity = Identity(path: @"c:\game\game.exe", processId: 100);
        var currentIdentity = Identity(path: @"c:\game\game.exe", processId: 200);
        var previousProfileByKind = new Dictionary<string, string> { ["G600"] = "p-game" };

        var decision = ProfileSwitchJudge.Decide(1, previousIdentity, previousProfileByKind, currentIdentity, resolver);

        Assert.True(decision.ProcessGenerationChanged);
        Assert.False(decision.Changed);
        var outcome = Assert.Single(decision.Outcomes);
        Assert.Equal("p-game", outcome.SelectedProfileId);
        Assert.Equal("p-game", outcome.PreviousProfileId);
        Assert.False(outcome.Changed);
    }

    [Fact]
    public void Decide_reports_profile_change_when_target_differs_from_previous()
    {
        var resolver = BuildResolver();
        var previousProfileByKind = new Dictionary<string, string> { ["G600"] = "p-main" };

        var decision = ProfileSwitchJudge.Decide(
            1, null, previousProfileByKind, Identity(path: @"c:\game\game.exe"), resolver);

        Assert.True(decision.Changed);
        Assert.False(decision.ProcessGenerationChanged);
        var outcome = Assert.Single(decision.Outcomes);
        Assert.Equal("p-game", outcome.SelectedProfileId);
        Assert.Equal("p-main", outcome.PreviousProfileId);
        Assert.True(outcome.Changed);
        Assert.Equal("path", outcome.MatchKind);
    }

    [Fact]
    public void Ring_suppresses_consecutive_identical_state_and_keeps_only_one_entry()
    {
        var ring = new ProfileSwitchDecisionRing();
        var outcome = new ProfileSwitchKindOutcome("G600", "path", "p-game", "p-game", Changed: false);
        var decision = new ProfileSwitchDecision(
            1, @"c:\game\game.exe", null, 100, null, [outcome], ProcessGenerationChanged: false);

        var firstRecorded = ring.Record(decision);
        var secondRecorded = ring.Record(decision with { Sequence = 2 });

        Assert.True(firstRecorded);
        Assert.False(secondRecorded);
        Assert.Single(ring.Snapshot());
        Assert.Equal(1, ring.Snapshot()[0].Sequence);
    }

    [Fact]
    public void Ring_records_when_match_kind_changes_even_without_profile_change()
    {
        var ring = new ProfileSwitchDecisionRing();
        var firstOutcome = new ProfileSwitchKindOutcome("G600", "default", "p-main", "p-main", Changed: false);
        var firstDecision = new ProfileSwitchDecision(
            1, null, null, null, null, [firstOutcome], ProcessGenerationChanged: false);
        var secondOutcome = new ProfileSwitchKindOutcome("G600", "identity-unavailable", "p-main", "p-main", Changed: false);
        var secondDecision = new ProfileSwitchDecision(
            2, null, null, null, null, [secondOutcome], ProcessGenerationChanged: false);

        Assert.True(ring.Record(firstDecision));
        Assert.True(ring.Record(secondDecision));
        Assert.Equal(2, ring.Snapshot().Count);
    }

    [Fact]
    public void Ring_drops_oldest_entry_beyond_capacity()
    {
        var ring = new ProfileSwitchDecisionRing(capacity: 2);
        for (var i = 1; i <= 3; i++)
        {
            var outcome = new ProfileSwitchKindOutcome("G600", "path", $"p-{i}", $"p-{i - 1}", Changed: true);
            ring.Record(new ProfileSwitchDecision(i, null, null, null, null, [outcome], ProcessGenerationChanged: false));
        }

        var snapshot = ring.Snapshot();
        Assert.Equal(2, snapshot.Count);
        Assert.Equal(2, snapshot[0].Sequence);
        Assert.Equal(3, snapshot[1].Sequence);
    }
}
