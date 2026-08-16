using System.Collections.ObjectModel;

namespace OpenLogicool.Domain;

/// <summary>
/// 一つの device 種別に対する mapping の pure data（計画 §6.3: Domain が Profile の pure model を持つ）。
/// layer（G13 M1/M2/M3 相当の latch と G600 G-Shift 相当の hold）と
/// (control, layer) → outputs の binding を持つ。output token の解釈は Input Emitter の責務。
/// </summary>
public sealed class MappingProfile
{
    private readonly IReadOnlyDictionary<(string ControlId, string LayerId), IReadOnlyList<string>> _bindings;

    public MappingProfile(
        string profileRevision,
        string mappingRevision,
        string defaultLayerId,
        IEnumerable<string> layerIds,
        IReadOnlyDictionary<string, string> latchSelectors,
        IReadOnlyDictionary<string, string> holdSelectors,
        IEnumerable<MappingBinding> bindings)
    {
        var layers = new HashSet<string>(layerIds, StringComparer.Ordinal);
        if (layers.Count == 0)
        {
            throw new ArgumentException("layer が一つも定義されていません。", nameof(layerIds));
        }

        if (!layers.Contains(defaultLayerId))
        {
            throw new ArgumentException($"default layer '{defaultLayerId}' が layer 集合に存在しません。", nameof(defaultLayerId));
        }

        foreach (var (controlId, layerId) in latchSelectors.Concat(holdSelectors))
        {
            if (!layers.Contains(layerId))
            {
                throw new ArgumentException($"selector '{controlId}' の対象 layer '{layerId}' が layer 集合に存在しません。");
            }
        }

        var latchControls = new HashSet<string>(latchSelectors.Keys, StringComparer.Ordinal);
        foreach (var holdControl in holdSelectors.Keys)
        {
            if (latchControls.Contains(holdControl))
            {
                throw new ArgumentException($"control '{holdControl}' が latch selector と hold selector の両方に指定されています。");
            }
        }

        var bindingTable = new Dictionary<(string, string), IReadOnlyList<string>>();
        foreach (var binding in bindings)
        {
            if (!layers.Contains(binding.LayerId))
            {
                throw new ArgumentException($"binding ({binding.ControlId}, {binding.LayerId}) の layer が layer 集合に存在しません。");
            }

            if (latchControls.Contains(binding.ControlId) || holdSelectors.ContainsKey(binding.ControlId))
            {
                throw new ArgumentException($"layer selector control '{binding.ControlId}' へ output binding を割り当てられません。");
            }

            if (binding.Outputs.Count == 0)
            {
                throw new ArgumentException($"binding ({binding.ControlId}, {binding.LayerId}) の outputs が空です。");
            }

            if (!bindingTable.TryAdd((binding.ControlId, binding.LayerId), Array.AsReadOnly(binding.Outputs.ToArray())))
            {
                throw new ArgumentException($"binding ({binding.ControlId}, {binding.LayerId}) が重複しています。");
            }
        }

        ProfileRevision = profileRevision;
        MappingRevision = mappingRevision;
        DefaultLayerId = defaultLayerId;
        LayerIds = layers;
        LatchSelectors = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(latchSelectors, StringComparer.Ordinal));
        HoldSelectors = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(holdSelectors, StringComparer.Ordinal));
        _bindings = bindingTable;
    }

    public string ProfileRevision { get; }

    public string MappingRevision { get; }

    public string DefaultLayerId { get; }

    public IReadOnlySet<string> LayerIds { get; }

    /// <summary>latch selector（押下で layer を切替えて保持。G13 M1/M2/M3 相当）: control → layer。</summary>
    public IReadOnlyDictionary<string, string> LatchSelectors { get; }

    /// <summary>hold selector（押下中だけ layer を上書き。G600 G-Shift 相当）: control → layer。</summary>
    public IReadOnlyDictionary<string, string> HoldSelectors { get; }

    /// <summary>全 binding: (control, layer) → outputs。token 文法の検証者（Input 層）が列挙に使う。</summary>
    public IReadOnlyDictionary<(string ControlId, string LayerId), IReadOnlyList<string>> Bindings => _bindings;

    public bool TryResolve(string controlId, string layerId, out IReadOnlyList<string> outputs) =>
        _bindings.TryGetValue((controlId, layerId), out outputs!);
}

/// <summary>(control, layer) に対する output token 列。</summary>
public sealed record MappingBinding(
    string ControlId,
    string LayerId,
    IReadOnlyList<string> Outputs);
