using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenLogicool.Host;

internal static class ApplicationControlCli
{
    public static int Run(string group, string[] arguments)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        try { return RunAsync(group, arguments).GetAwaiter().GetResult(); }
        catch (Exception error)
        {
            Console.WriteLine(JsonSerializer.Serialize(new ControlResponse(false,
                Error: new("command-failed", error.Message)), ControlOperationRegistry.Json));
            return 2;
        }
    }

    private static async Task<int> RunAsync(string group, string[] arguments)
    {
        var verb = arguments.FirstOrDefault() ?? (group == "app" ? "status" : "operations");
        string? Option(string name)
        {
            var index = Array.IndexOf(arguments, name);
            if (index < 0) return null;
            return index + 1 < arguments.Length ? arguments[index + 1] : throw new ArgumentException($"{name}の値が必要です。");
        }
        var wait = arguments.Contains("--wait", StringComparer.Ordinal) || group is "bot" or "app";
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var jsonText = Option("--json") ?? (Option("--json-file") is { } input ? File.ReadAllText(input) : "{}");
            var parameters = JsonNode.Parse(jsonText) as JsonObject ?? throw new ArgumentException("引数JSONはobjectです。");
            var reserved = new HashSet<string>(["--wait", "--json", "--json-file", "--out", "--db"], StringComparer.Ordinal);
            for (var index = 0; index < arguments.Length; index++)
            {
                if (!arguments[index].StartsWith("--", StringComparison.Ordinal)) continue;
                if (reserved.Contains(arguments[index])) { if (arguments[index] != "--wait") index++; continue; }
                var parts = arguments[index][2..].Split('-');
                var key = parts[0] + string.Concat(parts.Skip(1).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
                if (++index >= arguments.Length) throw new ArgumentException($"引数が必要です: {key}");
                try { parameters[key] = JsonNode.Parse(arguments[index]); }
                catch (JsonException) { parameters[key] = arguments[index]; }
            }

            foreach (var property in parameters.ToArray())
            {
                if (!new[] { "path", "imagePath", "applicationFullPath" }.Contains(property.Key, StringComparer.OrdinalIgnoreCase)
                    || property.Value is not JsonValue pathValue || !pathValue.TryGetValue<string>(out var filePath)
                    || filePath == "*" || filePath.StartsWith("package:", StringComparison.OrdinalIgnoreCase)) continue;
                parameters[property.Key] = Path.GetFullPath(filePath);
            }
            ControlRequest request;
            if (group == "control")
            {
                request = verb switch
                {
                    "operations" => new("operations"),
                    "invoke" when arguments.Length > 1 => new("invoke", arguments[1], JsonSerializer.SerializeToElement(parameters)),
                    "jobs" => new("jobs"),
                    "job" when arguments.Length > 1 => new("job", JobId: arguments[1]),
                    "cancel" when arguments.Length > 1 => new("cancel", JobId: arguments[1]),
                    _ => throw new ArgumentException("control operations|invoke <操作ID>|jobs|job <実行ID>|cancel <実行ID> を指定します。")
                };
            }
            else
            {
                var operation = group + "." + verb;
                if (group == "device") operation = "devices." + verb;
                if (group == "profile") operation = verb switch
                { "list" => "profiles.list", "load" => "workspace.load-document", "save" => "workspace.save", "compile" => "workspace.compile", "undo" => "workspace.undo", _ => operation };
                if (group == "bot")
                {
                    operation = verb switch { "list" => "bot.list-scripts", "status" => "bot.current", "modes" => "bot.list-modes", "functions" => "bot.list-functions", _ => operation };
                    if (verb == "start" && arguments.Length > 1 && !arguments[1].StartsWith("--", StringComparison.Ordinal)) parameters["scriptId"] = arguments[1];
                    // bot mode <モード> で入り、bot mode off で解除する。
                    if (verb == "mode")
                    {
                        if (arguments.Length < 2 || arguments[1].StartsWith("--", StringComparison.Ordinal))
                            throw new ArgumentException("bot mode <モード>|off を指定します。モードの一覧は bot modes で見られます。");
                        operation = arguments[1] == "off" ? "bot.clear-mode" : "bot.set-mode";
                        if (arguments[1] != "off") parameters["modeId"] = arguments[1];
                    }
                }
                request = new("invoke", operation, JsonSerializer.SerializeToElement(parameters));
            }
            request = request with { ExpectedDatabasePath = Option("--db") is { } expected ? Path.GetFullPath(expected) : null };

            ControlResponse response;
            try { response = await ApplicationControlPipe.Send(request, token: cancellation.Token); }
            catch (TimeoutException)
            {
                if (group == "app" && verb == "status")
                    response = new(true, JsonSerializer.SerializeToElement(new { running = LegacyApps().Length > 0, apiAvailable = false }));
                else if (group == "app" && verb == "close")
                    response = await CloseLegacy(cancellation.Token);
                else
                {
                    if (LegacyApps().Length > 0) throw new InvalidOperationException("起動中の旧版には操作APIがありません。app closeで通常終了し、更新してください。");
                    Launch(Option("--db"));
                    response = await ConnectAfterLaunch(request, cancellation.Token);
                }
            }
            if (response.Success && response.Value is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty("id", out var id))
            {
                var jobId = id.GetString()!;
                if (wait)
                {
                    while (value.GetProperty("state").GetString() is "queued" or "running")
                    {
                        await Task.Delay(100, cancellation.Token);
                        response = await ApplicationControlPipe.Send(new("job", JobId: jobId), token: cancellation.Token);
                        if (!response.Success) break;
                        value = response.Value!.Value;
                    }
                }
                if (response.Success && request.Action != "cancel" && value.GetProperty("state").GetString() is "faulted" or "cancelled")
                    response = new(false, value, value.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                        ? error.Deserialize<ControlFault>(ControlOperationRegistry.Json) : new("cancelled", "操作を中止しました。"));
                if (response.Success && request.Operation == "app.close" && value.GetProperty("state").GetString() == "completed")
                {
                    var pid = value.GetProperty("result").GetProperty("processId").GetInt32();
                    try { using var process = Process.GetProcessById(pid); await process.WaitForExitAsync(cancellation.Token); }
                    catch (ArgumentException) { }
                }
            }
            var text = JsonSerializer.Serialize(response, ControlOperationRegistry.Json);
            if (Option("--out") is { } output)
            {
                var path = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text);
            }
            Console.WriteLine(text);
            return response.Success ? 0 : 2;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }

    private static Process[] LegacyApps() => Process.GetProcessesByName("OpenLogicool.Host")
        .Where(process => process.Id != Environment.ProcessId && process.MainWindowHandle != 0
            && process.MainWindowTitle.Contains("OpenLogicool", StringComparison.Ordinal)).ToArray();

    private static async Task<ControlResponse> CloseLegacy(CancellationToken token)
    {
        var processes = LegacyApps();
        if (processes.Length == 0) return new(true, JsonSerializer.SerializeToElement(new { running = false }));
        if (processes.Length != 1) throw new InvalidOperationException("通常終了の対象アプリを一意に選べません。");
        using var process = processes[0];
        if (!process.CloseMainWindow()) throw new IOException("旧版アプリへ通常終了を要求できません。");
        await process.WaitForExitAsync(token);
        return new(true, JsonSerializer.SerializeToElement(new { running = false, transport = "windows-graceful-close",
            detail = "操作API導入前の起動中アプリを通常終了しました。強制終了はしていません。" }));
    }

    private static void Launch(string? databasePath)
    {
        var launcher = Path.Combine(AppContext.BaseDirectory, "OpenLogicool.Launcher.exe");
        if (!File.Exists(launcher)) throw new FileNotFoundException("同梱のOpenLogicool.Launcher.exeが必要です。", launcher);
        var start = new ProcessStartInfo(launcher) { UseShellExecute = true };
        if (databasePath is not null)
        {
            start.ArgumentList.Add("--host"); start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "OpenLogicool.Host.exe"));
            start.ArgumentList.Add("ui"); start.ArgumentList.Add("--resident"); start.ArgumentList.Add("--db"); start.ArgumentList.Add(Path.GetFullPath(databasePath));
        }
        _ = Process.Start(start) ?? throw new IOException("アプリを起動できません。");
    }

    private static async Task<ControlResponse> ConnectAfterLaunch(ControlRequest request, CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            try { return await ApplicationControlPipe.Send(request, token: token); }
            catch (TimeoutException) when (clock.Elapsed < TimeSpan.FromSeconds(30)) { await Task.Delay(100, token); }
        }
    }
}
