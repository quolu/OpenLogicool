using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Domain;

namespace OpenLogicool.Input;

/// <summary>物理 edge から解決された output token の down/up。Input Emitter への指示単位。</summary>
public sealed record MappedOutputEdge(
    string Output,
    PhysicalInputEdge Edge);

/// <summary>
/// 一つの device instance の PhysicalInput（button edge）を output token の down/up へ変換する
/// Mapping Runtime の中核。fast path 上の pure 状態機であり、I/O を持たない。
/// ホイール tick 等の非 edge 入力は扱わない（binding 対象は button edge のみ）。
///
/// 契約（計画 §6.5・MAP-003/005/006・DEV-007/008）:
/// - down 時に profile revision・layer・mapping revision・output 集合を PressOwnership へ固定する
/// - up は down 時の output 集合だけを解放し、現在 mapping を再解決しない
/// - profile／layer 変更は新規 down から有効（generation で識別）
/// - Stop は新規 down を止めてから全所有 output を解放する
/// </summary>
public sealed class DeviceMappingRuntime
{
    private readonly string _deviceInstanceId;
    private readonly HashSet<string> _ownedControls = new(StringComparer.Ordinal);
    private readonly List<string> _holdStack = [];
    private MappingProfile _profile;
    private PressOwnershipState _state;
    private string _latchedLayerId;

    public DeviceMappingRuntime(string deviceInstanceId, MappingProfile profile)
    {
        ValidateOutputGrammar(profile);
        _deviceInstanceId = deviceInstanceId;
        _profile = profile;
        _latchedLayerId = profile.DefaultLayerId;
        _state = PressOwnershipState.Create(profile.ProfileRevision, profile.DefaultLayerId, profile.MappingRevision);
    }

    /// <summary>新規 down に適用される layer（hold selector 押下中は hold layer が優先）。</summary>
    public string CurrentLayerId =>
        _holdStack.Count > 0 ? _profile.HoldSelectors[_holdStack[^1]] : _latchedLayerId;

    public bool AcceptsNewDowns => _state.AcceptsNewDowns;

    /// <summary>一つの物理 edge を処理し、送出すべき output edge 列を返す（layer 操作・未割当は空）。</summary>
    public IReadOnlyList<MappedOutputEdge> Process(PhysicalInput input)
    {
        if (input.DeviceInstanceId != _deviceInstanceId)
        {
            throw new ArgumentException(
                $"この runtime は device '{_deviceInstanceId}' 専用です。実際: '{input.DeviceInstanceId}'", nameof(input));
        }

        return input.Edge == PhysicalInputEdge.Down ? ProcessDown(input) : ProcessUp(input);
    }

    /// <summary>
    /// profile を差し替える。既存の所有 output は down 時の固定内容のまま維持され、
    /// 対応する up で旧 output 集合が解放される。layer は新 profile の default へ戻る。
    /// </summary>
    public void ApplyProfile(MappingProfile profile)
    {
        ValidateOutputGrammar(profile);
        _profile = profile;
        _latchedLayerId = profile.DefaultLayerId;
        _holdStack.Clear();
        _state = _state.ChangeProfile(profile.ProfileRevision, profile.DefaultLayerId, profile.MappingRevision);
    }

    /// <summary>新規 down を止め、全所有 output の up を返す（pause・切断・通常終了時）。</summary>
    public IReadOnlyList<MappedOutputEdge> StopAndReleaseAll()
    {
        var result = _state.StopAndReleaseAll();
        _state = result.State;
        _ownedControls.Clear();
        _holdStack.Clear();

        return result.Releases
            .SelectMany(release => release.Outputs)
            .Select(output => new MappedOutputEdge(output, PhysicalInputEdge.Up))
            .ToArray();
    }

    /// <summary>
    /// StopAndReleaseAll 後に新規 down の受理を再開する（device 再接続時）。
    /// layer は default へ戻る（切断前の latch／hold 状態は再接続後へ持ち越さない）。
    /// </summary>
    public void Resume()
    {
        _state = _state.Resume();
        _latchedLayerId = _profile.DefaultLayerId;
        SyncStateLayer();
    }

