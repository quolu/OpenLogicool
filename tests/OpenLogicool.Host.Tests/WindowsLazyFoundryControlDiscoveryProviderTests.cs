using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class WindowsLazyFoundryControlDiscoveryProviderTests
{
    [Fact]
    public void Loaded_multimodal_detection_distinguishes_empty_and_speech_only_lists()
    {
        Assert.False(WindowsFoundryLocalRuntimeResolver.HasLoadedMultimodalModel("{\"models\":[]}"));
        Assert.False(WindowsFoundryLocalRuntimeResolver.HasLoadedMultimodalModel(
            "{\"models\":[{\"type\":\"Speech\"}]}"));
        Assert.True(WindowsFoundryLocalRuntimeResolver.HasLoadedMultimodalModel(
            "{\"models\":[{\"type\":\"Multimodal\"}]}"));
    }

    [Fact]
    public void ConstructionAndUnusedDisposalDoNotResolveFoundry()
    {
        var resolveCount = 0;

        using (var provider = new WindowsLazyFoundryControlDiscoveryProvider(() =>
               {
                   resolveCount++;
                   return new FoundryLocalRuntime(new Uri("http://127.0.0.1:1"), "unused");
               }))
        {
            Assert.False(provider.IsResolved);
        }

        Assert.Equal(0, resolveCount);
    }
}
