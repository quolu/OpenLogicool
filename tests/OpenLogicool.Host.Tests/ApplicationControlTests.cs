using System.Reflection;
using System.Text.Json;
using OpenLogicool.Desktop;
using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class ApplicationControlTests
{
    public interface IExample
    {
        string Echo(string text, int repeat = 1);
        Task<string> WorkAsync(string text, IProgress<string> progress, CancellationToken cancellationToken);
    }
    private sealed class Example : IExample
    {
        public string Echo(string text, int repeat = 1) => string.Concat(Enumerable.Repeat(text, repeat));
        public async Task<string> WorkAsync(string text, IProgress<string> progress, CancellationToken cancellationToken)
        { progress.Report(text); await Task.Delay(Timeout.Infinite, cancellationToken); return text; }
    }

    [Fact]
    public void 全GUIの操作interfaceと編集処理が公開一覧に含まれる()
    {
        var registry = Registry();
        ApplicationControlRegistration.RegisterIntents(registry,
            new(null!, null, null!, null!, null!, null!, null!, null!, null, "未接続", null!, null!, null!));
        var interfaces = typeof(InputStudioWindow).GetConstructors().Single().GetParameters()
            .Where(parameter => parameter.ParameterType.IsInterface).Select(parameter => parameter.ParameterType);
        foreach (var contract in interfaces)
        {
            Assert.True(registry.Covers(contract), contract.Name);
            foreach (var method in contract.GetMethods().Where(method => !method.IsSpecialName))
                Assert.Contains(registry.List(), operation => operation.Contract == contract.Name && operation.Method == method.Name);
        }
        foreach (var method in typeof(WorkspaceDocumentEditor).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            Assert.Contains(registry.List(), operation => operation.Contract == nameof(WorkspaceDocumentEditor) && operation.Method == method.Name);
        Assert.All(registry.List(), operation => Assert.False(operation.Available && operation.Contract != nameof(WorkspaceDocumentEditor)));
    }

    [Fact]
    public async Task 引数を型へ変換し既定値を使い誤った引数を拒否する()
    {
        var registry = Registry(); registry.Add<IExample>("test", new Example());
        var jobs = new ControlJobs(registry);
        var valid = jobs.Start("test.echo", JsonSerializer.SerializeToElement(new { text = "日本語", repeat = 2 }));
        var done = await Terminal(jobs, valid.Id);
        Assert.Equal("completed", done.State);
        Assert.Equal("日本語日本語", done.Result!.Value.GetString());
        var invalid = jobs.Start("test.echo", JsonSerializer.SerializeToElement(new { typo = "日本語" }));
        Assert.Equal("invalid-argument", (await Terminal(jobs, invalid.Id)).Error!.Code);
        var missing = jobs.Start("test.echo", JsonSerializer.SerializeToElement(new { }));
        Assert.Equal("invalid-argument", (await Terminal(jobs, missing.Id)).Error!.Code);
        await jobs.StopAsync();
    }

    [Fact]
    public async Task 長い処理の進行を読み取れ実行IDから中止できる()
    {
        var registry = Registry(); registry.Add<IExample>("test", new Example());
        var jobs = new ControlJobs(registry);
        var job = jobs.Start("test.work", JsonSerializer.SerializeToElement(new { text = "処理中" }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (jobs.Get(job.Id).Progress is null) await Task.Delay(10, timeout.Token);
        Assert.Equal("処理中", jobs.Get(job.Id).Progress!.Value.GetString());
        jobs.Cancel(job.Id);
        Assert.Equal("cancelled", (await Terminal(jobs, job.Id)).State);
        await jobs.StopAsync();
        Assert.Throws<InvalidOperationException>(() => jobs.Start("test.echo", JsonSerializer.SerializeToElement(new { text = "終了後" })));
    }

    [Fact]
    public async Task 名前付きパイプで操作一覧実行結果エラーを往復できる()
    {
        var registry = Registry(); registry.Add<IExample>("test", new Example());
        var jobs = new ControlJobs(registry);
        var pipe = "OpenLogicool.Test." + Guid.NewGuid().ToString("N");
        using var server = new ApplicationControlPipe(registry, jobs, pipe, databasePath: "test.db");
        var wrongDatabase = await ApplicationControlPipe.Send(new("invoke", "test.echo",
            JsonSerializer.SerializeToElement(new { text = "別のDB" }), ExpectedDatabasePath: "other.db"), pipe);
        Assert.False(wrongDatabase.Success);
        Assert.Empty(jobs.List());
        var wrongVersion = await ApplicationControlPipe.Send(new("operations", Version: 2), pipe);
        Assert.False(wrongVersion.Success);
        var operations = await ApplicationControlPipe.Send(new("operations"), pipe);
        Assert.True(operations.Success);
        Assert.Equal(2, operations.Value!.Value.GetArrayLength());
        var invoked = await ApplicationControlPipe.Send(new("invoke", "test.echo", JsonSerializer.SerializeToElement(new { text = "往復" })), pipe);
        Assert.True(invoked.Success);
        var id = invoked.Value!.Value.GetProperty("id").GetString()!;
        _ = await Terminal(jobs, id);
        var receipt = await ApplicationControlPipe.Send(new("job", JobId: id), pipe);
        Assert.Equal("往復", receipt.Value!.Value.GetProperty("result").GetString());
        var invalid = await ApplicationControlPipe.Send(new("invoke", "not-found"), pipe);
        Assert.False(invalid.Success);
        await jobs.StopAsync();
    }

    private static ControlOperationRegistry Registry() => new(action => Task.FromResult(action()));

    [Fact]
    public async Task 終了操作は完了した結果を返信するまでアプリを閉じない()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = 0;
        var registry = Registry();
        registry.Add("app.close", async (_, _) =>
        {
            await ready.Task;
            return new ControlDeferredResult("終了します", () => closed++);
        });
        var jobs = new ControlJobs(registry);
        var accepted = jobs.Start("app.close", JsonSerializer.SerializeToElement(new { }));
        Assert.NotEqual("completed", accepted.State);
        ready.SetResult();
        var completed = await Terminal(jobs, accepted.Id);
        jobs.Replied(accepted);
        Assert.Equal(0, closed);
        jobs.Replied(completed);
        Assert.Equal(1, closed);
        jobs.Replied(completed);
        Assert.Equal(1, closed);
        await jobs.StopAsync();
    }

    private static async Task<ControlJobSnapshot> Terminal(ControlJobs jobs, string id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (jobs.Get(id).State is "queued" or "running") await Task.Delay(10, timeout.Token);
        return jobs.Get(id);
    }
}
