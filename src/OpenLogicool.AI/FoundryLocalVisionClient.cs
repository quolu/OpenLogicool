using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenLogicool.AI;

public enum FoundryVisionStatus
{
    Completed,
    Unknown,
}

public enum FoundryVisionFailure
{
    None,
    Timeout,
    Http,
    Provider,
    InvalidResponse,
}

[Flags]
public enum FoundryVisionNormalization
{
    None = 0,
    DuplicateLabelsCollapsed = 1,
    TruncatedRepetitionRecovered = 2,
    OutOfCandidateLabelsDropped = 4,
    OutputLimitApplied = 8,
}

public sealed record FoundryVisionResult(
    FoundryVisionStatus Status,
    FoundryVisionFailure Failure,
    string? FailureDetail,
    FoundryVisionNormalization Normalization,
    IReadOnlyList<string> Labels,
    string RawOutput,
    long ElapsedMs,
    int RequestBytes,
    int? InputTokens,
    int? OutputTokens);

public sealed record FoundryVisionControl(
    string Kind,
    string Label,
    double X,
    double Y,
    double Width,
    double Height);

public sealed record FoundryVisionControlsResult(
    FoundryVisionStatus Status,
    FoundryVisionFailure Failure,
    string? FailureDetail,
    IReadOnlyList<FoundryVisionControl> Controls,
    string RawOutput,
    long ElapsedMs,
    int RequestBytes,
    int? InputTokens,
    int? OutputTokens,
    FoundryVisionNormalization Normalization = FoundryVisionNormalization.None);

internal sealed record FoundryVisionRawResponse(
    FoundryVisionStatus Status,
    FoundryVisionFailure Failure,
    string? FailureDetail,
    string RawOutput,
    long ElapsedMs,
    int RequestBytes,
    int? InputTokens,
    int? OutputTokens);

public sealed class FoundryLocalVisionClient : IDisposable
{
    public const string PromptRevision = "clickable-visible-labels-v2";
    public const string ControlsPromptRevision = "clickable-controls-v2";

    private const string Prompt =
        "Read the image. Find visually clickable controls that contain visible words. " +
        "Copy each control's visible words exactly, preserving case. " +
        "Return a JSON object whose only property is named labels and whose value is an array of those copied strings. " +
        "Return at most 12 labels. " +
        "Each label must appear at most once. Never repeat a label. " +
        "If no such controls exist, return {\"labels\":[]}. " +
        "Never output the phrase 'visible text label'. " +
        "Do not output coordinates, descriptions, non-interactive text, or markdown.";

    private const string ControlsPrompt =
        "Inspect the image and find every visually clickable control, including controls with visible text and icon-only controls. " +
        "Return one JSON object whose only property is controls. controls must be an array. " +
        "Each item must have exactly kind, label, x, y, width, and height. " +
        "kind must be text or icon. label must copy visible control text, or briefly name the visible icon when no text exists. " +
        "x, y, width, and height must be numbers from 0 to 1, relative to the full image, and describe the clickable control bounds. " +
        "Return at most 20 controls. Do not repeat a control with the same label and overlapping bounds. " +
        "Do not include decorative images, characters, backgrounds, status text, or markdown. " +
        "Return compact single-line JSON with no explanatory whitespace. " +
        "If no clickable control exists, return {\"controls\":[]}.";

    private static readonly JsonSerializerOptions PromptJson = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    private const int MaximumReturnedLabels = 12;

    private readonly HttpClient http;
    private readonly Uri responsesEndpoint;
    private readonly string modelId;
    private readonly TimeSpan timeout;

    public FoundryLocalVisionClient(
        Uri daemonBaseUri,
        string modelId,
        TimeSpan timeout)
        : this(daemonBaseUri, modelId, timeout, CreateProductionHandler())
    {
    }

