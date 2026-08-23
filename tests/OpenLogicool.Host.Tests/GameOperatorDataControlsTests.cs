using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class GameOperatorDataControlsTests
{
    [Fact]
    public void Default_controls_are_local_only_with_zero_external_api_cost()
    {
        var status = GameOperatorDataControls.Inspect(GameOperatorDataControlSettings.Default);

        Assert.False(status.SavesScreenImages);
        Assert.True(status.ProcessesAiLocally);
        Assert.False(status.ExternalAiTransmissionAvailable);
        Assert.False(status.HasSelectedLocalModel);
        Assert.Equal(0m, status.ExternalApiCostUsd);
        Assert.Contains(GameOperatorDataKind.ScreenImage, status.DefaultDiagnosticBundleExcludedKinds);
        Assert.Contains(GameOperatorDataKind.Secret, status.DefaultDiagnosticBundleExcludedKinds);

        Assert.Equal(
            GameOperatorDataControlReason.LocalImageSavingDisabled,
            GameOperatorDataControls.AuthorizeLocalImageSave(GameOperatorDataControlSettings.Default).Reason);
    }

    [Fact]
    public void Local_provider_and_model_are_selected_as_one_pair()
    {
        var settings = GameOperatorDataControlSettings.Default with
        {
            SaveScreenImages = true,
            LocalProviderId = "local-runtime",
            LocalModelId = "local-model",
        };

        var status = GameOperatorDataControls.Inspect(settings);
        Assert.True(status.HasSelectedLocalModel);
        Assert.False(status.ExternalAiTransmissionAvailable);
        Assert.Equal(0m, status.ExternalApiCostUsd);
        Assert.True(GameOperatorDataControls.AuthorizeLocalImageSave(settings).IsAllowed);
        Assert.Throws<ArgumentException>(() => GameOperatorDataControls.Inspect(
            settings with { LocalModelId = null }));
    }

    [Fact]
    public void Deletion_is_available_for_a_stored_image_without_recreating_storage_or_diagnostic_bundle()
    {
        Assert.True(GameOperatorDataControls.AuthorizeDeletion(hasStoredImage: true).IsAllowed);
        Assert.Equal(
            GameOperatorDataControlReason.NothingStored,
            GameOperatorDataControls.AuthorizeDeletion(hasStoredImage: false).Reason);
    }
}
