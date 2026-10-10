using OpenLogicool.Contracts.Profiles;

namespace OpenLogicool.Host;

/// <summary>
/// app-firstの既存判断から、G13のバックライト色を前面アプリの音へ合わせるかを選ぶpure境界。
/// 明示一致したworkspaceの設定で決め、一致がなければ共通設定で決める。
/// </summary>
public static class G13BacklightProfileSettingSelector
{
    public static bool FollowsAudio(
        ProfileSwitchDecision decision,
        IReadOnlyDictionary<string, MappingProfileDocument> documentsById,
        MappingProfileDocument? defaultG13Document)
    {
        var matched = decision.Outcomes.FirstOrDefault(
            outcome => outcome.MatchKind is "path" or "package");
        if (matched is not null &&
            documentsById.TryGetValue(matched.SelectedProfileId, out var matchedDocument))
        {
            return matchedDocument.G13BacklightFollowsAudio;
        }

        return defaultG13Document?.G13BacklightFollowsAudio ?? false;
    }
}
