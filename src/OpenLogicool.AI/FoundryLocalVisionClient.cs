using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
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

public sealed record FoundryVisionResult(
    FoundryVisionStatus Status,
    FoundryVisionFailure Failure,
    string? FailureDetail,
    IReadOnlyList<string> Labels,
    string RawOutput,
    long ElapsedMs,
    int RequestBytes,
    int? InputTokens,
    int? OutputTokens);

public sealed class FoundryLocalVisionClient : IDisposable
{
    private const string Prompt =
        "Read the image. Find visually clickable controls that contain visible words. " +
        "Copy each control's visible words exactly, preserving case. " +
        "Return a JSON object whose only property is named labels and whose value is an array of those copied strings. " +
        "If no such controls exist, return {\"labels\":[]}. " +
        "Never output the phrase 'visible text label'. " +
        "Do not output coordinates, descriptions, non-interactive text, or markdown.";

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

    public async Task<FoundryVisionResult> ProposeLabelsAsync(
        ReadOnlyMemory<byte> pngBytes,
        CancellationToken cancellationToken = default)
    {
        if (pngBytes.IsEmpty)
        {
            throw new ArgumentException("vision input PNGが空です。", nameof(pngBytes));
        }

        var body = JsonSerializer.Serialize(new
        {
            model = modelId,
            max_output_tokens = 500,
            stream = true,
            input = new object[]
            {
                new
                {
                    type = "message",
                    role = "user",
                    content = new object[]
                    {
                        new { type = "input_text", text = Prompt },
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
                return Unknown(
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
                return new FoundryVisionResult(
                    FoundryVisionStatus.Unknown,
                    FoundryVisionFailure.Provider,
                    providerFailure,
                    [],
                    output.ToString(),
                    started.ElapsedMilliseconds,
                    requestBytes,
                    inputTokens,
                    outputTokens);
            }

            if (!completed)
            {
                return new FoundryVisionResult(
                    FoundryVisionStatus.Unknown,
                    FoundryVisionFailure.Provider,
                    "Foundry Local streamがresponse.completedより前に終了しました。",
                    [],
                    output.ToString(),
                    started.ElapsedMilliseconds,
                    requestBytes,
                    inputTokens,
                    outputTokens);
            }

            if (!TryParseLabels(output.ToString(), out var labels, out var validationError))
            {
                return new FoundryVisionResult(
                    FoundryVisionStatus.Unknown,
                    FoundryVisionFailure.InvalidResponse,
                    validationError,
                    [],
                    output.ToString(),
                    started.ElapsedMilliseconds,
                    requestBytes,
                    inputTokens,
                    outputTokens);
            }

            return new FoundryVisionResult(
                FoundryVisionStatus.Completed,
                FoundryVisionFailure.None,
                null,
                labels,
                output.ToString(),
                started.ElapsedMilliseconds,
                requestBytes,
                inputTokens,
                outputTokens);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unknown(
                FoundryVisionFailure.Timeout,
                $"Foundry Local visionが{timeout.TotalMilliseconds:F0}ms以内に完了しませんでした。",
                started,
                requestBytes);
        }
        catch (HttpRequestException ex)
        {
            return Unknown(FoundryVisionFailure.Http, ex.Message, started, requestBytes);
        }
        catch (JsonException ex)
        {
            return Unknown(FoundryVisionFailure.InvalidResponse, ex.Message, started, requestBytes);
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
            [],
            string.Empty,
            started.ElapsedMilliseconds,
            requestBytes,
            null,
            null);
    }

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
        out string? error)
    {
        labels = [];
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

                if (!seen.Add(label))
                {
                    error = $"vision responseに重複label '{label}' があります。";
                    return false;
                }

                result.Add(label);
            }

            labels = result;
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
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
        return firstNewline >= 0 && lastFence > firstNewline
            ? trimmed[(firstNewline + 1)..lastFence].Trim()
            : trimmed;
    }
}
