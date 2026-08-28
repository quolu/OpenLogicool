using System.Text.Json;
using OpenLogicool.Contracts.Exploration;

namespace OpenLogicool.Host;

public sealed record CodexGameObservedAction(
    string ActionId,
    string Label,
    string Operation,
    IReadOnlyList<double> NormalizedBounds);

public sealed record CodexGameObservation(
    string ObservationId,
    string ImageDataUrl,
    IReadOnlyList<string> Texts,
    IReadOnlyList<CodexGameObservedAction> Actions);

public sealed record CodexGameActionCommand(
    string Operation,
    string Label,
    IReadOnlyList<double>? NormalizedBounds = null,
    int? VerticalScrollSteps = null,
    IReadOnlyList<string>? KeyTokens = null,
    StructureScreenEdge? SavedEdge = null);

public sealed record CodexGameActionOutcome(
    string Status,
    GameTransitionJudgement? Judgement,
    string? CommittedEdgeId,
    string Detail);

public interface ICodexGameToolRuntime
{
    ValueTask<CodexGameObservation> ObserveAsync(CancellationToken cancellationToken = default);
    ValueTask<CodexGameActionOutcome> ExecuteAsync(
        CodexGameActionCommand command,
        bool repairing,
        CancellationToken cancellationToken = default);
}

public interface ICodexRouteRecorder
{
    StructureScreenEdge? NextSavedEdge { get; }
    int StepNumber { get; }
    long RevisionNumber { get; }
    bool Repairing { get; }
    void Record(CodexGameActionOutcome outcome, bool usedSavedEdge);
    void Complete(IReadOnlyList<string> facts);
}

