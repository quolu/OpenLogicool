using OpenLogicool.AI;
using OpenLogicool.Contracts.AI;

namespace OpenLogicool.Host;

/// <summary>AI探索が実際に必要になった時だけWindows上のFoundry Localを解決するvendor adapter。</summary>
public sealed class WindowsLazyFoundryControlDiscoveryProvider(
    Func<FoundryLocalRuntime> resolveRuntime) : ILocalControlDiscoveryProvider, IDisposable
{
    private readonly Lazy<(FoundryLocalVisionClient Client, FoundryLocalControlDiscoveryProvider Provider)> lazy =
        new(() => Create(resolveRuntime), LazyThreadSafetyMode.ExecutionAndPublication);

    public bool IsResolved => lazy.IsValueCreated;

    public Task<LocalControlDiscoveryResult> ObserveAsync(
        LocalVisionSceneRequest request,
        ReadOnlyMemory<byte> pngBytes,
        CancellationToken cancellationToken = default) =>
        lazy.Value.Provider.ObserveAsync(request, pngBytes, cancellationToken);

    public void Dispose()
    {
        if (lazy.IsValueCreated) lazy.Value.Client.Dispose();
    }

    private static (FoundryLocalVisionClient Client, FoundryLocalControlDiscoveryProvider Provider) Create(
        Func<FoundryLocalRuntime> resolveRuntime)
    {
        var runtime = resolveRuntime();
        var client = new FoundryLocalVisionClient(
            runtime.Endpoint,
            runtime.ModelId,
            TimeSpan.FromSeconds(30));
        return (client, new FoundryLocalControlDiscoveryProvider(client));
    }
}
