namespace OpenLogicool.Desktop;

public enum OutputRouteChoice
{
    SendInput,
    SerialHid,
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

