using OpenLogicool.AI;
using OpenLogicool.Contracts.AI;

namespace OpenLogicool.Host;

/// <summary>AI探索が実際に必要になった時だけWindows上のFoundry Localを解決するvendor adapter。</summary>
public sealed class WindowsLazyFoundryControlDiscoveryProvider : ILocalControlDiscoveryProvider, IDisposable
{
    private readonly Lazy<(FoundryLocalRuntime Runtime, FoundryLocalVisionClient Client, FoundryLocalControlDiscoveryProvider Provider)> lazy;
    private readonly Action<string> loadModel;
    private readonly Action<string> unloadModel;
    private readonly SemaphoreSlim execution = new(1, 1);
    private bool requiresReload;

    public WindowsLazyFoundryControlDiscoveryProvider(
        Func<FoundryLocalRuntime> resolveRuntime,
        Action<string>? loadModel = null,
        Action<string>? unloadModel = null)
    {
        this.loadModel = loadModel ?? WindowsFoundryLocalRuntimeResolver.LoadModel;
        this.unloadModel = unloadModel ?? WindowsFoundryLocalRuntimeResolver.UnloadModel;
        lazy = new(() => Create(resolveRuntime), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public bool IsResolved => lazy.IsValueCreated;

    public async Task<LocalControlDiscoveryResult> ObserveAsync(
        LocalVisionSceneRequest request,
        ReadOnlyMemory<byte> pngBytes,
        CancellationToken cancellationToken = default)
    {
        await execution.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var resolved = lazy.Value;
            if (requiresReload) loadModel(resolved.Runtime.ModelId);
            try
            {
                return await resolved.Provider
                    .ObserveAsync(request, pngBytes, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                unloadModel(resolved.Runtime.ModelId);
                requiresReload = true;
            }
        }
        finally
        {
            execution.Release();
        }
    }

    public void Dispose()
    {
        if (lazy.IsValueCreated) lazy.Value.Client.Dispose();
        execution.Dispose();
    }

    private static (FoundryLocalRuntime Runtime, FoundryLocalVisionClient Client, FoundryLocalControlDiscoveryProvider Provider) Create(
        Func<FoundryLocalRuntime> resolveRuntime)
    {
        var runtime = resolveRuntime();
        var client = new FoundryLocalVisionClient(
            runtime.Endpoint,
            runtime.ModelId,
            TimeSpan.FromSeconds(30));
        return (runtime, client, new FoundryLocalControlDiscoveryProvider(client));
    }
}
