using System.Diagnostics;
using System.Text.Json;

namespace OpenLogicool.Host;

public sealed record FoundryLocalRuntime(Uri Endpoint, string ModelId);

/// <summary>Windowsへ導入されたFoundry Local CLIから実endpointとloaded modelを解決するvendor adapter。</summary>
public sealed class WindowsFoundryLocalRuntimeResolver
{
    public const string PreferredVisionModelAlias = "qwen3-vl-4b-instruct";

    public FoundryLocalRuntime Resolve()
    {
        var status = Run(10_000, "status", "-o", "json");
        var models = Run(10_000, "model", "list", "--loaded", "-o", "json");
        return Parse(status, models);
    }

    public FoundryLocalRuntime ResolvePreferredVisionModel()
    {
        var status = Run(10_000, "status", "-o", "json");
        var models = Run(10_000, "model", "list", "--loaded", "-o", "json");
        if (!HasLoadedMultimodalModel(models))
        {
            LoadModel(PreferredVisionModelAlias);
            models = Run(10_000, "model", "list", "--loaded", "-o", "json");
        }
        return Parse(status, models);
    }

    public static void LoadModel(string modelId) =>
        _ = Run(60_000, "model", "load", modelId, "-o", "json");

    public static void UnloadModel(string modelId) =>
        _ = Run(60_000, "model", "unload", modelId, "-o", "json");

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

    internal static bool HasLoadedMultimodalModel(string modelsJson)
    {
        using var models = JsonDocument.Parse(modelsJson);
        return models.RootElement.GetProperty("models").EnumerateArray()
            .Any(model => string.Equals(
                model.GetProperty("type").GetString(),
                "Multimodal",
                StringComparison.OrdinalIgnoreCase));
    }

    private static string Run(int timeoutMilliseconds, params string[] arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = "foundry.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Foundry Local CLIを開始できませんでした。");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(timeoutMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Foundry Local CLIが{timeoutMilliseconds}ms以内に応答しませんでした。");
        }
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Foundry Local CLIが失敗しました: stderr={error.Trim()} stdout={output.Trim()}");
        return output;
    }
}
