using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace OpenLogicool.Host;

internal sealed record ControlParameter(string Name, string Type, bool Required, JsonNode Schema);
internal sealed record ControlOperation(string Id, string Contract, string Method, bool Available,
    string? UnavailableReason, IReadOnlyList<ControlParameter> Parameters, string ResultType);
internal sealed record ControlFault(string Code, string Message);
internal sealed record ControlJobSnapshot(string Id, string Operation, string State,
    JsonElement? Progress, JsonElement? Result, ControlFault? Error, bool CancellationRequested = false);
internal sealed record ControlDeferredResult(object? Value, Action AfterReply);

/// <summary>GUIと同じintentを、明示されたinterfaceの操作だけ公開する。</summary>
internal sealed class ControlOperationRegistry(Func<Func<object?>, Task<object?>> dispatch)
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(), Converters = { new JsonStringEnumConverter() }
    };
    private sealed record Entry(ControlOperation Description, Func<JsonElement, ControlJobContext, Task<object?>> Invoke);
    private readonly Dictionary<string, Entry> operations = new(StringComparer.Ordinal);
    private readonly HashSet<Type> contracts = [];
    private readonly NullabilityInfoContext nullability = new();

    public IReadOnlyList<ControlOperation> List() => operations.Values.Select(entry => entry.Description).OrderBy(item => item.Id).ToArray();
    public bool Covers(Type contract) => contracts.Contains(contract);

    public void Add<T>(string group, T? instance, string? unavailableReason = null, bool background = false) where T : class
    {
        var contract = typeof(T);
        if (!contract.IsInterface) throw new ArgumentException("公開操作にはinterfaceを指定します。");
        contracts.Add(contract);
        AddMethods(group, contract, instance, instance is not null, unavailableReason, background,
            contract.GetMethods().Where(method => !method.IsSpecialName));
    }

    public void AddStatic(string group, Type type) => AddMethods(group, type, null, true, null, false,
        type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));

    private void AddMethods(string group, Type contract, object? instance, bool available,
        string? unavailableReason, bool background, IEnumerable<MethodInfo> methods)
    {
        foreach (var method in methods)
        {
            var parameters = method.GetParameters().Where(parameter => !Injected(parameter.ParameterType)).Select(parameter =>
                new ControlParameter(parameter.Name!, parameter.ParameterType.Name, Required(parameter),
                    Json.GetJsonSchemaAsNode(parameter.ParameterType))).ToArray();
            var resultType = ResultType(method.ReturnType);
            var description = new ControlOperation(group + "." + Kebab(method.Name.EndsWith("Async", StringComparison.Ordinal)
                ? method.Name[..^5] : method.Name), contract.Name, method.Name, available, unavailableReason,
                parameters, resultType.Name);
            operations.Add(description.Id, new(description, async (arguments, context) =>
            {
                if (!available) throw new NotSupportedException(unavailableReason ?? "この起動モードでは利用できません。");
                var bound = Bind(method, arguments, context);
                object? Invoke() { context.Token.ThrowIfCancellationRequested(); return method.Invoke(instance, bound); }
                object? result = background ? await Task.Run(Invoke, context.Token) : await dispatch(Invoke);
                if (result is Task task)
                {
                    await task.ConfigureAwait(false);
                    return resultType == typeof(void) ? null : task.GetType().GetProperty("Result")!.GetValue(task);
                }
                return result;
            }));
        }
    }

    public void Wrap(string id, Func<Func<Task<object?>>, JsonElement, ControlJobContext, Task<object?>> wrapper)
    {
        var original = operations[id];
        operations[id] = original with { Invoke = (arguments, context) => wrapper(() => original.Invoke(arguments, context), arguments, context) };
    }

    public void Add(string id, Func<JsonElement, ControlJobContext, Task<object?>> action,
        IReadOnlyList<ControlParameter>? parameters = null, string resultType = "Object") =>
        operations.Add(id, new(new(id, "ApplicationControl", id, true, null, parameters ?? [], resultType), action));

    public async Task<object?> Invoke(string id, JsonElement arguments, ControlJobContext context)
    {
        if (!operations.TryGetValue(id, out var entry)) throw new KeyNotFoundException($"操作がありません: {id}");
        try { return await entry.Invoke(arguments, context); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }

    private object?[] Bind(MethodInfo method, JsonElement arguments, ControlJobContext context)
    {
        if (arguments.ValueKind != JsonValueKind.Object) throw new ArgumentException("操作引数はJSON objectです。");
        var parameters = method.GetParameters();
        var names = parameters.Where(parameter => !Injected(parameter.ParameterType)).Select(parameter => parameter.Name!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var property in arguments.EnumerateObject())
            if (!names.Contains(property.Name)) throw new ArgumentException($"未対応の引数です: {property.Name}");
        return parameters.Select(parameter =>
        {
            var type = parameter.ParameterType;
            if (type == typeof(CancellationToken)) return (object)context.Token;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IProgress<>))
                return Activator.CreateInstance(typeof(ControlProgress<>).MakeGenericType(type.GenericTypeArguments[0]), context)!;
            foreach (var property in arguments.EnumerateObject())
                if (string.Equals(property.Name, parameter.Name, StringComparison.OrdinalIgnoreCase))
                    return property.Value.Deserialize(type, Json);
            if (parameter.HasDefaultValue) return parameter.DefaultValue;
            if (!Required(parameter)) return null;
            throw new ArgumentException($"引数が必要です: {parameter.Name}");
        }).ToArray();
    }

    private bool Required(ParameterInfo parameter) => !parameter.HasDefaultValue
        && Nullable.GetUnderlyingType(parameter.ParameterType) is null
        && (parameter.ParameterType.IsValueType || nullability.Create(parameter).ReadState != NullabilityState.Nullable);
    private static bool Injected(Type type) => type == typeof(CancellationToken)
        || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IProgress<>);
    private static Type ResultType(Type type) => type == typeof(Task) ? typeof(void)
        : type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>) ? type.GenericTypeArguments[0] : type;
    internal static string Kebab(string value) => string.Concat(value.Select((c, index) => char.IsUpper(c)
        ? (index > 0 ? "-" : "") + char.ToLowerInvariant(c) : c.ToString()));
    private sealed class ControlProgress<T>(ControlJobContext context) : IProgress<T>
    { public void Report(T value) => context.Report(value); }
}

