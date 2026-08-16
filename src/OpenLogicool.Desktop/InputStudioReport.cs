using OpenLogicool.Contracts.Devices.G13;
using OpenLogicool.Contracts.Devices.G600;
using OpenLogicool.Domain;

namespace OpenLogicool.Desktop;

/// <summary>UI へ渡す device 1種別分の入力（Host が列挙・復元して渡す。Desktop は I/O を持たない）。</summary>
public sealed record DeviceDisplayInput(
    string DeviceKind,
    int ConnectedInstanceCount,
    string? ProfileId,
    MappingProfile? Profile);

/// <summary>Supported control 表の1行（Phase 2 Exit 条件1: 欠落なく表示・変換）。</summary>
public sealed record ControlRow(
    string ControlId,
    string Evidence,
    string Capability,
    string Role,
    string BindingSummary);

/// <summary>device 1種別分の表示 section。</summary>
public sealed record DeviceSection(
    string DeviceKind,
    string ConnectionSummary,
    string OwnershipSummary,
    string ProfileSummary,
    IReadOnlyList<ControlRow> ControlRows,
    IReadOnlyList<string> Constraints);

/// <summary>Input Studio 初回起動画面の表示内容一式（UX-001 の骨格）。</summary>
public sealed record InputStudioReport(
    IReadOnlyList<DeviceSection> Devices,
    IReadOnlyList<string> EnvironmentNotes);

/// <summary>
/// 表示内容の pure 組み立て（表示計算をテスト可能にするため WPF から分離）。
/// capability は DEV-005 の4値のうち実測根拠があるものだけを表示する:
/// 実測台帳「確認済み」→ Supported、「強い推定」→ Experimental。Unverified を Supported と表示しない。
/// </summary>
public static class InputStudioReportBuilder
{
    public static InputStudioReport Build(DeviceDisplayInput g13, DeviceDisplayInput g600)
    {
        var g13Section = BuildSection(
            g13,
            G13Controls.Buttons,
            G13Controls.ConfirmedButtons,
            ownershipSummary: "read-only（input 読取のみ。onboard write・light/LCD 出力は未実装）",
            constraints:
            [
                "light・LCD 出力と onboard profile 転送面は Phase 8A まで未対応。",
                "スティック X/Y は生 sample の取得のみ（binding 対象は button edge だけ）。",
                "未確認 bit（byte5 bit6-7・byte7 bit1-2/4-7）は control として扱わない（Unverified を隠さない）。",
            ]);

        var g600Section = BuildSection(
            g600,
            G600Controls.Buttons,
            G600Controls.ConfirmedButtons,
            ownershipSummary: "onboard write 実証済み（restore・apply 往復・slot 切替）。残置適用は Input Studio profile 管理下",
            constraints:
            [
                "side remap は B変種が主経路: G9〜G20 の onboard 割当を中間 usage F13〜F24 へ書き換え、意味は Input Studio が解決する（A=slot 切替が補完・C は不採用）。",
                "onboard profile は 3 slot（F3/F4/F5）。F0 write で active slot を切替（実測成立）。",
                "F6 は read 不能（実測）。完全 backup の対象外として扱う。",
                "elevated foreground へは SendInput 配送不能（UIPI・実測）。elevated 中の held-output は watchdog release も不達の残留 risk（Supported matrix の行条件）。",
                "ホイール回転 tick は入力実測済みだが binding 対象外（button edge のみ）。",
            ]);

        return new InputStudioReport(
            [g13Section, g600Section],
            [
                "LGS / G HUB / driver の検出照合は未実装（Unverified）——本画面は表示骨格。",
                "capability 表記: Supported=実測「確認済み」／Experimental=実測「強い推定」。Unverified は Supported と表示しない（DEV-005）。",
            ]);
    }

    private static DeviceSection BuildSection(
        DeviceDisplayInput input,
        IReadOnlyList<string> catalog,
        IReadOnlySet<string> confirmed,
        string ownershipSummary,
        IReadOnlyList<string> constraints)
    {
        var rows = catalog
            .Select(controlId => new ControlRow(
                controlId,
                Evidence: confirmed.Contains(controlId) ? "確認済み" : "強い推定",
                Capability: confirmed.Contains(controlId) ? "Supported" : "Experimental",
                Role: ResolveRole(input.Profile, controlId),
                BindingSummary: ResolveBindingSummary(input.Profile, controlId)))
            .ToArray();

        return new DeviceSection(
            input.DeviceKind,
            ConnectionSummary: input.ConnectedInstanceCount > 0
                ? $"接続中（{input.ConnectedInstanceCount} 台）"
                : "未接続",
            ownershipSummary,
            ProfileSummary: input.Profile is null
                ? "profile 未設定（配線されない——黙って既定値を作らない）"
                : $"profile '{input.ProfileId}'（layer: {string.Join(", ", OrderedLayers(input.Profile))}）",
            rows,
            constraints);
    }

    private static string ResolveRole(MappingProfile? profile, string controlId)
    {
        if (profile is null)
        {
            return "—";
        }

        if (profile.LatchSelectors.TryGetValue(controlId, out var latchLayer))
        {
            return $"layer selector（latch → {latchLayer}）";
        }

        if (profile.HoldSelectors.TryGetValue(controlId, out var holdLayer))
        {
            return $"layer selector（hold → {holdLayer}）";
        }

        return "binding";
    }

    private static string ResolveBindingSummary(MappingProfile? profile, string controlId)
    {
        if (profile is null)
        {
            return "—";
        }

        if (profile.LatchSelectors.ContainsKey(controlId) || profile.HoldSelectors.ContainsKey(controlId))
        {
            return "—";
        }

        var cells = OrderedLayers(profile)
            .Select(layerId => profile.TryResolve(controlId, layerId, out var outputs)
                ? $"{layerId}: {string.Join(", ", outputs)}"
                : null)
            .OfType<string>()
            .ToArray();

        return cells.Length == 0 ? "未割当" : string.Join(" ／ ", cells);
    }

    /// <summary>表示用の layer 順: default を先頭に、残りは Ordinal 昇順（決定的表示）。</summary>
    private static IReadOnlyList<string> OrderedLayers(MappingProfile profile) =>
        [profile.DefaultLayerId, .. profile.LayerIds.Where(layerId => layerId != profile.DefaultLayerId).Order(StringComparer.Ordinal)];
}
