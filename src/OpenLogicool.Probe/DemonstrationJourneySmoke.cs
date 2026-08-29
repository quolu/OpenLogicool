using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Desktop;
using OpenLogicool.Host;
using OpenLogicool.Input;
using OpenLogicool.Persistence;
using OpenLogicool.Playbooks;

namespace OpenLogicool.Probe;

/// <summary>demonstration-journey-smoke の判定1件。</summary>
internal sealed record JourneyCheck(string Name, bool Passed, string Detail);

/// <summary>
/// t07: 記録から割当・別process再openまでの一巡を、実windowと物理入力で確認するprobe。
///
/// 入力は**Nano（Serial HID）の物理HID出力**だけを使う。SendInput・Computer Useは使わない。
/// 対象は自分のprocessが作ったwindowで、他appやgameには触らない。
///
/// 判定は観測結果だけから決まる純関数（<see cref="DemonstrationJourneySmokeJudgement"/>）へ
/// 出してあるので、保存済みJSONへ再適用して同じ結論を機械で確認できる。
/// </summary>
internal static class DemonstrationJourneySmoke
{
    public static int Run(string[] arguments, string outputDirectory)
    {
        string? port = null;
        string? label = null;
        for (var i = 0; i < arguments.Length; i++)
        {
            if (arguments[i] == "--port" && i + 1 < arguments.Length) port = arguments[++i];
            else if (arguments[i] == "--label" && i + 1 < arguments.Length) label = arguments[++i];
        }
        if (string.IsNullOrWhiteSpace(port))
        {
            return Unverified(outputDirectory, label, "--port <COMn> でNanoのportを指定すること。", null);
        }

        Directory.CreateDirectory(outputDirectory);
        var databasePath = Path.Combine(outputDirectory, $"demonstration-journey-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.db");
        var processName = Process.GetCurrentProcess().ProcessName;

        DemonstrationRecorderSmoke.SelfWindow window;
        try
        {
            // clickで別のページへ変わる窓にする。実測（2026-08-30）で分かったこと:
            // 無地の窓では遷移が起きず、文字1語だけの入れ替えは「OCR表記ゆれ」と判定されて
            // 遷移に数えられない。要素の顔ぶれごと変わる画面にする。
            window = DemonstrationRecorderSmoke.SelfWindow.Create(
                "OpenLogicool 操作デモ一巡 self-window", 120, 120, 720, 520,
                "START",
                "MENU" + Environment.NewLine + "ITEM" + Environment.NewLine + "BACK");
        }
        catch (InvalidOperationException error)
        {
            // 保護appが前面を握っていると、self-windowを前面化できず物理入力も観測できない。
            // 別経路へ逃げず「未確認」として止める。
            return Unverified(outputDirectory, label, "self-windowを前面化できなかった。", error.Message);
        }

        using (window)
        {
            var clientBounds = window.ClientBoundsOnScreen();

            // 製品側は process 名から MainWindowHandle を引く。console から起動した probe では
            // それが self-window とは限らないので、違う window を撮って一巡が成立したように
            // 見えることを防ぐため、先に突合して違えば未確認で止める。
            try
            {
                var located = WindowsGameTargetLocator.Locate(processName);
                if (located.Window != window.Handle)
                {
                    return Unverified(
                        outputDirectory, label,
                        "製品側が解決したwindowがself-windowと一致しない。",
                        $"located=0x{located.Window:X} self=0x{window.Handle:X} title={located.WindowTitle}");
                }
            }
            catch (InvalidOperationException error)
            {
                return Unverified(outputDirectory, label, "製品側が対象windowを解決できなかった。", error.Message);
            }

            MacroTargetSettingsStore.ForDatabase(databasePath).Save(processName);

            var gate = new DemonstrationRecordingGate();
            using var recording = new HostDemonstrationRecordingIntents(
                databasePath,
                new WindowsDemonstrationLiveSessionFactory(databasePath),
                gate,
                DemonstrationWaitConditions.WithVisionDiscovery);

            DemonstrationSessionSummary started;
            try
            {
                started = recording.StartAsync("self-windowを一度クリックする").GetAwaiter().GetResult();
            }
            catch (Exception error)
            {
                return Unverified(outputDirectory, label, "記録を開始できなかった。", error.ToString());
            }

            NanoClickResult click;
            try
            {
                click = NanoClick(port!, clientBounds);
            }
            catch (Exception error)
            {
                _ = recording.StopAsync().GetAwaiter().GetResult();
                return Unverified(outputDirectory, label, "Nanoで物理clickを送れなかった。", error.ToString());
            }

            var stopped = recording.StopAsync().GetAwaiter().GetResult();
            MacroCatalogItem? macro = null;
            if (stopped.OperationCount > 0)
            {
                try
                {
                    macro = recording.CreateMacroFromSession(started.SessionId);
                }
                catch (InvalidOperationException error)
                {
                    // 遷移しなかった操作しか無いとrouteは作れない。別経路へ逃げず未確認で止める。
                    return Unverified(outputDirectory, label, "記録した操作からrouteを導出できなかった。", error.Message);
                }
            }

            string? token = null;
            if (macro is not null)
            {
                using var connection = Open(databasePath);
                var editor = new HostWorkspaceEditorIntents(connection);
                token = MacroAssignment.CreateToken(macro, MacroPlaybackMode.AiFree);
                var document = WorkspaceDocumentEditor.CreateDraft(WorkspaceId);
                document = WorkspaceDocumentEditor.AddAction(document, "demo", "デモから作った操作", [token]);
                document = WorkspaceDocumentEditor.SetBinding(document, "demo", "G13", "G1", "base");
                document = WorkspaceDocumentEditor.SetBinding(document, "demo", "G600", "G9", "base");
                var compiled = editor.Compile(document);
                if (!compiled.IsValid)
                {
                    return Unverified(outputDirectory, label, "workspaceをcompileできなかった。", compiled.ErrorMessage);
                }
                _ = editor.Save(document, "*");
            }

            var reopen = VerifyInSeparateProcess(databasePath);
            var observation = new JourneyObservation(
                processName,
                clientBounds.Left, clientBounds.Top, clientBounds.Width, clientBounds.Height,
                click.CursorMatched,
                stopped.OperationCount,
                stopped.State.ToString(),
                macro?.RouteId,
                macro?.Goal,
                macro?.StepCount ?? 0,
                token,
                reopen);

            var checks = DemonstrationJourneySmokeJudgement.Evaluate(observation);
            var passed = checks.All(check => check.Passed);
            var path = WriteReport(outputDirectory, label, databasePath, observation, checks, passed, verdict: null);
            foreach (var check in checks)
            {
                Console.WriteLine($"{(check.Passed ? "OK  " : "NG  ")}{check.Name}: {check.Detail}");
            }
            Console.WriteLine($"report: {path}");
            return passed ? 0 : 1;
        }
    }

