using OpenLogicool.Desktop;

namespace OpenLogicool.Host;

/// <summary>
/// Desktop の出力設定 intent を machine-local store と SetupAPI/protocol handshake へ接続する。
/// 保存する identity は PnP device instance ID だけで、COM 番号は毎回列挙結果から解決する。
/// </summary>
public sealed class HostSerialHidSettingsIntent(
    SerialHidOutputSettingsStore store,
    SerialHidDiscoveryService discovery,
    Func<ResidentOutputRoute?> activeRoute)
    : ISerialHidSettingsIntent
{
    public SerialHidSettingsSnapshot Load()
    {
        var settings = store.Load();
        IReadOnlyList<SerialHidCandidateChoice> candidates;
        string? discoveryError = null;
        try
        {
            candidates = discovery.ListCandidates()
                .Select(candidate => new SerialHidCandidateChoice(candidate.DeviceInstanceId, candidate.DisplayName))
                .ToArray();
        }
        catch (Exception exception)
        {
            candidates = [];
            discoveryError = $"候補を列挙できません: {exception.Message}";
        }

        var active = activeRoute();
        return Snapshot(settings, candidates, discoveryError ?? DescribeStatus(settings.RequestedRoute, active));
    }

    public SerialHidSettingsSaveResult SaveAndTest(
        OutputRouteChoice requestedRoute,
        string? selectedDeviceInstanceId)
    {
        var route = ToResidentRoute(requestedRoute);
        if (route == ResidentOutputRoute.SerialHid)
        {
            if (string.IsNullOrWhiteSpace(selectedDeviceInstanceId))
            {
                var current = Load();
                return new SerialHidSettingsSaveResult(
                    false,
                    current with { StatusLine = "USB出力に使うSparkFun Pro Microを選んでください。" });
            }

            var test = discovery.Test(selectedDeviceInstanceId);
            if (!test.Success)
            {
                var current = Load();
                return new SerialHidSettingsSaveResult(false, current with { StatusLine = test.StatusLine });
            }
        }

        var settings = new SerialHidOutputSettings(
            SerialHidOutputSettings.CurrentSchemaVersion,
            route,
            route == ResidentOutputRoute.SerialHid ? selectedDeviceInstanceId : null);
        store.Save(settings);

        var snapshot = Load();
        return new SerialHidSettingsSaveResult(
            true,
            snapshot with
            {
                StatusLine = route == ResidentOutputRoute.SerialHid
                    ? $"接続確認済み。{DescribeStatus(route, activeRoute())}"
                    : DescribeStatus(route, activeRoute()),
            });
    }

    private SerialHidSettingsSnapshot Snapshot(
        SerialHidOutputSettings settings,
        IReadOnlyList<SerialHidCandidateChoice> candidates,
        string statusLine) =>
        new(
            ToChoice(settings.RequestedRoute),
            activeRoute() is { } route ? ToChoice(route) : null,
            settings.SelectedDeviceInstanceId,
            candidates,
            statusLine);

    public static string DescribeStatus(ResidentOutputRoute requested, ResidentOutputRoute? active)
    {
        var requestedLabel = DescribeRoute(requested);
        if (active is null)
        {
            return $"保存済み: {requestedLabel}。次に常駐を起動したときから使います。";
        }

        if (active == requested)
        {
            return $"使用中: {requestedLabel}";
        }

        return $"保存済み: {requestedLabel}。現在は{DescribeRoute(active.Value)}を使用中で、次回起動から切り替わります。";
    }

    private static string DescribeRoute(ResidentOutputRoute route) => route switch
    {
        ResidentOutputRoute.SendInput => "Windows出力（SendInput）",
        ResidentOutputRoute.SerialHid => "USB出力（Serial HID）",
        _ => throw new ArgumentOutOfRangeException(nameof(route)),
    };

    private static OutputRouteChoice ToChoice(ResidentOutputRoute route) => route switch
    {
        ResidentOutputRoute.SendInput => OutputRouteChoice.SendInput,
        ResidentOutputRoute.SerialHid => OutputRouteChoice.SerialHid,
        _ => throw new ArgumentOutOfRangeException(nameof(route)),
    };

    private static ResidentOutputRoute ToResidentRoute(OutputRouteChoice route) => route switch
    {
        OutputRouteChoice.SendInput => ResidentOutputRoute.SendInput,
        OutputRouteChoice.SerialHid => ResidentOutputRoute.SerialHid,
        _ => throw new ArgumentOutOfRangeException(nameof(route)),
    };
}
