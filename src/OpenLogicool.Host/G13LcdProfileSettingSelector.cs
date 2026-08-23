using OpenLogicool.Contracts.Profiles;

namespace OpenLogicool.Host;

/// <summary>
/// app-firstの既存判断からG13 LCD設定を選ぶpure境界。
/// 明示一致したworkspaceの設定を優先し、設定がなければ共通設定へ戻す。
/// </summary>
public static class G13LcdProfileSettingSelector
{
    public static WorkspaceG13LcdSetting? Select(
        ProfileSwitchDecision decision,
        IReadOnlyDictionary<string, MappingProfileDocument> documentsById,
        MappingProfileDocument? defaultG13Document)
    {
        var matched = decision.Outcomes.FirstOrDefault(
            outcome => outcome.MatchKind is "path" or "package");
        if (matched is not null &&
            documentsById.TryGetValue(matched.SelectedProfileId, out var matchedDocument) &&
            matchedDocument.G13Lcd is not null)
        {
            return matchedDocument.G13Lcd;
        }

        return defaultG13Document?.G13Lcd;
    }
}
