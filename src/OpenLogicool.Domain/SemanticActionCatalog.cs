using OpenLogicool.Contracts.Domain;

namespace OpenLogicool.Domain;

public sealed class SemanticActionCatalog
{
    private readonly IReadOnlyDictionary<string, SemanticAction> _actions;

    public SemanticActionCatalog(IEnumerable<SemanticAction> actions)
    {
        var registeredActions = new Dictionary<string, SemanticAction>(StringComparer.Ordinal);

        foreach (var action in actions)
        {
            if (!registeredActions.TryAdd(action.ActionId, action))
            {
                throw new ArgumentException($"SemanticAction '{action.ActionId}' は既に登録されています。", nameof(actions));
            }
        }

        _actions = registeredActions;
    }

    public SemanticAction Get(string actionId) =>
        _actions.TryGetValue(actionId, out var action)
            ? action
            : throw new KeyNotFoundException($"SemanticAction '{actionId}' は登録されていません。");

    public SemanticActionCatalog With(SemanticAction action)
    {
        if (_actions.ContainsKey(action.ActionId))
        {
            throw new ArgumentException($"SemanticAction '{action.ActionId}' は既に登録されています。", nameof(action));
        }

        return new SemanticActionCatalog(_actions.Values.Append(action));
    }
}
