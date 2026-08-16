using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Domain;

namespace OpenLogicool.Profiles;

/// <summary>
/// MappingProfileDocument（永続化 wire type）と Domain の MappingProfile の相互変換。
/// 内容検証は MappingProfile の構築子が行う——不正な document はここで例外として現れ、
/// 黙って捨てたり既定値で埋めたりしない。
/// </summary>
public static class MappingProfileMaterializer
{
    public static MappingProfile ToProfile(MappingProfileDocument document)
    {
        if (document.SchemaVersion != ContractSchemaVersions.Revision01)
        {
            throw new ArgumentException(
                $"MappingProfileDocument schema version '{document.SchemaVersion}' は未対応です（対応: {ContractSchemaVersions.Revision01}）。",
                nameof(document));
        }

        return new MappingProfile(
            document.ProfileRevision,
            document.MappingRevision,
            document.DefaultLayerId,
            document.LayerIds,
            ToSelectorDictionary(document.LatchSelectors, "latch"),
            ToSelectorDictionary(document.HoldSelectors, "hold"),
            document.Bindings.Select(binding => new MappingBinding(binding.ControlId, binding.LayerId, binding.Outputs)));
    }

    private static Dictionary<string, string> ToSelectorDictionary(
        IReadOnlyList<LayerSelectorEntry> entries,
        string selectorKind)
    {
        var selectors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!selectors.TryAdd(entry.ControlId, entry.LayerId))
            {
                throw new ArgumentException($"{selectorKind} selector の control '{entry.ControlId}' が重複しています。");
            }
        }

        return selectors;
    }
}
