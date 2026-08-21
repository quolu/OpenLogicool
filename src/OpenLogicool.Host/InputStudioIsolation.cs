namespace OpenLogicool.Host;

/// <summary>
/// Game Operator 側の外部依存が失敗した時に、Input Studio と混同しないための障害境界。
/// この型は状態を分類するだけで、fast path、watchdog、dispatch、設定保存を呼び出さない。
/// </summary>
public static class InputStudioIsolation
{
    private static readonly InputStudioOperation[] PreservedOperations =
    [
        InputStudioOperation.EditMappings,
        InputStudioOperation.SaveProfiles,
        InputStudioOperation.RunMappings,
    ];

    /// <summary>
    /// AI、network、capture のいずれかが fault しても、Input Studio の編集・保存・入力経路は
    /// Game Operator の障害から独立して利用可能であることを表す。
    /// </summary>
    public static InputStudioIsolationStatus Assess(IEnumerable<GameOperatorDependency> failedDependencies)
    {
        ArgumentNullException.ThrowIfNull(failedDependencies);

        var failures = new HashSet<GameOperatorDependency>();
        foreach (var dependency in failedDependencies)
        {
            if (!Enum.IsDefined(dependency))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(failedDependencies),
                    dependency,
                    "Input Studio 隔離の対象外の dependency です。");
            }

            failures.Add(dependency);
        }

        return new InputStudioIsolationStatus(
            PreservedOperations,
            failures.OrderBy(dependency => dependency).ToArray());
    }
}

/// <summary>Input Studio が保持する既存の操作面。</summary>
public enum InputStudioOperation
{
    EditMappings,
    SaveProfiles,
    RunMappings,
}

/// <summary>Game Operator に限って隔離する外部依存。</summary>
public enum GameOperatorDependency
{
    Ai,
    Network,
    Capture,
}

/// <summary>
/// 障害時の公開状態。Game Operator が利用可能とは主張せず、失敗 dependency を明示する。
/// </summary>
public sealed class InputStudioIsolationStatus
{
    private readonly HashSet<InputStudioOperation> _availableOperations;

    internal InputStudioIsolationStatus(
        IEnumerable<InputStudioOperation> availableOperations,
        IReadOnlyList<GameOperatorDependency> failedDependencies)
    {
        _availableOperations = availableOperations.ToHashSet();
        FailedDependencies = failedDependencies;
    }

    /// <summary>隔離対象の依存が一つでも fault している場合だけ true。</summary>
    public bool IsGameOperatorDegraded => FailedDependencies.Count > 0;

    /// <summary>障害と無関係に利用可能な Input Studio 操作。</summary>
    public IReadOnlyList<InputStudioOperation> AvailableOperations =>
        _availableOperations.OrderBy(operation => operation).ToArray();

    /// <summary>利用不可を成功扱いにしないため、fault した dependency をそのまま公開する。</summary>
    public IReadOnlyList<GameOperatorDependency> FailedDependencies { get; }

    public bool CanUse(InputStudioOperation operation) => _availableOperations.Contains(operation);
}
