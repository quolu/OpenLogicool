using Microsoft.Data.Sqlite;
using System.IO;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Persistence;

namespace OpenLogicool.Host;

public sealed record ProductMacroExecutionRequest(
    string TargetProcessName,
    string Goal,
    MacroPlaybackMode PlaybackMode,
    LearningRouteRevision? InitialRoute);

public interface IProductMacroExecutionEngine
{
    Task<MacroRunSnapshot> ExecuteAsync(
        ProductMacroExecutionRequest request,
        IProgress<MacroRunSnapshot> progress,
        CancellationToken cancellationToken);
}

public interface IMacroInvocationRunner
{
    Task<MacroRunSnapshot> RunQueuedAsync(
        MacroVersionReference invocation,
        IProgress<MacroRunSnapshot> progress,
        CancellationToken cancellationToken);
}

/// <summary>UI手動実行と物理button起動を同じproduct execution engineへ直列化するHost境界。</summary>
public sealed class HostMacroAutomationIntents : IMacroAutomationIntents, IMacroInvocationRunner, IDisposable
{
    private readonly string databasePath;
    private readonly IProductMacroExecutionEngine engine;
    private readonly Func<IReadOnlyList<MacroTargetOption>> targets;
    private readonly SemaphoreSlim executionGate = new(1, 1);
    private readonly object stateGate = new();
    private CancellationTokenSource? activeCancellation;
    private MacroRunSnapshot current = Idle("待機中です。");
    private bool disposed;

    public event Action<MacroRunSnapshot>? StateChanged;

    public HostMacroAutomationIntents(
        string databasePath,
        IProductMacroExecutionEngine engine,
        Func<IReadOnlyList<MacroTargetOption>>? targets = null)
    {
        this.databasePath = Path.GetFullPath(databasePath);
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        this.targets = targets ?? ListVisibleTargets;
    }

    public IReadOnlyList<MacroTargetOption> ListTargets() => targets();

    public IReadOnlyList<MacroCatalogItem> ListMacros()
    {
        using var connection = Open();
        return new HostMacroCatalog(connection).ListMacros();
    }

    public MacroCatalogItem Compose(MacroCompositionRequest request)
    {
        using var connection = Open();
        return new HostMacroCatalog(connection).Compose(request);
    }

    public Task<MacroRunSnapshot> CreateAsync(
        MacroCreateRequest request,
        IProgress<MacroRunSnapshot> progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsync(new ProductMacroExecutionRequest(
            request.TargetProcessName, request.Goal, MacroPlaybackMode.AiMonitored, null), progress, cancellationToken);
    }

    public async Task<MacroRunSnapshot> PlayAsync(
        MacroPlaybackRequest request,
        IProgress<MacroRunSnapshot> progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var route = Resolve(request.Macro);
        if (!string.Equals(route.GameId, request.TargetProcessName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("選択したアプリとマクロの対象gameが一致しません。");
        }
        return await ExecuteAsync(new ProductMacroExecutionRequest(
            request.TargetProcessName, route.Goal, request.Macro.PlaybackMode, route),
            progress, cancellationToken).ConfigureAwait(false);
    }

    public Task<MacroRunSnapshot> RunQueuedAsync(
        MacroVersionReference invocation,
        IProgress<MacroRunSnapshot> progress,
        CancellationToken cancellationToken)
    {
        var route = Resolve(invocation);
        return ExecuteAsync(new ProductMacroExecutionRequest(
            route.GameId, route.Goal, invocation.PlaybackMode, route), progress, cancellationToken);
    }

    public MacroRunSnapshot Stop()
    {
        MacroRunSnapshot stopped;
        lock (stateGate)
        {
            activeCancellation?.Cancel();
            stopped = current with
            {
                Phase = MacroRunPhase.Stopped,
                Detail = "停止を要求しました。",
                CanStart = true,
                CanStop = false,
            };
            current = stopped;
        }
        StateChanged?.Invoke(stopped);
        return stopped;
    }

    private async Task<MacroRunSnapshot> ExecuteAsync(
        ProductMacroExecutionRequest request,
        IProgress<MacroRunSnapshot> progress,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (stateGate)
        {
            activeCancellation = linked;
            current = new MacroRunSnapshot(
                MacroRunPhase.Starting, request.Goal, request.TargetProcessName, 0,
                request.InitialRoute is null ? "AI探索" : "保存済み", "", "", 0,
                request.InitialRoute?.RevisionNumber ?? 0, "開始しています。", false, true);
        }
        StateChanged?.Invoke(current);
        progress.Report(current);
        var tracked = new InlineProgress(snapshot =>
        {
            lock (stateGate) current = snapshot;
            StateChanged?.Invoke(snapshot);
            progress.Report(snapshot);
        });
        try
        {
            var terminal = await engine.ExecuteAsync(request, tracked, linked.Token).ConfigureAwait(false);
            lock (stateGate) current = terminal;
            StateChanged?.Invoke(terminal);
            return terminal;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            lock (stateGate)
            {
                current = current with { Phase = MacroRunPhase.Stopped, Detail = "停止しました。", CanStart = true, CanStop = false };
            }
            StateChanged?.Invoke(current);
            return current;
        }
        catch (Exception error)
        {
            lock (stateGate)
            {
                current = current with { Phase = MacroRunPhase.Faulted, Detail = error.Message, CanStart = true, CanStop = false };
            }
            StateChanged?.Invoke(current);
            throw;
        }
        finally
        {
            lock (stateGate) activeCancellation = null;
            executionGate.Release();
        }
    }

    private LearningRouteRevision Resolve(MacroVersionReference reference)
    {
        using var connection = Open();
        return new HostMacroCatalog(connection).Resolve(reference);
    }

    private SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
        }.ToString());
        connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
        return connection;
    }

    private static IReadOnlyList<MacroTargetOption> ListVisibleTargets() =>
        RunningApplicationCatalog.ListVisibleApplications()
            .Select(application => new MacroTargetOption(
                Path.GetFileNameWithoutExtension(application.FullPath), application.WindowTitle))
            .OrderBy(target => target.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static MacroRunSnapshot Idle(string detail) => new(
        MacroRunPhase.Idle, "", "", 0, "", "", "", 0, 0, detail, true, false);

    private sealed class InlineProgress(Action<MacroRunSnapshot> report) : IProgress<MacroRunSnapshot>
    {
        public void Report(MacroRunSnapshot value) => report(value);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Stop();
        if (!executionGate.Wait(TimeSpan.FromSeconds(15)))
        {
            throw new InvalidOperationException("macro実行が15秒以内に停止しませんでした。");
        }
        executionGate.Release();
    }
}
