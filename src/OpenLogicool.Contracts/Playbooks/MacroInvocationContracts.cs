using System.Text;

namespace OpenLogicool.Contracts.Playbooks;

public enum MacroPlaybackMode
{
    AiFree,
    AiMonitored,
}

public sealed record MacroVersionReference(
    string RouteId,
    string? VersionId,
    MacroPlaybackMode PlaybackMode);

/// <summary>Workspace actionからfast pathへ渡すmacro version参照token。</summary>
public static class MacroInvocationTokens
{
    public const string Prefix = "Macro:";

    public static string Create(MacroVersionReference reference)
    {
        Validate(reference);
        var version = reference.VersionId is null ? "latest" : Encode(reference.VersionId);
        return $"{Prefix}{Mode(reference.PlaybackMode)}:{Encode(reference.RouteId)}:{version}";
    }

    public static bool IsMacro(string token) =>
        token?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    public static MacroVersionReference Parse(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if (!IsMacro(token))
        {
            throw new ArgumentException("macro invocation tokenではありません。", nameof(token));
        }
        var fields = token[Prefix.Length..].Split(':');
        if (fields.Length != 3)
        {
            throw new ArgumentException("macro invocation tokenのfield数が不正です。", nameof(token));
        }
        var reference = new MacroVersionReference(
            Decode(fields[1]),
            fields[2] == "latest" ? null : Decode(fields[2]),
            fields[0] switch
            {
                "free" => MacroPlaybackMode.AiFree,
                "monitored" => MacroPlaybackMode.AiMonitored,
                _ => throw new ArgumentException("macro playback modeが不正です。", nameof(token)),
            });
        Validate(reference);
        return reference;
    }

    private static string Mode(MacroPlaybackMode mode) => mode switch
    {
        MacroPlaybackMode.AiFree => "free",
        MacroPlaybackMode.AiMonitored => "monitored",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string Decode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        }
        catch (FormatException error)
        {
            throw new ArgumentException("macro invocation tokenの参照がBase64URLではありません。", nameof(value), error);
        }
    }

    private static void Validate(MacroVersionReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference.RouteId);
        if (reference.VersionId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(reference.VersionId);
        _ = Mode(reference.PlaybackMode);
    }
}
