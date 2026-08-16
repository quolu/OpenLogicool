using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Domain;
using Xunit;

namespace OpenLogicool.Input.Tests;

/// <summary>
/// Phase 2 Exit 条件2: 1,000 generation race。
/// 1,000 回の generation 変化（profile 差し替え・latch 層切替・hold 層の出入り）を
/// 押下・解放と決定的 LCG で交錯させ、wrong release 0 を検証する:
/// - up が返す outputs は down 時に返された outputs と完全一致（revision 刻印付き token で識別）
/// - 仮想 key 台帳で「down していない output の up」「二重 down」が一度も起きない
/// - 最終 StopAndReleaseAll が保持中 output と完全一致し、台帳が空になる
/// </summary>
public sealed class GenerationRaceTests
{
    private const string DeviceId = "race-device";
    private static readonly string[] Controls = ["G1", "G2", "G3", "G4", "G5", "G6", "G7", "G8"];

    /// <summary>revision 刻印付き binding。up が現在 profile を再解決すると token の revision が食い違って検出される。</summary>
    private static MappingProfile Profile(int revision) =>
        new(
            profileRevision: $"rev-{revision}",
            mappingRevision: $"map-{revision}",
            defaultLayerId: "base",
            layerIds: ["base", "alt", "hold"],
            latchSelectors: new Dictionary<string, string> { ["SEL_BASE"] = "base", ["SEL_ALT"] = "alt" },
            holdSelectors: new Dictionary<string, string> { ["HOLD"] = "hold" },
            bindings: Controls.SelectMany(control => new[]
            {
                new MappingBinding(control, "base", [$"r{revision}:base:{control}"]),
                new MappingBinding(control, "alt", [$"r{revision}:alt:{control}", $"r{revision}:alt2:{control}"]),
                new MappingBinding(control, "hold", [$"r{revision}:hold:{control}"]),
            }));

    [Fact]
    public void One_thousand_generation_changes_produce_zero_wrong_releases()
    {
        var lcg = new DeterministicLcg(seed: 0x0202_6816);
        var runtime = new DeviceMappingRuntime(DeviceId, Profile(0));

        var heldControls = new HashSet<string>(StringComparer.Ordinal);
        var downOutputsByControl = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var virtualKeyLedger = new HashSet<string>(StringComparer.Ordinal);
        var holdSelectorHeld = false;

        var generationChanges = 0;
        var revision = 0;
        long sequence = 0;
        long downsProcessed = 0;

        void ApplyEdges(IReadOnlyList<MappedOutputEdge> edges)
        {
            foreach (var edge in edges)
            {
                if (edge.Edge == PhysicalInputEdge.Down)
                {
                    Assert.True(virtualKeyLedger.Add(edge.Output), $"二重 down: {edge.Output}");
                }
                else
                {
                    Assert.True(virtualKeyLedger.Remove(edge.Output), $"down していない output の up: {edge.Output}");
                }
            }
        }

        PhysicalInput Edge(string controlId, PhysicalInputEdge edge) =>
            new(ContractSchemaVersions.Revision01, DeviceId, controlId, edge, MonotonicMs: 0, ReportSequence: ++sequence);

        while (generationChanges < 1_000)
        {
            var action = lcg.Next(100);
            if (action < 35)
            {
                // 押下（未保持の control から選ぶ）
                var candidates = Controls.Where(control => !heldControls.Contains(control)).ToArray();
                if (candidates.Length == 0)
                {
                    continue;
                }

                var control = candidates[(int)lcg.Next(candidates.Length)];
                var edges = runtime.Process(Edge(control, PhysicalInputEdge.Down));
                Assert.All(edges, edge => Assert.Equal(PhysicalInputEdge.Down, edge.Edge));
                Assert.NotEmpty(edges);
                ApplyEdges(edges);
                heldControls.Add(control);
                downOutputsByControl[control] = edges.Select(edge => edge.Output).ToArray();
                downsProcessed++;
            }
            else if (action < 70)
            {
                // 解放: down 時 outputs と完全一致すること（現在 profile を再解決しないこと）
                if (heldControls.Count == 0)
                {
                    continue;
                }

                var control = heldControls.ElementAt((int)lcg.Next(heldControls.Count));
                var edges = runtime.Process(Edge(control, PhysicalInputEdge.Up));
                Assert.Equal(downOutputsByControl[control], edges.Select(edge => edge.Output).ToArray());
                Assert.All(edges, edge => Assert.Equal(PhysicalInputEdge.Up, edge.Edge));
                ApplyEdges(edges);
                heldControls.Remove(control);
                downOutputsByControl.Remove(control);
            }
            else if (action < 85)
            {
                // generation 変化: profile 差し替え（hold stack はクリアされる仕様）
                revision++;
                runtime.ApplyProfile(Profile(revision));
                holdSelectorHeld = false;
                generationChanges++;
            }
            else if (action < 95)
            {
                // generation 変化: latch 層切替
                var selector = lcg.Next(2) == 0 ? "SEL_BASE" : "SEL_ALT";
                Assert.Empty(runtime.Process(Edge(selector, PhysicalInputEdge.Down)));
                Assert.Empty(runtime.Process(Edge(selector, PhysicalInputEdge.Up)));
                generationChanges++;
            }
            else
            {
                // generation 変化: hold 層の出入り
                if (holdSelectorHeld)
                {
                    Assert.Empty(runtime.Process(Edge("HOLD", PhysicalInputEdge.Up)));
                    holdSelectorHeld = false;
                }
                else
                {
                    Assert.Empty(runtime.Process(Edge("HOLD", PhysicalInputEdge.Down)));
                    holdSelectorHeld = true;
                }

                generationChanges++;
            }
        }

        // 終端: StopAndReleaseAll が保持中 output と完全一致し、台帳が空になる
        var releases = runtime.StopAndReleaseAll();
        Assert.All(releases, edge => Assert.Equal(PhysicalInputEdge.Up, edge.Edge));
        Assert.Equal(virtualKeyLedger, releases.Select(edge => edge.Output).ToHashSet());
        ApplyEdges(releases);
        Assert.Empty(virtualKeyLedger);

        Assert.True(downsProcessed > 300, $"down 総数が想定より少ない: {downsProcessed}");
    }
}

/// <summary>再現可能な決定的 LCG（テスト専用）。</summary>
internal sealed class DeterministicLcg(ulong seed)
{
    private ulong _state = seed;

    public uint Next(int exclusiveMax)
    {
        _state = _state * 6364136223846793005UL + 1442695040888963407UL;
        return (uint)((_state >> 33) % (uint)exclusiveMax);
    }
}