    private IReadOnlyList<MappedOutputEdge> ProcessDown(PhysicalInput input)
    {
        if (!_state.AcceptsNewDowns)
        {
            return [];
        }

        if (_profile.LatchSelectors.TryGetValue(input.ControlId, out var latchLayer))
        {
            _latchedLayerId = latchLayer;
            SyncStateLayer();
            return [];
        }

        if (_profile.HoldSelectors.ContainsKey(input.ControlId))
        {
            _holdStack.Add(input.ControlId);
            SyncStateLayer();
            return [];
        }

        if (!_profile.TryResolve(input.ControlId, CurrentLayerId, out var outputs))
        {
            return [];
        }

        if (OutputTokens.IsSequenceStep(outputs[0]))
        {
            return BuildSequenceEdges(outputs);
        }

        var result = _state.Down(input, outputs);
        _state = result.State;
        _ownedControls.Add(input.ControlId);

        return result.Ownership.Outputs
            .Select(output => new MappedOutputEdge(output, PhysicalInputEdge.Down))
            .ToArray();
    }

    private IReadOnlyList<MappedOutputEdge> ProcessUp(PhysicalInput input)
    {
        if (_ownedControls.Remove(input.ControlId))
        {
            var result = _state.Up(input);
            _state = result.State;

            return result.Release.Outputs
                .Select(output => new MappedOutputEdge(output, PhysicalInputEdge.Up))
                .ToArray();
        }

        // hold selector の解放。profile 変更や Stop で stack が消えた後の up は所有なしとして無視する。
        if (_holdStack.Remove(input.ControlId))
        {
            SyncStateLayer();
        }

        return [];
    }

    /// <summary>layer 変更を PressOwnershipState へ反映する（generation が進み、新規 down から有効になる）。</summary>
    private void SyncStateLayer() =>
        _state = _state.ChangeProfile(_profile.ProfileRevision, CurrentLayerId, _profile.MappingRevision);

    /// <summary>
    /// 有限 sequence（DEV-006）: 各段の chord down → 逆順 up を段順に並べた edge 列。
    /// 全 down が列内の up と対になるため所有を作らず、単一 Emit 呼び出しで完結する
    /// （MAP-008 の停止境界を構造で満たす）。
    /// </summary>
    private static IReadOnlyList<MappedOutputEdge> BuildSequenceEdges(IReadOnlyList<string> steps)
    {
        var edges = new List<MappedOutputEdge>();
        foreach (var step in steps)
        {
            var components = OutputTokens.SplitSequenceStep(step);
            foreach (var component in components)
            {
                edges.Add(new MappedOutputEdge(component, PhysicalInputEdge.Down));
            }

            for (var i = components.Count - 1; i >= 0; i--)
            {
                edges.Add(new MappedOutputEdge(components[i], PhysicalInputEdge.Up));
            }
        }

        return edges;
    }

    /// <summary>
    /// 全 binding の sequence 文法を検証する（fail early: sequence 段と押下保持 token の混在・
    /// 不正な sequence 段は、最初の keypress でなく profile 適用時にエラーにする）。
    /// 押下保持 token の解釈・検証は従来どおり Input Emitter の責務のまま。
    /// </summary>
    private static void ValidateOutputGrammar(MappingProfile profile)
    {
        foreach (var ((controlId, layerId), outputs) in profile.Bindings)
        {
            var isSequence = OutputTokens.IsSequenceStep(outputs[0]);
            foreach (var output in outputs)
            {
                if (OutputTokens.IsSequenceStep(output) != isSequence)
                {
                    throw new ArgumentException(
                        $"binding ({controlId}, {layerId}) で sequence 段（{OutputTokens.SequenceStepPrefix}…）と押下保持 token が混在しています。");
                }

                if (isSequence)
                {
                    OutputTokens.SplitSequenceStep(output);
                }
            }
        }
    }
}
