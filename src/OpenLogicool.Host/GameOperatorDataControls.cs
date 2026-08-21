namespace OpenLogicool.Host;

/// <summary>Game Operator が扱いうるデータの種類。</summary>
public enum GameOperatorDataKind
{
    ScreenImage,
    EvidenceCrop,
    JournalContent,
    Prompt,
    Secret,
}

/// <summary>データ操作を許可または拒否した理由。</summary>
public enum GameOperatorDataControlReason
{
    Allowed,
    LocalImageSavingDisabled,
    CloudTransmissionDisabled,
    ProviderUnselected,
    DataKindNotEligibleForCloud,
    CostLimitExceeded,
    NothingStored,
}

/// <summary>データ操作の可否。実際の保存・送信・削除はこの型の責務ではない。</summary>
public sealed record GameOperatorDataControlDecision(bool IsAllowed, GameOperatorDataControlReason Reason);

/// <summary>
/// 利用者が確認・変更できる Game Operator のデータ制御設定。
/// providerId は選定済み provider の識別子を表すだけで、この型は provider を選定せず、network に到達しない。
/// </summary>
public sealed record GameOperatorDataControlSettings(
    bool SaveScreenImages,
    bool AllowCloudEvidenceCropTransmission,
    string? ProviderId,
    decimal CostLimitUsd)
{
    public static GameOperatorDataControlSettings Default { get; } = new(
        SaveScreenImages: false,
        AllowCloudEvidenceCropTransmission: false,
        ProviderId: null,
        CostLimitUsd: 0m);
}

/// <summary>利用者へ表示する現在のデータ制御状態。</summary>
public sealed record GameOperatorDataControlStatus(
    bool SavesScreenImages,
    bool CloudEvidenceCropTransmissionEnabled,
    bool HasSelectedProvider,
    decimal CostLimitUsd,
    IReadOnlyList<GameOperatorDataKind> DefaultDiagnosticBundleExcludedKinds);

/// <summary>
/// Game Operator の画像保存、cloud 送信、削除、provider、cost の pure な制御口。
/// DiagnosticBundle の生成・削除や provider 通信を再実装せず、呼び出し側が副作用の前にこの decision を使う。
/// </summary>
public static class GameOperatorDataControls
{
    private static readonly IReadOnlyList<GameOperatorDataKind> DefaultDiagnosticBundleExcludedKinds =
    [
        GameOperatorDataKind.ScreenImage,
        GameOperatorDataKind.Secret,
    ];

    public static GameOperatorDataControlStatus Inspect(GameOperatorDataControlSettings settings)
    {
        Validate(settings);
        return new(
            settings.SaveScreenImages,
            settings.AllowCloudEvidenceCropTransmission,
            HasSelectedProvider(settings),
            settings.CostLimitUsd,
            DefaultDiagnosticBundleExcludedKinds);
    }

    public static GameOperatorDataControlDecision AuthorizeLocalImageSave(GameOperatorDataControlSettings settings)
    {
        Validate(settings);
        return settings.SaveScreenImages
            ? Allow()
            : Deny(GameOperatorDataControlReason.LocalImageSavingDisabled);
    }

    public static GameOperatorDataControlDecision AuthorizeCloudTransmission(
        GameOperatorDataControlSettings settings,
        GameOperatorDataKind kind,
        decimal alreadySpentUsd,
        decimal estimatedCostUsd)
    {
        Validate(settings);
        ValidateCost(alreadySpentUsd, nameof(alreadySpentUsd));
        ValidateCost(estimatedCostUsd, nameof(estimatedCostUsd));

        if (!settings.AllowCloudEvidenceCropTransmission)
        {
            return Deny(GameOperatorDataControlReason.CloudTransmissionDisabled);
        }

        if (!HasSelectedProvider(settings))
        {
            return Deny(GameOperatorDataControlReason.ProviderUnselected);
        }

        // Full-screen images and secrets are never a default cloud payload. The only explicit cloud
        // data surface presently offered is a user-selected Teach evidence crop.
        if (kind != GameOperatorDataKind.EvidenceCrop)
        {
            return Deny(GameOperatorDataControlReason.DataKindNotEligibleForCloud);
        }

        return alreadySpentUsd + estimatedCostUsd <= settings.CostLimitUsd
            ? Allow()
            : Deny(GameOperatorDataControlReason.CostLimitExceeded);
    }

    public static GameOperatorDataControlDecision AuthorizeDeletion(bool hasStoredImage) =>
        hasStoredImage ? Allow() : Deny(GameOperatorDataControlReason.NothingStored);

    private static bool HasSelectedProvider(GameOperatorDataControlSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.ProviderId);

    private static GameOperatorDataControlDecision Allow() => new(true, GameOperatorDataControlReason.Allowed);

    private static GameOperatorDataControlDecision Deny(GameOperatorDataControlReason reason) => new(false, reason);

    private static void Validate(GameOperatorDataControlSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidateCost(settings.CostLimitUsd, nameof(settings.CostLimitUsd));
    }

    private static void ValidateCost(decimal amount, string parameterName)
    {
        if (amount < 0m)
        {
            throw new ArgumentOutOfRangeException(parameterName, "cost は負にできません。");
        }
    }
}
