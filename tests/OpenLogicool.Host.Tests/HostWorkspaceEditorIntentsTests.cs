using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Desktop;
using OpenLogicool.Host;
using OpenLogicool.Persistence;
using Xunit;

namespace OpenLogicool.Host.Tests;

/// <summary>
/// Desktop UI 移行 第3段（Action 盤＋Binding Inspector＋保存/undo 配線）の機能中核。
/// Desktop 側（<see cref="WorkspaceDocumentEditor"/>・<see cref="WorkspaceEditorProjection"/>）は pure なので
/// Desktop.Tests 側の focused test で確認し、ここでは Host 実装（<see cref="HostWorkspaceEditorIntents"/>）が
/// 実 SQLite（in-memory）越しに compile／保存／undo／読み込みを正しく配線していることを確認する。
/// </summary>
public sealed class HostWorkspaceEditorIntentsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HostWorkspaceEditorIntents _intents;

    public HostWorkspaceEditorIntentsTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(_connection);
        _intents = new HostWorkspaceEditorIntents(_connection);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void Unassociated_application_loads_an_empty_draft_with_the_default_device_layout()
    {
        var result = _intents.LoadDocument(@"c:\game\nikke.exe");

        Assert.Null(result.RevisionNumber);
        Assert.Empty(result.Document.Actions);
        Assert.Empty(result.Document.Bindings);

        var g13 = Assert.Single(result.Document.Devices, device => device.DeviceKind == "G13");
        Assert.Equal("base", g13.DefaultLayerId);
        Assert.Equal(["base", "m2", "m3"], g13.LayerIds);
        Assert.Equal(["M1", "M2", "M3"], g13.LatchSelectors.Select(selector => selector.ControlId));

        var g600 = Assert.Single(result.Document.Devices, device => device.DeviceKind == "G600");
        Assert.Equal("base", g600.DefaultLayerId);
        Assert.Equal(["base", "shift"], g600.LayerIds);
        Assert.Equal("G6", Assert.Single(g600.HoldSelectors).ControlId);

        // 保存（revision）は未実施——WorkspaceApplyReport と同一語彙
        Assert.Contains(result.Stages, stage => stage.Stage == "保存（revision）" && stage.State == "未実施");
    }

    [Fact]
    public void Compile_rejects_colliding_bindings_and_marks_the_document_unsavable()
    {
        var draft = WorkspaceDocumentEditor.CreateDraft("ws-dodge");
        draft = WorkspaceDocumentEditor.AddAction(draft, "dodge", "回避", ["Key:Space"]);
        draft = WorkspaceDocumentEditor.AddAction(draft, "attack", "攻撃", ["Key:F"]);
        draft = WorkspaceDocumentEditor.SetBinding(draft, "dodge", "G13", "G1", "base");
        draft = WorkspaceDocumentEditor.SetBinding(draft, "attack", "G13", "G1", "base");

        var outcome = _intents.Compile(draft);

        Assert.False(outcome.IsValid);
        Assert.Contains("G1", outcome.ErrorMessage);
        Assert.Contains("'dodge'", outcome.ErrorMessage);
        Assert.Contains("'attack'", outcome.ErrorMessage);
    }

    [Fact]
    public void Adding_an_action_and_binding_it_reflects_in_the_saved_document_and_stage_strip()
    {
        var draft = WorkspaceDocumentEditor.CreateDraft("ws-dodge");
        draft = WorkspaceDocumentEditor.AddAction(draft, "dodge", "回避", ["Key:Space"]);
        draft = WorkspaceDocumentEditor.SetBinding(draft, "dodge", "G13", "G1", "base");

        var compileOutcome = _intents.Compile(draft);
        Assert.True(compileOutcome.IsValid);

        var saveOutcome = _intents.Save(draft, "*");

        Assert.Equal(1, saveOutcome.RevisionNumber);
        // APP-007: 保存成功を「適用完了」と表示しない——段階は「保存」成立と「runtime 適用」を別に持つ
        Assert.Contains(saveOutcome.Stages, stage => stage.Stage == "保存（revision）" && stage.State == "成立");
        Assert.DoesNotContain(saveOutcome.Stages, stage => stage.State.Contains("適用完了"));

        // 保存直後に associate すれば LoadDocument が保存内容を revision から読み戻す
        new SqliteAppAssociationStore(_connection).Upsert(new AppProfileAssociation(
            ContractSchemaVersions.Revision01, @"c:\game\nikke.exe", "G13", "ws-dodge-G13"));
        new SqliteAppAssociationStore(_connection).Upsert(new AppProfileAssociation(
            ContractSchemaVersions.Revision01, @"c:\game\nikke.exe", "G600", "ws-dodge-G600"));

        var reloaded = _intents.LoadDocument(@"c:\game\nikke.exe");

        // 特定アプリが共通設定と同じprofileを参照している間は、編集前にapp専用workspaceへ分岐する。
        Assert.Null(reloaded.RevisionNumber);
        Assert.Equal("ws-nikke", reloaded.Document.WorkspaceId);
        var dodge = Assert.Single(reloaded.Document.Actions);
        Assert.Equal("dodge", dodge.ActionId);
        var binding = Assert.Single(reloaded.Document.Bindings);
        Assert.Equal(("G13", "G1", "base"), (binding.DeviceKind, binding.ControlId, binding.LayerId));
    }

    [Fact]
    public void Macro_action_compiles_and_saves_through_the_existing_workspace_path()
    {
        var token = MacroInvocationTokens.Create(new MacroVersionReference(
            "route:daily", null, MacroPlaybackMode.AiMonitored));
        var draft = WorkspaceDocumentEditor.CreateDraft("ws-macro");
        draft = WorkspaceDocumentEditor.AddAction(draft, "daily", "日課", [token]);
        draft = WorkspaceDocumentEditor.SetBinding(draft, "daily", "G600", "G9", "base");

        Assert.True(_intents.Compile(draft).IsValid);
        var saved = _intents.Save(draft, "*");

        Assert.Equal(1, saved.RevisionNumber);
        var persisted = Assert.Single(new SqliteWorkspaceRevisionStore(_connection).ListRevisions("ws-macro")).Document;
        Assert.Equal(token, Assert.Single(persisted.Actions).Outputs.Single());
        Assert.Equal("G9", Assert.Single(persisted.Bindings).ControlId);
    }

    [Fact]
    public void Undo_reapplies_the_previous_revision_as_a_new_revision_and_returns_its_document()
    {
        var v1 = WorkspaceDocumentEditor.CreateDraft("ws-undo");
        v1 = WorkspaceDocumentEditor.AddAction(v1, "dodge", "回避", ["Key:Space"]);
        _intents.Save(v1, "*");

        var v2 = WorkspaceDocumentEditor.AddAction(v1, "attack", "攻撃", ["Key:F"]);
        _intents.Save(v2, "*");

        var undoOutcome = _intents.Undo("ws-undo", revisionNumber: null);

        Assert.Equal(3, undoOutcome.RevisionNumber);
        // revision 1（"dodge" だけ）を新 revision として再適用しているので、"attack" は含まれない
        Assert.Single(undoOutcome.Document.Actions);
        Assert.Equal("dodge", undoOutcome.Document.Actions[0].ActionId);
        Assert.Contains(undoOutcome.Stages, stage => stage.Stage == "保存（revision）" && stage.State == "成立");
    }

    [Fact]
    public void Save_of_second_app_workspace_keeps_resolution_by_persisting_default_association()
    {
        // 共通設定（"*"）を先に保存 → その時点では種別ごとに profile が1つで自動既定
        var defaultDraft = WorkspaceDocumentEditor.CreateDraft("default");
        defaultDraft = WorkspaceDocumentEditor.AddAction(defaultDraft, "dodge", "回避", ["Key:Space"]);
        _intents.Save(defaultDraft, "*");

        // 2つ目（app 用）の保存が拒否されず、既定の関連付けが保全される
        var appDraft = WorkspaceDocumentEditor.CreateDraft("ws-nikke");
        appDraft = WorkspaceDocumentEditor.AddAction(appDraft, "burst", "バースト", ["Key:B"]);
        appDraft = WorkspaceDocumentEditor.SetBinding(appDraft, "burst", "G600", "G9", "base");
        var outcome = _intents.Save(appDraft, @"C:\NIKKE\NIKKE\game\nikke.exe");

        Assert.Equal(1, outcome.RevisionNumber);

        // 再読込で app workspace が関連付け経由で引けること（保存が袋小路を作らない）
        var reloaded = _intents.LoadDocument(@"c:\nikke\nikke\game\nikke.exe");
        Assert.Equal("ws-nikke", reloaded.Document.WorkspaceId);
    }

    [Fact]
    public void App_that_reuses_default_profiles_is_forked_before_editing()
    {
        var commonLcd = new WorkspaceG13LcdSetting(
            WorkspaceG13LcdContentKind.Text,
            Convert.ToBase64String(new byte[960]),
            null,
            "COMMON");
        var defaultDraft = WorkspaceDocumentEditor.CreateDraft("default") with { G13Lcd = commonLcd };
        defaultDraft = WorkspaceDocumentEditor.AddAction(defaultDraft, "dodge", "回避", ["Key:Space"]);
        _intents.Save(defaultDraft, "*");

        var associations = new SqliteAppAssociationStore(_connection);
        associations.Upsert(new AppProfileAssociation(
            ContractSchemaVersions.Revision01, @"c:\nikke\nikke\game\nikke.exe", "G13", "default-G13"));
        associations.Upsert(new AppProfileAssociation(
            ContractSchemaVersions.Revision01, @"c:\nikke\nikke\game\nikke.exe", "G600", "default-G600"));

        var inherited = _intents.LoadDocument(@"c:\nikke\nikke\game\nikke.exe");

        Assert.Equal("ws-nikke", inherited.Document.WorkspaceId);
        Assert.Null(inherited.RevisionNumber);
        Assert.Equal(commonLcd, inherited.Document.G13Lcd);
        Assert.Single(inherited.Document.Actions);

        var gameLcd = commonLcd with { Text = "NIKKE" };
        _intents.Save(inherited.Document with { G13Lcd = gameLcd }, @"c:\nikke\nikke\game\nikke.exe");

        var reloadedGame = _intents.LoadDocument(@"c:\nikke\nikke\game\nikke.exe");
        var reloadedDefault = _intents.LoadDocument("*");
        Assert.Equal("ws-nikke", reloadedGame.Document.WorkspaceId);
        Assert.Equal(gameLcd, reloadedGame.Document.G13Lcd);
        Assert.Equal("default", reloadedDefault.Document.WorkspaceId);
        Assert.Equal(commonLcd, reloadedDefault.Document.G13Lcd);
    }
}
