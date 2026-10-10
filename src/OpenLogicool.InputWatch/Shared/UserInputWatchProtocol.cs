using System.Globalization;

namespace OpenLogicool.InputWatch;

/// <summary>
/// 管理者権限の監視processからBot本体への連絡。監視が一行ずつ送るだけの一方通行で、Bot本体からは何も受け取らない。
/// 押下の数と無入力の時間だけを渡し、どのキーかは渡さない。
/// </summary>
internal static class UserInputWatchProtocol
{
    public const string PipeName = "OpenLogicool.UserInputWatch";
    /// <summary>監視processを管理者権限で起動する、登録済みのタスク（scripts/install-user-input-watch.ps1）。</summary>
    public const string TaskName = @"\OpenLogicool\UserInputWatch";
    public const int ReportIntervalMs = 50;
    /// <summary>監視processは、Bot本体の通信口がこの時間つながらなければ終了する。</summary>
    public const int ConnectTimeoutMs = 10_000;

    public static string Ready(int integrity) => FormattableString.Invariant($"ready {integrity}");

    public static int ParseReady(string? line) =>
        line?.Split(' ') is ["ready", var level] && int.TryParse(level, NumberStyles.None, CultureInfo.InvariantCulture, out var integrity)
            ? integrity : throw new FormatException($"監視processの開始の合図を読めません: {line}");

    public static string Format(UserInputPauseSnapshot snapshot) => FormattableString.Invariant(
        $"{snapshot.HeldCount} {snapshot.IdleMilliseconds} {snapshot.UserEvents} {snapshot.NanoEvents} {snapshot.LostReleases}");

    public static UserInputPauseSnapshot Parse(string line)
    {
        var parts = line.Split(' ');
        if (parts.Length != 5 || parts.Any(part => !long.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            throw new FormatException($"監視processの報告を読めません: {line}");
        var values = parts.Select(part => long.Parse(part, CultureInfo.InvariantCulture)).ToArray();
        return new(values[0] > 0 || values[1] < UserInputPauseState.QuietMilliseconds, (int)values[0], values[1], values[2], values[3],
            values[4], []);
    }
}

/// <summary>Botの出力機器（Serial HIDのNano）を、Raw Inputの機器のパスで見分ける。</summary>
internal static class BotOutputDevices
{
    /// <summary>
    /// 製品が出力に使う基板（SparkFun Pro Microの実行時のID）か。監視processは、Bot本体から機器の指定を受け取らずに、
    /// この固定の条件だけで見分ける。
    /// </summary>
    public static bool IsSerialHidOutput(string devicePath) =>
        devicePath.Contains("VID_1B4F", StringComparison.OrdinalIgnoreCase)
        && (devicePath.Contains("PID_9205", StringComparison.OrdinalIgnoreCase)
            || devicePath.Contains("PID_9206", StringComparison.OrdinalIgnoreCase));
}