public sealed class CodexGameDynamicTools(
    ICodexGameToolRuntime runtime,
    ICodexRouteRecorder route) : ICodexDynamicToolHandler
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private string? currentObservationId;
    private readonly List<string> toolErrors = [];

    public IReadOnlyList<string> FinalFacts { get; private set; } = [];
    public string FinalSummary { get; private set; } = string.Empty;
    public bool IsCompleted { get; private set; }
    public int ActionCallCount { get; private set; }
    public IReadOnlyList<string> ToolErrors => toolErrors;
    public bool IsReplayableCompletion => ActionCallCount == 0 || route.RevisionNumber > 0;

    public IReadOnlyList<CodexDynamicToolDefinition> Definitions { get; } =
    [
        Tool("observe", "Capture the current game page. Returns OCR text, the next saved route action, and a screenshot.",
            "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"),
        Tool("use_saved_action", "Execute the next saved route action. Always use this before proposing a new action when present.",
            "{\"type\":\"object\",\"properties\":{\"observationId\":{\"type\":\"string\"},\"edgeId\":{\"type\":\"string\"}},\"required\":[\"observationId\",\"edgeId\"],\"additionalProperties\":false}"),
        Tool("click", "Click one in-game control by normalized full-window point.",
            "{\"type\":\"object\",\"properties\":{\"observationId\":{\"type\":\"string\"},\"label\":{\"type\":\"string\"},\"x\":{\"type\":\"number\",\"minimum\":0,\"maximum\":1},\"y\":{\"type\":\"number\",\"minimum\":0,\"maximum\":1}},\"required\":[\"observationId\",\"label\",\"x\",\"y\"],\"additionalProperties\":false}"),
        Tool("scroll", "Scroll one visible information region. Use negative verticalSteps to read downward.",
            "{\"type\":\"object\",\"properties\":{\"observationId\":{\"type\":\"string\"},\"label\":{\"type\":\"string\"},\"x\":{\"type\":\"number\",\"minimum\":0,\"maximum\":1},\"y\":{\"type\":\"number\",\"minimum\":0,\"maximum\":1},\"verticalSteps\":{\"type\":\"integer\",\"minimum\":-8,\"maximum\":8}},\"required\":[\"observationId\",\"label\",\"x\",\"y\",\"verticalSteps\"],\"additionalProperties\":false}"),
        Tool("back", "Send the in-game back action (Escape) after an unrelated page or blocking screen.",
            "{\"type\":\"object\",\"properties\":{\"observationId\":{\"type\":\"string\"}},\"required\":[\"observationId\"],\"additionalProperties\":false}"),
        Tool("wait", "Wait briefly without game input, then observe again.",
            "{\"type\":\"object\",\"properties\":{\"milliseconds\":{\"type\":\"integer\",\"minimum\":100,\"maximum\":3000}},\"required\":[\"milliseconds\"],\"additionalProperties\":false}"),
        Tool("finish", "Finish only after the user goal is complete. Return all collected facts.",
            "{\"type\":\"object\",\"properties\":{\"summary\":{\"type\":\"string\"},\"facts\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}},\"required\":[\"summary\",\"facts\"],\"additionalProperties\":false}"),
    ];

    public async ValueTask<CodexDynamicToolOutput> ExecuteAsync(
        string tool,
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return tool switch
            {
                "observe" => await ObserveAsync(cancellationToken).ConfigureAwait(false),
                "use_saved_action" => await UseSavedAsync(arguments, cancellationToken).ConfigureAwait(false),
                "click" => await ClickAsync(arguments, cancellationToken).ConfigureAwait(false),
                "scroll" => await ScrollAsync(arguments, cancellationToken).ConfigureAwait(false),
                "back" => await BackAsync(arguments, cancellationToken).ConfigureAwait(false),
                "wait" => await WaitAsync(arguments, cancellationToken).ConfigureAwait(false),
                "finish" => Finish(arguments),
                _ => new CodexDynamicToolOutput(false, JsonSerializer.Serialize(new { error = "unknown tool" }, Json)),
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            toolErrors.Add($"{tool}: {exception}");
            return new CodexDynamicToolOutput(false, JsonSerializer.Serialize(new { error = exception.Message }, Json));
        }
    }

    private async ValueTask<CodexDynamicToolOutput> ObserveAsync(CancellationToken cancellationToken)
    {
        var observation = await runtime.ObserveAsync(cancellationToken).ConfigureAwait(false);
        currentObservationId = observation.ObservationId;
        var saved = route.Repairing ? null : route.NextSavedEdge;
        var text = JsonSerializer.Serialize(new
        {
            observation.ObservationId,
            observation.Texts,
            SavedAction = saved is null ? null : new
            {
                saved.EdgeId,
                Label = saved.TargetSemanticKey ?? saved.AffordanceCandidateId,
                Operation = saved.Primitive,
            },
            KnownActions = observation.Actions,
            RouteRevision = route.RevisionNumber,
            route.Repairing,
        }, Json);
        return new CodexDynamicToolOutput(true, text, observation.ImageDataUrl);
    }

    private async ValueTask<CodexDynamicToolOutput> UseSavedAsync(
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        RequireObservation(arguments);
        var edgeId = arguments.GetProperty("edgeId").GetString();
        var edge = route.NextSavedEdge;
        if (edge is null || edge.EdgeId != edgeId)
            throw new InvalidOperationException("指定edgeは現在stepの保存actionではありません。");
        return await ActAsync(new CodexGameActionCommand(
            edge.Primitive,
            edge.TargetSemanticKey ?? edge.AffordanceCandidateId,
            edge.TargetNormalizedBounds,
            edge.VerticalScrollSteps,
            edge.KeyTokens,
            edge), usedSaved: true, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<CodexDynamicToolOutput> ClickAsync(
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        RequireObservation(arguments);
        var x = Unit(arguments, "x");
        var y = Unit(arguments, "y");
        return await ActAsync(new CodexGameActionCommand(
            GameInteractionOperations.Click,
            RequiredText(arguments, "label"),
            PointBounds(x, y)), false, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<CodexDynamicToolOutput> ScrollAsync(
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        RequireObservation(arguments);
        var steps = arguments.GetProperty("verticalSteps").GetInt32();
        if (steps is < -8 or > 8 || steps == 0)
            throw new ArgumentOutOfRangeException(nameof(steps));
        return await ActAsync(new CodexGameActionCommand(
            GameInteractionOperations.Scroll,
            RequiredText(arguments, "label"),
            PointBounds(Unit(arguments, "x"), Unit(arguments, "y")),
            steps), false, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<CodexDynamicToolOutput> BackAsync(
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        RequireObservation(arguments);
        return await ActAsync(new CodexGameActionCommand(
            GameInteractionOperations.KeyTap,
            "Back",
            KeyTokens: ["Key:Esc"]), false, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<CodexDynamicToolOutput> WaitAsync(
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var milliseconds = arguments.GetProperty("milliseconds").GetInt32();
        if (milliseconds is < 100 or > 3_000) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        await Task.Delay(milliseconds, cancellationToken).ConfigureAwait(false);
        return new CodexDynamicToolOutput(true, JsonSerializer.Serialize(new { waitedMilliseconds = milliseconds }, Json));
    }

    private CodexDynamicToolOutput Finish(JsonElement arguments)
    {
        var summary = RequiredText(arguments, "summary");
        var facts = arguments.GetProperty("facts").EnumerateArray()
            .Select(value => value.GetString()?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        route.Complete(facts);
        FinalSummary = summary;
        FinalFacts = facts;
        IsCompleted = true;
        return new CodexDynamicToolOutput(true, JsonSerializer.Serialize(new { completed = true, summary, facts }, Json));
    }

    private async ValueTask<CodexDynamicToolOutput> ActAsync(
        CodexGameActionCommand command,
        bool usedSaved,
        CancellationToken cancellationToken)
    {
        ActionCallCount++;
        var outcome = await runtime.ExecuteAsync(command, route.Repairing, cancellationToken).ConfigureAwait(false);
        route.Record(outcome, usedSaved);
        currentObservationId = null;
        return new CodexDynamicToolOutput(true, JsonSerializer.Serialize(new
        {
            outcome.Status,
            Judgement = outcome.Judgement?.ToString(),
            outcome.CommittedEdgeId,
            outcome.Detail,
            RouteRevision = route.RevisionNumber,
            route.Repairing,
            ObservationRequired = true,
        }, Json));
    }

    private void RequireObservation(JsonElement arguments)
    {
        var supplied = arguments.GetProperty("observationId").GetString();
        if (currentObservationId is null || supplied != currentObservationId)
            throw new InvalidOperationException("actionは直前のobserve結果へ束縛されていません。");
    }

    private static string RequiredText(JsonElement arguments, string property)
    {
        var value = arguments.GetProperty(property).GetString();
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{property}が必要です。") : value.Trim();
    }

    private static double Unit(JsonElement arguments, string property)
    {
        var value = arguments.GetProperty(property).GetDouble();
        return double.IsFinite(value) && value is >= 0 and <= 1
            ? value
            : throw new ArgumentOutOfRangeException(property);
    }

    private static IReadOnlyList<double> PointBounds(double x, double y) =>
        [Math.Clamp(x - 0.001, 0, 0.998), Math.Clamp(y - 0.001, 0, 0.998), 0.002, 0.002];

    private static CodexDynamicToolDefinition Tool(string name, string description, string schema)
    {
        using var document = JsonDocument.Parse(schema);
        return new CodexDynamicToolDefinition(name, description, document.RootElement.Clone());
    }
}
