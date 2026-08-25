using System.Diagnostics;
using System.Text.Json;

namespace OpenLogicool.Host;

public sealed record FoundryLocalRuntime(Uri Endpoint, string ModelId);

/// <summary>Windowsへ導入されたFoundry Local CLIから実endpointとloaded modelを解決するvendor adapter。</summary>
public sealed class WindowsFoundryLocalRuntimeResolver
{
    public FoundryLocalRuntime Resolve()
    {
        var status = Run("status -o json");
        var models = Run("model list --loaded -o json");
        return Parse(status, models);
    }

    public static FoundryLocalRuntime Parse(string statusJson, string modelsJson)
    {
        using var status = JsonDocument.Parse(statusJson);
        var service = status.RootElement.GetProperty("service");
        if (!service.GetProperty("ready").GetBoolean())
            throw new InvalidOperationException("Foundry Local serviceがreadyではありません。");
        var endpointText = service.GetProperty("webUrls").EnumerateArray()
            .Select(value => value.GetString())
            .FirstOrDefault(value => Uri.TryCreate(value, UriKind.Absolute, out _))
            ?? throw new InvalidOperationException("Foundry Local endpointがありません。");

        using var models = JsonDocument.Parse(modelsJson);
        var loaded = models.RootElement.GetProperty("models").EnumerateArray()
            .Where(model => string.Equals(model.GetProperty("type").GetString(), "Multimodal", StringComparison.OrdinalIgnoreCase))
            .Select(model => model.GetProperty("id").GetString())
            .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id))
            ?? throw new InvalidOperationException("Foundry LocalでloadedなMultimodal modelがありません。");
        return new FoundryLocalRuntime(new Uri(endpointText), loaded);
    }

    private static string Run(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "foundry.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("Foundry Local CLIを開始できませんでした。");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(10_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Foundry Local CLIが10秒以内に応答しませんでした。");
        }
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Foundry Local CLIが失敗しました: {error.Trim()}");
        return output;
    }
}
