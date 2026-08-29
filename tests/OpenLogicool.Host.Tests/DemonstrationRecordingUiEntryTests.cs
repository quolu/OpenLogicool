using System.IO;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Host;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Host.Tests;

/// <summary>
/// t07: 記録画面は開いた瞬間に状態とsession一覧を読む。まだ何も記録していないDBでも
/// それが通らないと、UIは開いた瞬間に落ちる。実装（fakeではない）で確認する。
/// </summary>
public sealed class DemonstrationRecordingUiEntryTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"openlogicool-ui-entry-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        File.Delete(path);
        File.Delete($"{path}.{MacroTargetSettingsStore.FileName}");
    }

    [Fact]
    public void The_recording_screen_can_open_before_any_target_is_selected()
    {
        using var intents = new HostDemonstrationRecordingIntents(
            path, new WindowsDemonstrationLiveSessionFactory(path), new DemonstrationRecordingGate());

        var status = intents.Status();

        Assert.Equal(DemonstrationRecorderStatus.Idle, status.Status);
        Assert.Null(status.SessionId);
        Assert.Empty(intents.ListSessions());
    }

    [Fact]
    public void The_recording_screen_can_open_with_a_target_selected_and_nothing_recorded()
    {
        MacroTargetSettingsStore.ForDatabase(path).Save("game");
        using var intents = new HostDemonstrationRecordingIntents(
            path, new WindowsDemonstrationLiveSessionFactory(path), new DemonstrationRecordingGate());

        Assert.Equal(DemonstrationRecorderStatus.Idle, intents.Status().Status);
        Assert.Empty(intents.ListSessions());
    }
}
