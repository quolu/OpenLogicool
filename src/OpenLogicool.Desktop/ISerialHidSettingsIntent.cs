namespace OpenLogicool.Desktop;

public enum OutputRouteChoice
{
    SendInput,
    SerialHid,
}

/// <summary>Serial HID設定面と公開support matrixで共有する利用制約。</summary>
public static class SerialHidSettingsPresentation
{
    public const string LimitNotice =
        "USB出力（Serial HID v1）は通常キー同時6個までです。7個以上の同時押し、マウス移動・ホイール、音量などの特殊キーには対応していません。対応外の割り当ては部分送出せず停止します。";
}

public sealed record SerialHidCandidateChoice(string DeviceInstanceId, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed record SerialHidSettingsSnapshot(
    OutputRouteChoice RequestedRoute,
    OutputRouteChoice? ActiveRoute,
    string? SelectedDeviceInstanceId,
    IReadOnlyList<SerialHidCandidateChoice> Candidates,
    string StatusLine);

public sealed record SerialHidSettingsSaveResult(bool Success, SerialHidSettingsSnapshot Snapshot);

public interface ISerialHidSettingsIntent
{
    SerialHidSettingsSnapshot Load();
    SerialHidSettingsSaveResult SaveAndTest(OutputRouteChoice requestedRoute, string? selectedDeviceInstanceId);
}
