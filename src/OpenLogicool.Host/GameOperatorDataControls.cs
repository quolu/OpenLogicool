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
    NothingStored,
}

/// <summary>データ操作の可否。実際の保存・送信・削除はこの型の責務ではない。</summary>
public sealed record GameOperatorDataControlDecision(bool IsAllowed, GameOperatorDataControlReason Reason);

/// <summary>
/// 利用者が確認・変更できる Game Operator のデータ制御設定。
/// local provider／modelは選定済み識別子を表すだけで、この型はproviderを選定せずnetworkに到達しない。
/// </summary>
public sealed record GameOperatorDataControlSettings(
    bool SaveScreenImages,
    string? LocalProviderId,
    string? LocalModelId)
{
    public static GameOperatorDataControlSettings Default { get; } = new(
        SaveScreenImages: false,
        LocalProviderId: null,
        LocalModelId: null);
}

/// <summary>利用者へ表示する現在のデータ制御状態。</summary>
public sealed record GameOperatorDataControlStatus(
    bool SavesScreenImages,
    bool ProcessesAiLocally,
    bool ExternalAiTransmissionAvailable,
    bool HasSelectedLocalModel,
    decimal ExternalApiCostUsd,
    IReadOnlyList<GameOperatorDataKind> DefaultDiagnosticBundleExcludedKinds);

/// <summary>
/// Game Operatorの画像保存、削除、ローカルAI状態のpureな制御口。
/// 外部AI送信の許可口とAPI key設定は意図的に持たない。
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
            ProcessesAiLocally: true,
            ExternalAiTransmissionAvailable: false,
            HasSelectedLocalModel(settings),
            ExternalApiCostUsd: 0m,
            DefaultDiagnosticBundleExcludedKinds);
    }

    public static GameOperatorDataControlDecision AuthorizeLocalImageSave(GameOperatorDataControlSettings settings)
    {
        Validate(settings);
        return settings.SaveScreenImages
            ? Allow()
            : Deny(GameOperatorDataControlReason.LocalImageSavingDisabled);
    }

    public static GameOperatorDataControlDecision AuthorizeDeletion(bool hasStoredImage) =>
        hasStoredImage ? Allow() : Deny(GameOperatorDataControlReason.NothingStored);

    private static bool HasSelectedLocalModel(GameOperatorDataControlSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.LocalProviderId);

    private static GameOperatorDataControlDecision Allow() => new(true, GameOperatorDataControlReason.Allowed);

    private static GameOperatorDataControlDecision Deny(GameOperatorDataControlReason reason) => new(false, reason);

    private static void Validate(GameOperatorDataControlSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var hasProvider = !string.IsNullOrWhiteSpace(settings.LocalProviderId);
        var hasModel = !string.IsNullOrWhiteSpace(settings.LocalModelId);
        if (hasProvider != hasModel)
        {
            throw new ArgumentException("local providerとmodelは両方設定するか両方省略します。", nameof(settings));
        }
    }
}
