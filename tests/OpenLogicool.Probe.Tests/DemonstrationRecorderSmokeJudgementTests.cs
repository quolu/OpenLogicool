using System.Text.Json;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Host;
using OpenLogicool.Probe;
using Xunit;

namespace OpenLogicool.Probe.Tests;

/// <summary>
/// t02: demonstration-recorder-smoke の判定を、live実行で取得済みの観測へ再適用する。
///
/// 判定は観測列とclient frameだけから決まる純関数なので、判定を直した後に実OS入力を
/// 起こし直さなくても、保存済みの実測（probe-output のJSON）に対して成立するかどうかを
/// ここで機械が確認できる。実行時にhookもSendInputも前面windowも使わない。
/// </summary>
public sealed class DemonstrationRecorderSmokeJudgementTests
{
    /// <summary>hook呼び出し keyboard=8 / mouse=8 が取れた採用実測。</summary>
    private const string AdoptedRun = "demonstration-recorder-smoke-20260829-112607-825.json";

    [Fact]
    public void The_corrected_judgement_passes_on_the_live_observation_that_was_already_captured()
    {
        var (clientBounds, observed) = LoadRecordedRun(AdoptedRun);

        var checks = DemonstrationRecorderSmokeJudgement.Evaluate(clientBounds, observed);

        Assert.All(checks, check => Assert.True(check.Passed, $"{check.Name}: {check.Detail}"));
    }

    [Fact]
    public void The_recorded_run_carries_the_edges_the_probe_sent_even_though_other_apps_input_is_mixed_in()
    {
        var (_, observed) = LoadRecordedRun(AdoptedRun);

        // low-level hookはdesktop全体を拾うので、送出していないkeyも観測列に居る。
        // 「先頭が自分の送出」と決めつけていたのが元の判定バグである。
        var keyDownTokens = observed
            .Where(edge => edge.Kind == DemonstrationInputEdgeKind.KeyDown)
            .Select(edge => edge.OutputToken)
            .ToArray();
        Assert.Contains("Key:Esc", keyDownTokens);
        Assert.NotEqual("Key:Esc", keyDownTokens[0]);

        var pointerDowns = observed
            .Where(edge => edge.Kind == DemonstrationInputEdgeKind.PointerDown)
            .ToArray();
        var pointerUps = observed
            .Where(edge => edge.Kind == DemonstrationInputEdgeKind.PointerUp)
            .ToArray();

        // click（押下点＝解放点）と drag（押下点≠解放点）が両方入っている実測である。
        Assert.Equal(2, pointerDowns.Length);
        Assert.Equal(2, pointerUps.Length);
        Assert.Equal(pointerDowns[0].ScreenPoint, pointerUps[0].ScreenPoint);
        Assert.NotEqual(pointerDowns[1].ScreenPoint, pointerUps[1].ScreenPoint);
    }

    [Fact]
    public void Key_edges_in_the_recorded_run_carry_no_desktop_coordinates()
    {
        var (_, observed) = LoadRecordedRun(AdoptedRun);

        Assert.All(
            observed.Where(edge =>
                edge.Kind == DemonstrationInputEdgeKind.KeyDown
                || edge.Kind == DemonstrationInputEdgeKind.KeyUp),
            edge => Assert.Null(edge.ScreenPoint));
    }

    [Fact]
    public void A_run_whose_pointer_edges_fall_outside_the_client_frame_fails_the_judgement()
    {
        var (clientBounds, observed) = LoadRecordedRun(AdoptedRun);

        // client frameを実測から遠ざけると、同じ観測でも正規化が成立しなくなる。
        // 判定が観測を無条件に通していないことの確認である。
        var shifted = new GameCaptureScreenBounds(
            clientBounds.Left + 10_000, clientBounds.Top + 10_000, clientBounds.Width, clientBounds.Height);

        var checks = DemonstrationRecorderSmokeJudgement.Evaluate(shifted, observed);

        Assert.Contains(
            checks,
            check => check.Name == "観測した pointer down はすべて client frame 内で正規化できる" && !check.Passed);
    }

    private static (GameCaptureScreenBounds Bounds, IReadOnlyList<DemonstrationInputEdge> Edges) LoadRecordedRun(
        string fileName)
    {
        var path = Path.Combine(RepositoryRoot(), "probe-output", fileName);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        var boundsElement = root.GetProperty("clientBoundsOnScreen");
        var bounds = new GameCaptureScreenBounds(
            boundsElement.GetProperty("Left").GetInt32(),
            boundsElement.GetProperty("Top").GetInt32(),
            boundsElement.GetProperty("Width").GetInt32(),
            boundsElement.GetProperty("Height").GetInt32());

        var edges = new List<DemonstrationInputEdge>();
        foreach (var element in root.GetProperty("observedEdges").EnumerateArray())
        {
            var screenPointElement = element.GetProperty("screenPoint");
            var screenPoint = screenPointElement.ValueKind == JsonValueKind.Null
                ? null
                : new DemonstrationScreenPoint(
                    screenPointElement.GetProperty("X").GetInt32(),
                    screenPointElement.GetProperty("Y").GetInt32());

            edges.Add(new DemonstrationInputEdge(
                "demonstration-input-edge.v1",
                Enum.Parse<DemonstrationInputSource>(element.GetProperty("source").GetString()!),
                Enum.Parse<DemonstrationInputEdgeKind>(element.GetProperty("kind").GetString()!),
                ControlId: element.GetProperty("token").GetString()!,
                OutputToken: element.GetProperty("token").GetString()!,
                MonotonicMs: edges.Count,
                OccurredUtc: root.GetProperty("capturedAtUtc").GetDateTimeOffset(),
                ScreenPoint: screenPoint,
                WheelVerticalSteps: element.GetProperty("WheelVerticalSteps").GetInt32(),
                WheelHorizontalSteps: element.GetProperty("WheelHorizontalSteps").GetInt32()));
        }

        return (bounds, edges);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenLogicool.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("OpenLogicool.sln を含む repository root を特定できません。");
    }
}
