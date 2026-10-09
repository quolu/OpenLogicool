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
        string Caller()
        {
            var identity = Environment.GetEnvironmentVariable("CODEX_THREAD_ID");
            if (!Guid.TryParse(identity, out var id))
                throw new InvalidOperationException("現在のCodex会話を確認できません。会話の標準shellから実行してください。");
            return id.ToString();
        }
        object result;
        switch (command)
        {
            case "attach":
                var home = Path.GetFullPath(Environment.GetEnvironmentVariable("CODEX_HOME")
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"));
                result = await BotAssistanceCoordinator.Create(database).AttachAsync(Caller(), home, CancellationToken.None);
                break;
            case "status":
                result = store.Read();
                break;
            case "claim" when arguments.Length > 1:
                result = store.Claim(arguments[1], Caller());
                break;
            case "resolve" when arguments.Length > 1:
                result = store.Resolve(arguments[1], Caller(), Option("--reason")
                    ?? throw new ArgumentException("完了理由を--reasonで指定してください。"));
                break;
            default:
                throw new ArgumentException("assistant attach|status|claim <案件ID>|resolve <案件ID> --reason <理由> を指定します。");
        }
        Console.WriteLine(JsonSerializer.Serialize(new { success = true, result }, BotAssistanceStore.Json));
        return 0;
    }
}
