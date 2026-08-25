using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Desktop;
using OpenLogicool.Persistence;
using OpenLogicool.Playbooks;

namespace OpenLogicool.Host;

/// <summary>
/// 保存済みLearning Route版をpinし、観測・journal・Nano dispatch・再観測を一手ずつ進めるHost境界。
/// 自動retry、別入力経路、入力API戻り値による成功扱いは持たない。
/// </summary>
public sealed class HostSupervisedMacroIntents : ISupervisedMacroIntents
{
    private readonly SqliteGameStructureStore structures;
    private readonly SqliteLearningRouteStore routes;
    private readonly ISupervisedMacroRuntimePort runtime;
    private readonly IEngineeringLogSink engineeringLog;
    private readonly TimeProvider timeProvider;
    private readonly SupervisedMacroAuthorizationSource authorizationSource;
    private readonly IReadOnlyCollection<string> prohibitedRiskTags;
    private SupervisedVisualMacroRunner? runner;
    private ObservedScene? beforeScene;

    public HostSupervisedMacroIntents(
        SqliteConnection connection,
        ISupervisedMacroRuntimePort runtime,
        IEngineeringLogSink engineeringLog,
        TimeProvider? timeProvider = null,
        SupervisedMacroAuthorizationSource authorizationSource = SupervisedMacroAuthorizationSource.InteractiveUser,
        IReadOnlyCollection<string>? prohibitedRiskTags = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        this.engineeringLog = engineeringLog ?? throw new ArgumentNullException(nameof(engineeringLog));
        structures = new SqliteGameStructureStore(connection);
        routes = new SqliteLearningRouteStore(connection);
        JournalStore = new SqliteRunJournalStore(connection);
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.authorizationSource = authorizationSource;
        this.prohibitedRiskTags = prohibitedRiskTags ?? [];
    }

    internal IRunJournalStore JournalStore { get; }

    public SupervisedMacroRunSnapshot Start(
        string gameId,
        string environmentScope,
        string routeId,
        string routeVersionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentScope);
        ArgumentException.ThrowIfNullOrWhiteSpace(routeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(routeVersionId);
        if (runner is not null
            && runner.Snapshot.State is not (SupervisedMacroRunState.Completed or SupervisedMacroRunState.Stopped))
        {
            throw new InvalidOperationException(
                "未完了の教師付きマクロがあります。結果不明を含め、先に停止して明示的に破棄してください。");
        }

        var route = routes.ReadRevisions(routeId).SingleOrDefault(item =>
            string.Equals(item.VersionId, routeVersionId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("指定した学習ルート版がありません。");
        if (!string.Equals(route.GameId, gameId, StringComparison.Ordinal)
            || !string.Equals(route.EnvironmentScope, environmentScope, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("学習ルート版は選択中のゲーム環境と一致しません。");
        }
        var structure = structures.LoadRevision(gameId, environmentScope);
        var program = VisualMacroCompiler.Compile(route, structure, prohibitedRiskTags);
        var journal = RunJournal.Restore(JournalStore, engineeringLog);
        var gate = AttemptDispatchGate.Recover(JournalStore, journal);
        var unresolved = gate.Attempts.FirstOrDefault(candidate => candidate.IsUnresolvedAfterArm);
        if (unresolved is not null)
        {
            throw new InvalidOperationException(
                $"未解決のAttempt '{unresolved.AttemptId}'（{unresolved.State}）が残っています。" +
                "明示的なreconciliation／abandon完了まで新しい教師付き実行を開始できません。");
        }
        var runId = $"macro-run:{Guid.NewGuid():N}";
        runner = new SupervisedVisualMacroRunner(
            program,
            runId,
            journal,
            gate,
            prefix => $"{prefix}:{Guid.NewGuid():N}",
            timeProvider);
        try
        {
            runtime.Pin(program);
        }
        catch (Exception exception)
        {
            runner.StopBeforeDispatchUnavailable($"実行環境を準備できません: {exception.Message}");
            return runner.Snapshot;
        }
        AuditBefore();
        return runner.Snapshot;
    }

    public SupervisedMacroRunSnapshot Next()
    {
        var active = runner ?? throw new InvalidOperationException("先に教師付きマクロを開始してください。");
        if (active.Snapshot.State != SupervisedMacroRunState.ReadyToDispatch)
        {
            throw new InvalidOperationException("操作前画面が確認済みのときだけ次の一手を送信できます。");
        }
        var step = active.CurrentStep;
        try
        {
            var refreshedBefore = runtime.ObserveBefore(step);
            active.ReauditBefore(refreshedBefore);
            beforeScene = active.Snapshot.State == SupervisedMacroRunState.ReadyToDispatch
                ? refreshedBefore
                : null;
        }
        catch (Exception exception)
        {
            active.StopBeforeDispatchUnavailable($"送信直前の画面を確認できません: {exception.Message}");
            beforeScene = null;
            return active.Snapshot;
        }
        if (active.Snapshot.State != SupervisedMacroRunState.ReadyToDispatch)
        {
            return active.Snapshot;
        }
        var capturedBefore = beforeScene
            ?? throw new InvalidOperationException("操作前の観測が保持されていません。");
        try
        {
            var authorization = authorizationSource switch
            {
                SupervisedMacroAuthorizationSource.InteractiveUser =>
                    (RunEventActorType.User, "interactive-user"),
                SupervisedMacroAuthorizationSource.OwnerDelegatedAutomation =>
                    (RunEventActorType.Automation, "owner-delegated-automation"),
                _ => throw new ArgumentOutOfRangeException(nameof(authorizationSource)),
            };
            active.DispatchOnce(
                () => runtime.DispatchNano(step, capturedBefore),
                authorization.Item1,
                authorization.Item2);
        }
        catch
        {
            beforeScene = null;
            return active.Snapshot;
        }
        try
        {
            var after = runtime.ObserveAfter(step, capturedBefore);
            active.AuditAfterTransition(after);
        }
        catch (Exception exception)
        {
            active.MarkAfterObservationUnknown(exception.Message);
            beforeScene = null;
            return active.Snapshot;
        }
        beforeScene = null;
        if (active.Snapshot.State == SupervisedMacroRunState.AwaitingBeforeAudit)
        {
            AuditBefore();
        }
        return active.Snapshot;
    }

    public SupervisedMacroRunSnapshot Stop()
    {
        var active = runner ?? throw new InvalidOperationException("開始済みの教師付きマクロがありません。");
        active.StopByUser();
        beforeScene = null;
        return active.Snapshot;
    }

    private void AuditBefore()
    {
        var active = runner!;
        try
        {
            var observed = runtime.ObserveBefore(active.CurrentStep);
            active.AuditBefore(observed);
            beforeScene = active.Snapshot.State == SupervisedMacroRunState.ReadyToDispatch ? observed : null;
        }
        catch (Exception exception)
        {
            active.StopBeforeDispatchUnavailable($"操作前の画面を確認できません: {exception.Message}");
            beforeScene = null;
        }
    }
}
