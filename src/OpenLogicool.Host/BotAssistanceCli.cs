using System.IO;
using System.Text.Json;

namespace OpenLogicool.Host;

internal static class BotAssistanceCli
{
    public static int Run(string[] arguments, string defaultDatabasePath)
    {
        try { return RunAsync(arguments, defaultDatabasePath).GetAwaiter().GetResult(); }
        catch (Exception error)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { success = false, error = error.Message }, BotAssistanceStore.Json));
            return 2;
        }
    }

    private static async Task<int> RunAsync(string[] arguments, string defaultDatabasePath)
    {
        string? Option(string name)
        {
            var index = Array.IndexOf(arguments, name);
            return index < 0 ? null : index + 1 < arguments.Length ? arguments[index + 1]
                : throw new ArgumentException(name + "の値が必要です。");
        }
        var database = Path.GetFullPath(Option("--db") ?? defaultDatabasePath);
        var store = new BotAssistanceStore(database);
        var command = arguments.FirstOrDefault() ?? "status";
        (string? Kind, string? ThreadId) Who() => Caller(Option("--harness"), Environment.GetEnvironmentVariable);
        object result;
        switch (command)
        {
            case "attach" when Who() is (BotAssistantKind.Claude, { } session):
                var inbox = new ClaudeInboxTarget(session,
                    Environment.GetEnvironmentVariable("CLAUDE_CODE_MESSAGING_SOCKET") is { Length: > 0 } socket ? socket
                        : throw new InvalidOperationException("この会話の受信箱を確認できません。Claudeの会話の標準shellから実行してください。"),
                    Environment.GetEnvironmentVariable("CLAUDE_CODE_MESSAGING_TOKEN") is { Length: > 0 } key ? key
                        : throw new InvalidOperationException("この会話の受信箱の鍵を確認できません。Claudeの会話の標準shellから実行してください。"));
                result = await BotAssistanceCoordinator.Create(database).AttachAsync(session, "", CancellationToken.None,
                    takeover: arguments.Contains("--takeover", StringComparer.Ordinal), inbox);
                break;
            case "attach":
                var home = Path.GetFullPath(Environment.GetEnvironmentVariable("CODEX_HOME")
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"));
                result = await BotAssistanceCoordinator.Create(database).AttachAsync(Required(Who().ThreadId), home, CancellationToken.None,
                    takeover: arguments.Contains("--takeover", StringComparer.Ordinal));
                break;
            case "status":
                result = store.Read();
                break;
            case "claim" when arguments.Length > 1:
                result = store.Claim(arguments[1], Required(Who().ThreadId));
                break;
            case "resolve" when arguments.Length > 1:
                result = store.Resolve(arguments[1], Required(Who().ThreadId), Option("--reason")
                    ?? throw new ArgumentException("完了理由を--reasonで指定してください。"));
                break;
            default:
                throw new ArgumentException("assistant attach|status|claim <案件ID>|resolve <案件ID> --reason <理由> を指定します。");
        }
        Console.WriteLine(JsonSerializer.Serialize(new { success = true, result }, BotAssistanceStore.Json));
        return 0;
    }

    private static string Required(string? caller) => caller
        ?? throw new InvalidOperationException("現在の会話を確認できません。CodexまたはClaudeの会話の標準shellから実行してください。");

    /// <summary>呼び出した会話と、その宛先の種類を決める。両方の会話の中にいる時は推測せず指定を求める。</summary>
    internal static (string? Kind, string? ThreadId) Caller(string? harness, Func<string, string?> environment)
    {
        var found = new[] { (Kind: BotAssistantKind.Codex, Name: "CODEX_THREAD_ID"), (Kind: BotAssistantKind.Claude, Name: "CLAUDE_CODE_SESSION_ID") }
            .Where(item => harness is null || harness == item.Kind)
            .Select(item => (item.Kind, Id: Guid.TryParse(environment(item.Name), out var id) ? id.ToString() : null))
            .Where(item => item.Id is not null).ToArray();
        if (harness is not null and not (BotAssistantKind.Codex or BotAssistantKind.Claude))
            throw new ArgumentException("--harness は codex か claude を指定します。");
        return found.Length switch
        {
            0 => (null, null),
            1 => (found[0].Kind, found[0].Id),
            _ => throw new InvalidOperationException("CodexとClaudeの両方の会話を検出しました。--harness codex|claude で担当にする会話を指定してください。"),
        };
    }
}