internal sealed class ControlJobContext(string id, CancellationToken token, Action<object?> progress)
{
    public string Id => id;
    public CancellationToken Token => token;
    public void Report(object? value) => progress(value);
}

internal sealed class ControlJobs(ControlOperationRegistry registry)
{
    private sealed class Job(string id, string operation)
    {
        public readonly Lock Gate = new();
        public readonly CancellationTokenSource Stop = new();
        public ControlJobSnapshot Snapshot = new(id, operation, "queued", null, null, null);
        public Task Worker = Task.CompletedTask;
        public Action? AfterReply;
    }
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Job> jobs = new();
    private readonly Lock lifecycle = new();
    private bool closing;

    public ControlJobSnapshot Start(string operation, JsonElement arguments)
    {
        lock (lifecycle)
        {
        if (closing) throw new InvalidOperationException("アプリは終了処理中です。");
        if (!registry.List().Any(item => item.Id == operation)) throw new KeyNotFoundException($"操作がありません: {operation}");
        var id = Guid.NewGuid().ToString("N");
        var job = new Job(id, operation);
        jobs[id] = job;
        job.Worker = Task.Run(async () =>
        {
            try
            {
                lock (job.Gate) job.Snapshot = job.Snapshot with { State = "running" };
                var context = new ControlJobContext(id, job.Stop.Token, value =>
                { lock (job.Gate) job.Snapshot = job.Snapshot with { Progress = JsonSerializer.SerializeToElement(value, ControlOperationRegistry.Json) }; });
                var result = await registry.Invoke(operation, arguments, context);
                if (result is ControlDeferredResult deferred) { job.AfterReply = deferred.AfterReply; result = deferred.Value; }
                lock (job.Gate) job.Snapshot = job.Snapshot with
                { State = "completed", Result = JsonSerializer.SerializeToElement(result, ControlOperationRegistry.Json) };
            }
            catch (OperationCanceledException) when (job.Stop.IsCancellationRequested)
            { lock (job.Gate) job.Snapshot = job.Snapshot with { State = "cancelled" }; }
            catch (Exception error)
            {
                lock (job.Gate) job.Snapshot = job.Snapshot with { State = "faulted",
                    Error = new(error is ArgumentException or JsonException ? "invalid-argument" : error is NotSupportedException ? "unavailable" : "execution-failed", error.Message) };
            }
        });
        return Get(id);
        }
    }

    public ControlJobSnapshot Get(string id)
    { var job = Find(id); lock (job.Gate) return job.Snapshot with { CancellationRequested = job.Stop.IsCancellationRequested }; }
    public IReadOnlyList<ControlJobSnapshot> List() => jobs.Keys.Select(Get).ToArray();
    public ControlJobSnapshot Cancel(string id)
    { var job = Find(id); if (Get(id).State is "queued" or "running") job.Stop.Cancel(); return Get(id); }
    private Job Find(string id) => jobs.TryGetValue(id, out var job) ? job : throw new KeyNotFoundException($"実行がありません: {id}");
    public void Replied(ControlJobSnapshot delivered)
    {
        var job = Find(delivered.Id);
        if (delivered.State == "completed") Interlocked.Exchange(ref job.AfterReply, null)?.Invoke();
    }
    public async Task StopAsync()
    {
        Job[] active;
        lock (lifecycle) { closing = true; active = jobs.Values.ToArray(); }
        foreach (var job in active) job.Stop.Cancel();
        await Task.WhenAll(active.Select(job => job.Worker));
    }
}
