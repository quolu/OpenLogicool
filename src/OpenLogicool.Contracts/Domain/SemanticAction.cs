namespace OpenLogicool.Contracts.Domain;

public enum RiskClass
{
    Low,
    Medium,
    High,
}

public sealed record SemanticAction(
    string SchemaVersion,
    string ActionId,
    string Name,
    RiskClass RiskClass,
    string ParameterSchema);