    /// <summary>子processとして走り、保存済みDBを再openして読めたものをJSONで返す。</summary>
    public static int RunVerify(string[] arguments)
    {
        string? databasePath = null;
        for (var i = 0; i < arguments.Length; i++)
        {
            if (arguments[i] == "--db" && i + 1 < arguments.Length) databasePath = arguments[++i];
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        using var connection = Open(databasePath);
        var workspace = new SqliteWorkspaceRevisionStore(connection).ListRevisions(WorkspaceId).LastOrDefault();
        var savedToken = workspace?.Document.Actions.SingleOrDefault()?.Outputs.SingleOrDefault();
        var controls = workspace is null
            ? []
            : workspace.Document.Bindings.Select(binding => binding.ControlId).Order().ToArray();
        var profiles = new SqliteMappingProfileStore(connection).ListAll()
            .Select(document => document.ProfileId)
            .Order()
            .ToArray();
        string? routeId = null;
        var routeEdges = 0;
        if (savedToken is not null)
        {
            var route = new HostMacroCatalog(connection).Resolve(MacroInvocationTokens.Parse(savedToken));
            routeId = route.RouteId;
            routeEdges = route.EdgeIds.Count;
        }

        Console.WriteLine(JsonSerializer.Serialize(new ReopenObservation(
            savedToken, controls, profiles, routeId, routeEdges)));
        return 0;
    }

    private const string WorkspaceId = "ws-demonstration-journey";

    private static ReopenObservation? VerifyInSeparateProcess(string databasePath)
    {
        var startInfo = new ProcessStartInfo(Environment.ProcessPath!)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("demonstration-journey-verify");
        startInfo.ArgumentList.Add("--db");
        startInfo.ArgumentList.Add(databasePath);
        using var child = Process.Start(startInfo)
            ?? throw new InvalidOperationException("再open用の別processを起動できませんでした。");
        var output = child.StandardOutput.ReadToEnd();
        child.WaitForExit(30_000);
        return child.ExitCode == 0
            ? JsonSerializer.Deserialize<ReopenObservation>(output)
            : null;
    }

    private static NanoClickResult NanoClick(string port, DemonstrationRecorderSmoke.SelfWindow.ScreenBounds bounds)
    {
        var exchange = new ProbeSerialPortFrameExchange(port);
        using var session = new SerialHidResidentOutputSession(
            exchange,
            new SerialHidSemanticVersion(1, 1, 0),
            TimeSpan.FromMilliseconds(300),
            TimeSpan.FromMilliseconds(50),
            SerialHidProtocolV1.AllCapabilities);
        session.Start();
        session.Protocol.SendAllUp();
        var emitter = session.Emitter as SerialHidEmitter
            ?? throw new InvalidOperationException("Nano sessionがSerialHidEmitterを返しませんでした。");

        var pointer = new SerialHidRelativePointer(session.Protocol, new WindowsSerialHidCursorOracle());
        var centre = new SerialHidCursorPoint(
            bounds.Left + (bounds.Width / 2), bounds.Top + (bounds.Height / 2));
        _ = pointer.MoveTo(centre);
        var observed = new WindowsSerialHidCursorOracle().ReadCurrent();
        var matched = LiveDiscoveryNanoCoordinateSmoke.IsCursorAtTarget(centre, observed);
        Thread.Sleep(200);

        // 物理HIDのdown/up。SendInputは使わない。
        emitter.Emit([new MappedOutputEdge("Mouse:Left", PhysicalInputEdge.Down)]);
        emitter.Emit([new MappedOutputEdge("Mouse:Left", PhysicalInputEdge.Up)]);
        Thread.Sleep(1_200);
        return new NanoClickResult(matched, centre.X, centre.Y);
    }

    private static SqliteConnection Open(string databasePath)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
        }.ToString());
        connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
        return connection;
    }

    private static int Unverified(string outputDirectory, string? label, string verdict, string? reason)
    {
        Directory.CreateDirectory(outputDirectory);
        var path = WriteReport(outputDirectory, label, null, null, [], false, $"未確認: {verdict}");
        Console.Error.WriteLine($"未確認: {verdict} {reason}");
        Console.Error.WriteLine($"report: {path}");
        return 2;
    }

    private static string WriteReport(
        string outputDirectory,
        string? label,
        string? databasePath,
        JourneyObservation? observation,
        IReadOnlyList<JourneyCheck> checks,
        bool passed,
        string? verdict)
    {
        var path = Path.Combine(
            outputDirectory,
            $"demonstration-journey-smoke-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(
            new
            {
                probe = "demonstration-journey-smoke",
                label = label ?? "self-window",
                capturedAtUtc = DateTimeOffset.UtcNow,
                note = "入力はNanoの物理HID出力だけ。SendInputとComputer Useは使わない。",
                databasePath,
                verdict,
                observation,
                checks,
                passed,
            },
            new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    private sealed record NanoClickResult(bool CursorMatched, int X, int Y);
}

/// <summary>再open時に別processが読めたもの。</summary>
internal sealed record ReopenObservation(
    string? Token,
    IReadOnlyList<string> BoundControlIds,
    IReadOnlyList<string> ProfileIds,
    string? RouteId,
    int RouteEdgeCount);

/// <summary>一巡で観測したもの。判定はこれだけから決まる。</summary>
internal sealed record JourneyObservation(
    string TargetProcessName,
    int ClientLeft,
    int ClientTop,
    int ClientWidth,
    int ClientHeight,
    bool NanoCursorMatched,
    int RecordedOperationCount,
    string SessionState,
    string? RouteId,
    string? Goal,
    int StepCount,
    string? AssignedToken,
    ReopenObservation? Reopen);

/// <summary>
/// demonstration-journey-smoke の判定。観測だけから決まる純関数なので、
/// 保存済みJSONへ再適用して同じ結論を機械で確認できる。
/// </summary>
internal static class DemonstrationJourneySmokeJudgement
{
    public static IReadOnlyList<JourneyCheck> Evaluate(JourneyObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var checks = new List<JourneyCheck>
        {
            Check("Nanoのカーソルが対象点へ届いた", observation.NanoCursorMatched, $"({observation.ClientLeft}, {observation.ClientTop}) 基準"),
            Check("物理clickが1操作として記録された", observation.RecordedOperationCount == 1, $"count={observation.RecordedOperationCount}"),
            Check("記録は停止済みで閉じている", observation.SessionState == "Stopped", observation.SessionState),
            Check("原本からrouteが導出された", observation.RouteId is not null && observation.StepCount >= 1, $"route={observation.RouteId} step={observation.StepCount}"),
            Check("割当tokenが作られた", observation.AssignedToken is not null, observation.AssignedToken ?? "(なし)"),
        };

        var reopen = observation.Reopen;
        checks.Add(Check("別processで再openできた", reopen is not null, reopen is null ? "子processが失敗" : "OK"));
        if (reopen is not null)
        {
            checks.Add(Check(
                "再open後も同じtokenが保存されている",
                reopen.Token is not null && reopen.Token == observation.AssignedToken,
                reopen.Token ?? "(なし)"));
            checks.Add(Check(
                "G13とG600の2 bindingが残っている",
                reopen.BoundControlIds.SequenceEqual(["G1", "G9"], StringComparer.Ordinal),
                string.Join(",", reopen.BoundControlIds.DefaultIfEmpty("(なし)"))));
            checks.Add(Check(
                "device種別ごとのprofileが2件できている",
                reopen.ProfileIds.Count == 2,
                string.Join(",", reopen.ProfileIds.DefaultIfEmpty("(なし)"))));
            checks.Add(Check(
                "tokenがデモ由来routeへ解決する",
                reopen.RouteId is not null && reopen.RouteId == observation.RouteId && reopen.RouteEdgeCount >= 1,
                $"route={reopen.RouteId} edges={reopen.RouteEdgeCount}"));
        }

        return checks;
    }

    private static JourneyCheck Check(string name, bool passed, string detail) => new(name, passed, detail);
}
