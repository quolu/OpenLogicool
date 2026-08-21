using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class GameOperatorDataControlsTests
{
    [Fact]
    public void Default_controls_keep_images_cloud_provider_and_cost_off_while_excluding_screen_and_secret_from_diagnostic_bundle()
    {
        var status = GameOperatorDataControls.Inspect(GameOperatorDataControlSettings.Default);

        Assert.False(status.SavesScreenImages);
        Assert.False(status.CloudEvidenceCropTransmissionEnabled);
        Assert.False(status.HasSelectedProvider);
        Assert.Equal(0m, status.CostLimitUsd);
        Assert.Contains(GameOperatorDataKind.ScreenImage, status.DefaultDiagnosticBundleExcludedKinds);
        Assert.Contains(GameOperatorDataKind.Secret, status.DefaultDiagnosticBundleExcludedKinds);

        Assert.Equal(
            GameOperatorDataControlReason.LocalImageSavingDisabled,
            GameOperatorDataControls.AuthorizeLocalImageSave(GameOperatorDataControlSettings.Default).Reason);
        Assert.Equal(
            GameOperatorDataControlReason.CloudTransmissionDisabled,
            GameOperatorDataControls.AuthorizeCloudTransmission(
                GameOperatorDataControlSettings.Default,
                GameOperatorDataKind.EvidenceCrop,
                alreadySpentUsd: 0m,
                estimatedCostUsd: 0m).Reason);
    }

    [Fact]
    public void Cloud_does_not_start_when_provider_is_unselected_even_after_user_enables_its_control()
    {
        var settings = GameOperatorDataControlSettings.Default with
        {
            SaveScreenImages = true,
            AllowCloudEvidenceCropTransmission = true,
            CostLimitUsd = 5m,
        };

        var decision = GameOperatorDataControls.AuthorizeCloudTransmission(
            settings,
            GameOperatorDataKind.EvidenceCrop,
            alreadySpentUsd: 0m,
            estimatedCostUsd: .10m);

        Assert.False(decision.IsAllowed);
        Assert.Equal(GameOperatorDataControlReason.ProviderUnselected, decision.Reason);
        Assert.True(GameOperatorDataControls.AuthorizeLocalImageSave(settings).IsAllowed);
    }

    [Fact]
    public void Explicit_evidence_crop_is_limited_by_user_cost_budget_and_other_data_is_not_cloud_eligible()
    {
        var settings = new GameOperatorDataControlSettings(
            SaveScreenImages: true,
            AllowCloudEvidenceCropTransmission: true,
            ProviderId: "provider-configured-by-user",
            CostLimitUsd: 1m);

        var accepted = GameOperatorDataControls.AuthorizeCloudTransmission(
            settings,
            GameOperatorDataKind.EvidenceCrop,
            alreadySpentUsd: .75m,
            estimatedCostUsd: .25m);
        var overBudget = GameOperatorDataControls.AuthorizeCloudTransmission(
            settings,
            GameOperatorDataKind.EvidenceCrop,
            alreadySpentUsd: .75m,
            estimatedCostUsd: .26m);
        var screen = GameOperatorDataControls.AuthorizeCloudTransmission(
            settings,
            GameOperatorDataKind.ScreenImage,
            alreadySpentUsd: 0m,
            estimatedCostUsd: 0m);
        var secret = GameOperatorDataControls.AuthorizeCloudTransmission(
            settings,
            GameOperatorDataKind.Secret,
            alreadySpentUsd: 0m,
            estimatedCostUsd: 0m);

        Assert.True(accepted.IsAllowed);
        Assert.Equal(GameOperatorDataControlReason.CostLimitExceeded, overBudget.Reason);
        Assert.Equal(GameOperatorDataControlReason.DataKindNotEligibleForCloud, screen.Reason);
        Assert.Equal(GameOperatorDataControlReason.DataKindNotEligibleForCloud, secret.Reason);
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