    internal FoundryLocalVisionClient(
        Uri daemonBaseUri,
        string modelId,
        TimeSpan timeout,
        HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(daemonBaseUri);
        if (!daemonBaseUri.IsAbsoluteUri
            || daemonBaseUri.Scheme != Uri.UriSchemeHttp
            || !IPAddress.TryParse(daemonBaseUri.Host, out var address)
            || !IPAddress.IsLoopback(address)
            || !string.IsNullOrEmpty(daemonBaseUri.UserInfo))
        {
            throw new ArgumentException(
                "Foundry Local endpointはIP literalのloopback HTTPだけを許可します。",
                nameof(daemonBaseUri));
        }

        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new ArgumentException("Foundry Local model IDが必要です。", nameof(modelId));
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        responsesEndpoint = new Uri(
            $"{daemonBaseUri.Scheme}://{daemonBaseUri.Authority}/v1/responses",
            UriKind.Absolute);
        this.modelId = modelId;
        this.timeout = timeout;
        http = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    public Uri Endpoint => responsesEndpoint;
    public string ModelId => modelId;
    public string PromptSha256 => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Prompt)));
    public string LabelsPromptSha256(IReadOnlyList<string> candidateLabels, string? targetIntent = null) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(BuildLabelsPrompt(candidateLabels, targetIntent))));
    public string ControlsPromptSha256 =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ControlsPrompt)));

    public async Task<FoundryVisionResult> ProposeLabelsAsync(
        ReadOnlyMemory<byte> pngBytes,
        CancellationToken cancellationToken = default) =>
        await ProposeLabelsAsync(pngBytes, [], null, cancellationToken).ConfigureAwait(false);

    public async Task<FoundryVisionResult> ProposeLabelsAsync(
        ReadOnlyMemory<byte> pngBytes,
        IReadOnlyList<string> candidateLabels,
        CancellationToken cancellationToken = default) =>
        await ProposeLabelsAsync(pngBytes, candidateLabels, null, cancellationToken).ConfigureAwait(false);

    public async Task<FoundryVisionResult> ProposeLabelsAsync(
        ReadOnlyMemory<byte> pngBytes,
        IReadOnlyList<string> candidateLabels,
        string? targetIntent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidateLabels);
        var candidates = candidateLabels
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var raw = await SendVisionAsync(
            BuildLabelsPrompt(candidates, targetIntent),
            500,
            pngBytes,
            cancellationToken).ConfigureAwait(false);
        if (raw.Status != FoundryVisionStatus.Completed)
        {
            return new FoundryVisionResult(
                raw.Status,
                raw.Failure,
                raw.FailureDetail,
                FoundryVisionNormalization.None,
                [],
                raw.RawOutput,
                raw.ElapsedMs,
                raw.RequestBytes,
                raw.InputTokens,
                raw.OutputTokens);
        }
        if (!TryParseLabels(
                raw.RawOutput,
                out var labels,
                out var normalization,
                out var validationError))
        {
            return new FoundryVisionResult(
                FoundryVisionStatus.Unknown,
                FoundryVisionFailure.InvalidResponse,
                validationError,
                FoundryVisionNormalization.None,
                [],
                raw.RawOutput,
                raw.ElapsedMs,
                raw.RequestBytes,
                raw.InputTokens,
                raw.OutputTokens);
        }
        if (candidates.Length > 0)
        {
            var constrained = labels
                .Where(label => candidates.Contains(label, StringComparer.Ordinal))
                .ToArray();
            if (constrained.Length == 0)
            {
                return new FoundryVisionResult(
                    FoundryVisionStatus.Unknown,
                    FoundryVisionFailure.InvalidResponse,
                    "vision responseに同一frame OCR候補と一致するlabelがありません。",
                    FoundryVisionNormalization.None,
                    [],
                    raw.RawOutput,
                    raw.ElapsedMs,
                    raw.RequestBytes,
                    raw.InputTokens,
                    raw.OutputTokens);
            }
            if (constrained.Length != labels.Count)
            {
                normalization |= FoundryVisionNormalization.OutOfCandidateLabelsDropped;
            }
            labels = constrained;
        }
        var maximumReturnedLabels = string.IsNullOrWhiteSpace(targetIntent) ? MaximumReturnedLabels : 1;
        if (labels.Count > maximumReturnedLabels)
        {
            labels = labels.Take(maximumReturnedLabels).ToArray();
            normalization |= FoundryVisionNormalization.OutputLimitApplied;
        }
        return new FoundryVisionResult(
            FoundryVisionStatus.Completed,
            FoundryVisionFailure.None,
            null,
            normalization,
            labels,
            raw.RawOutput,
            raw.ElapsedMs,
            raw.RequestBytes,
            raw.InputTokens,
            raw.OutputTokens);
    }

    private static string BuildLabelsPrompt(IReadOnlyList<string> candidateLabels, string? targetIntent) =>
        candidateLabels.Count == 0
            ? Prompt
            : Prompt
              + " The only permitted labels are these same-frame OCR strings: "
              + JsonSerializer.Serialize(candidateLabels, PromptJson)
              + ". Select clickable controls only from this array. Copy the selected array strings exactly; "
              + "never translate, transliterate, correct, combine, or invent a label."
              + (string.IsNullOrWhiteSpace(targetIntent)
                  ? string.Empty
                  : " The current goal is: " + targetIntent.Trim()
                    + ". Return exactly one label: the single control that most directly advances this goal. "
                    + "If none of the OCR strings is a suitable control, return {\"labels\":[]}.");

    public async Task<FoundryVisionControlsResult> ProposeControlsAsync(
        ReadOnlyMemory<byte> pngBytes,
        CancellationToken cancellationToken = default)
    {
        var raw = await SendVisionAsync(ControlsPrompt, 1_500, pngBytes, cancellationToken).ConfigureAwait(false);
        if (raw.Status != FoundryVisionStatus.Completed)
        {
            return ControlsFromRaw(raw, []);
        }
        if (!TryParseControls(raw.RawOutput, out var controls, out var normalization, out var error))
        {
            return new FoundryVisionControlsResult(
                FoundryVisionStatus.Unknown,
                FoundryVisionFailure.InvalidResponse,
                error,
                [],
                raw.RawOutput,
                raw.ElapsedMs,
                raw.RequestBytes,
                raw.InputTokens,
                raw.OutputTokens,
                FoundryVisionNormalization.None);
        }
        return ControlsFromRaw(raw, controls, normalization);
    }

    private async Task<FoundryVisionRawResponse> SendVisionAsync(
        string prompt,
        int maximumOutputTokens,
        ReadOnlyMemory<byte> pngBytes,
        CancellationToken cancellationToken)
    {
        if (pngBytes.IsEmpty)
        {
            throw new ArgumentException("vision input PNGが空です。", nameof(pngBytes));
        }
        var body = JsonSerializer.Serialize(new
        {
            model = modelId,
            max_output_tokens = maximumOutputTokens,
            stream = true,
            input = new object[]
            {
                new
                {
                    type = "message",
                    role = "user",
                    content = new object[]
                    {
                        new { type = "input_text", text = prompt },
                        new
                        {
                            type = "input_image",
                            image_data = Convert.ToBase64String(pngBytes.Span),
                            media_type = "image/png",
                        },
                    },
                },
            },
        });
        var requestBytes = Encoding.UTF8.GetByteCount(body);
        var started = Stopwatch.StartNew();
        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, responsesEndpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "notneeded");
            using var response = await http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                linkedCancellation.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return RawUnknown(
                    FoundryVisionFailure.Http,
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
                    started,
                    requestBytes);
            }
            await using var stream = await response.Content
                .ReadAsStreamAsync(linkedCancellation.Token)
                .ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            var output = new StringBuilder();
            string? providerFailure = null;
            var completed = false;
            int? inputTokens = null;
            int? outputTokens = null;
            while (await reader.ReadLineAsync(linkedCancellation.Token).ConfigureAwait(false) is { } line)
            {
                if (!line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    continue;
                }
                var data = line["data: ".Length..];
                if (data == "[DONE]")
                {
                    break;
                }
                using var document = JsonDocument.Parse(data);
                var root = document.RootElement;
                var eventType = root.TryGetProperty("type", out var typeElement)
                    ? typeElement.GetString()
                    : null;
                if (eventType == "response.output_text.delta"
                    && root.TryGetProperty("delta", out var deltaElement))
                {
                    output.Append(deltaElement.GetString());
                }
                else if (eventType == "response.failed")
                {
                    providerFailure = ReadProviderFailure(root);
                }
                else if (eventType == "response.completed"
                    && root.TryGetProperty("response", out var completedResponse))
                {
                    completed = true;
                    if (completedResponse.TryGetProperty("usage", out var usage))
                    {
                        inputTokens = ReadNullableInt(usage, "input_tokens");
                        outputTokens = ReadNullableInt(usage, "output_tokens");
                    }
                }
            }
            started.Stop();
            if (providerFailure is not null)
            {
                return new FoundryVisionRawResponse(
                    FoundryVisionStatus.Unknown,
                    FoundryVisionFailure.Provider,
                    providerFailure,
                    output.ToString(),
                    started.ElapsedMilliseconds,
                    requestBytes,
                    inputTokens,
                    outputTokens);
            }
            if (!completed)
            {
                return new FoundryVisionRawResponse(
                    FoundryVisionStatus.Unknown,
                    FoundryVisionFailure.Provider,
                    "Foundry Local streamがresponse.completedより前に終了しました。",
                    output.ToString(),
                    started.ElapsedMilliseconds,
                    requestBytes,
                    inputTokens,
                    outputTokens);
            }
            return new FoundryVisionRawResponse(
                FoundryVisionStatus.Completed,
                FoundryVisionFailure.None,
                null,
                output.ToString(),
                started.ElapsedMilliseconds,
                requestBytes,
                inputTokens,
                outputTokens);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return RawUnknown(
                FoundryVisionFailure.Timeout,
                $"Foundry Local visionが{timeout.TotalMilliseconds:F0}ms以内に完了しませんでした。",
                started,
                requestBytes);
        }
        catch (HttpRequestException ex)
        {
            return RawUnknown(FoundryVisionFailure.Http, ex.Message, started, requestBytes);
        }
        catch (JsonException ex)
        {
            return RawUnknown(FoundryVisionFailure.InvalidResponse, ex.Message, started, requestBytes);
        }
    }

    public void Dispose() => http.Dispose();

    private static SocketsHttpHandler CreateProductionHandler() => new()
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        UseCookies = false,
    };

    private static FoundryVisionResult Unknown(
        FoundryVisionFailure failure,
        string detail,
        Stopwatch started,
        int requestBytes)
    {
        started.Stop();
        return new FoundryVisionResult(
            FoundryVisionStatus.Unknown,
            failure,
            detail,
            FoundryVisionNormalization.None,
            [],
            string.Empty,
            started.ElapsedMilliseconds,
            requestBytes,
            null,
            null);
    }

    private static FoundryVisionRawResponse RawUnknown(
        FoundryVisionFailure failure,
        string detail,
        Stopwatch started,
        int requestBytes)
    {
        started.Stop();
        return new FoundryVisionRawResponse(
            FoundryVisionStatus.Unknown,
            failure,
            detail,
            string.Empty,
            started.ElapsedMilliseconds,
            requestBytes,
            null,
            null);
    }

    private static FoundryVisionControlsResult ControlsFromRaw(
        FoundryVisionRawResponse raw,
        IReadOnlyList<FoundryVisionControl> controls,
        FoundryVisionNormalization normalization = FoundryVisionNormalization.None) =>
        new(
            raw.Status,
            raw.Failure,
            raw.FailureDetail,
            controls,
            raw.RawOutput,
            raw.ElapsedMs,
            raw.RequestBytes,
            raw.InputTokens,
            raw.OutputTokens,
            normalization);

    private static bool TryParseControls(
        string rawOutput,
        out IReadOnlyList<FoundryVisionControl> controls,
        out FoundryVisionNormalization normalization,
        out string? error)
    {
        controls = [];
        normalization = FoundryVisionNormalization.None;
        error = null;
        try
        {
            using var document = JsonDocument.Parse(StripCodeFence(rawOutput));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || root.EnumerateObject().Count() != 1
                || !root.TryGetProperty("controls", out var array)
                || array.ValueKind != JsonValueKind.Array)
            {
                error = "vision responseはcontrolsだけを持つobjectではありません。";
                return false;
            }
            var result = new List<FoundryVisionControl>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var duplicateCollapsed = false;
            foreach (var element in array.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object
                    || element.EnumerateObject().Select(property => property.Name).Order().ToArray()
                        is not ["height", "kind", "label", "width", "x", "y"]
                    || !element.TryGetProperty("kind", out var kindElement)
                    || !element.TryGetProperty("label", out var labelElement)
                    || !element.TryGetProperty("x", out var xElement)
                    || !element.TryGetProperty("y", out var yElement)
                    || !element.TryGetProperty("width", out var widthElement)
                    || !element.TryGetProperty("height", out var heightElement)
                    || kindElement.ValueKind != JsonValueKind.String
                    || labelElement.ValueKind != JsonValueKind.String
                    || !xElement.TryGetDouble(out var x)
                    || !yElement.TryGetDouble(out var y)
                    || !widthElement.TryGetDouble(out var width)
                    || !heightElement.TryGetDouble(out var height))
                {
                    error = "controlはkind、label、normalized boundsだけを持つ必要があります。";
                    return false;
                }
                var kind = kindElement.GetString();
                var label = labelElement.GetString();
                if (kind is not ("text" or "icon")
                    || string.IsNullOrWhiteSpace(label)
                    || !FiniteUnit(x) || !FiniteUnit(y)
                    || !FinitePositiveUnit(width) || !FinitePositiveUnit(height)
                    || x + width > 1 || y + height > 1)
                {
                    error = "controlのkind、label、normalized boundsが不正です。";
                    return false;
                }
                var control = new FoundryVisionControl(kind, label.Trim(), x, y, width, height);
                var key = $"{control.Kind}|{control.Label}|{control.X:R}|{control.Y:R}|{control.Width:R}|{control.Height:R}";
                if (seen.Add(key))
                {
                    result.Add(control);
                }
                else
                {
                    duplicateCollapsed = true;
                }
            }
            controls = result;
            normalization = duplicateCollapsed
                ? FoundryVisionNormalization.DuplicateLabelsCollapsed
                : FoundryVisionNormalization.None;
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool FiniteUnit(double value) =>
        double.IsFinite(value) && value is >= 0 and <= 1;

    private static bool FinitePositiveUnit(double value) =>
        double.IsFinite(value) && value is > 0 and <= 1;

    private static string ReadProviderFailure(JsonElement root)
    {
        if (root.TryGetProperty("response", out var response)
            && response.TryGetProperty("error", out var error))
        {
            var code = error.TryGetProperty("code", out var codeElement)
                ? codeElement.GetString()
                : null;
            var message = error.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString()
                : null;
            return $"{code ?? "provider_error"}: {message ?? "details unavailable"}";
        }

        return "provider_error: details unavailable";
    }

    private static int? ReadNullableInt(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var element) && element.TryGetInt32(out var value)
            ? value
            : null;

    private static bool TryParseLabels(
        string rawOutput,
        out IReadOnlyList<string> labels,
        out FoundryVisionNormalization normalization,
        out string? error)
    {
        labels = [];
        normalization = FoundryVisionNormalization.None;
        error = null;
        var json = StripCodeFence(rawOutput);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || root.EnumerateObject().Count() != 1
                || !root.TryGetProperty("labels", out var labelArray)
                || labelArray.ValueKind != JsonValueKind.Array)
            {
                error = "vision responseはlabelsだけを持つobjectではありません。";
                return false;
            }

            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var duplicateCollapsed = false;
            foreach (var element in labelArray.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(element.GetString()))
                {
                    error = "vision responseのlabelが非空stringではありません。";
                    return false;
                }

                var label = element.GetString()!.Trim();
                if (string.Equals(label, "visible text label", StringComparison.OrdinalIgnoreCase))
                {
                    error = "vision responseがprompt説明用placeholderをlabelとして返しました。";
                    return false;
                }

                if (seen.Add(label))
                {
                    result.Add(label);
                }
                else
                {
                    duplicateCollapsed = true;
                }
            }

            labels = result;
            normalization = duplicateCollapsed
                ? FoundryVisionNormalization.DuplicateLabelsCollapsed
                : FoundryVisionNormalization.None;
            return true;
        }
        catch (JsonException ex)
        {
            if (TryRecoverTruncatedRepetitionLabels(json, out labels))
            {
                normalization = FoundryVisionNormalization.DuplicateLabelsCollapsed
                    | FoundryVisionNormalization.TruncatedRepetitionRecovered;
                return true;
            }
            error = ex.Message;
            return false;
        }
    }

    private static bool TryRecoverTruncatedRepetitionLabels(
        string json,
        out IReadOnlyList<string> labels)
    {
        labels = [];
        var index = 0;
        SkipWhitespace(json, ref index);
        if (!Consume(json, ref index, '{'))
        {
            return false;
        }
        SkipWhitespace(json, ref index);
        if (!TryReadJsonString(json, ref index, out var propertyName, out _)
            || propertyName != "labels")
        {
            return false;
        }
        SkipWhitespace(json, ref index);
        if (!Consume(json, ref index, ':'))
        {
            return false;
        }
        SkipWhitespace(json, ref index);
        if (!Consume(json, ref index, '['))
        {
            return false;
        }

        var completeLabels = new List<string>();
        var expectsValue = true;
        while (true)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length)
            {
                break;
            }
            if (expectsValue)
            {
                if (!TryReadJsonString(json, ref index, out var label, out var incomplete))
                {
                    if (incomplete)
                    {
                        break;
                    }
                    return false;
                }
                if (string.IsNullOrWhiteSpace(label)
                    || string.Equals(label, "visible text label", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                completeLabels.Add(label.Trim());
                expectsValue = false;
                continue;
            }
            if (!Consume(json, ref index, ','))
            {
                return false;
            }
            expectsValue = true;
        }

        if (completeLabels.Count == 0
            || completeLabels.GroupBy(label => label, StringComparer.Ordinal).Max(group => group.Count()) < 3)
        {
            return false;
        }

        labels = completeLabels.Distinct(StringComparer.Ordinal).ToArray();
        return true;
    }

    private static bool TryReadJsonString(
        string json,
        ref int index,
        out string value,
        out bool incomplete)
    {
        value = string.Empty;
        incomplete = false;
        if (index >= json.Length || json[index] != '"')
        {
            return false;
        }

        var start = index++;
        var escaped = false;
        for (; index < json.Length; index++)
        {
            var character = json[index];
            if (character < 0x20)
            {
                return false;
            }
            if (escaped)
            {
                escaped = false;
                continue;
            }
            if (character == '\\')
            {
                escaped = true;
                continue;
            }
            if (character != '"')
            {
                continue;
            }

            index++;
            try
            {
                value = JsonSerializer.Deserialize<string>(json[start..index]) ?? string.Empty;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        incomplete = true;
        return false;
    }

    private static void SkipWhitespace(string value, ref int index)
    {
        while (index < value.Length && char.IsWhiteSpace(value[index]))
        {
            index++;
        }
    }

    private static bool Consume(string value, ref int index, char expected)
    {
        if (index >= value.Length || value[index] != expected)
        {
            return false;
        }
        index++;
        return true;
    }

    private static string StripCodeFence(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (firstNewline < 0)
        {
            return trimmed;
        }
        return lastFence > firstNewline
            ? trimmed[(firstNewline + 1)..lastFence].Trim()
            : trimmed[(firstNewline + 1)..].Trim();
    }
}
