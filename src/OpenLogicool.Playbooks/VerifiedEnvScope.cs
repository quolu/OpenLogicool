namespace OpenLogicool.Playbooks;

/// <summary>
/// Verified の根拠を得た環境。別環境へは昇格させない。
/// </summary>
public sealed record VerifiedEnvScope
{
    public VerifiedEnvScope(string environmentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        EnvironmentId = environmentId;
    }

    public string EnvironmentId { get; }

    public bool AppliesTo(string environmentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        return string.Equals(EnvironmentId, environmentId, StringComparison.Ordinal);
    }
}
