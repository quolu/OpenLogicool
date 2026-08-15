using System;
using System.Runtime.InteropServices;

namespace OpenLogicool.Desktop.SmokeApp;

/// <summary>
/// 実行時に取得した事実だけを保持する。Phase 0 の器成立確認用。
/// </summary>
public sealed record EnvironmentReport(
    string FrameworkDescription,
    string OsDescription,
    string ProcessArchitecture,
    DateTime StartedAtUtc)
{
    public static EnvironmentReport Capture() => new(
        RuntimeInformation.FrameworkDescription,
        RuntimeInformation.OSDescription,
        RuntimeInformation.ProcessArchitecture.ToString(),
        DateTime.UtcNow);
}
