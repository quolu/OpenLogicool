using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OpenLogicool.Host;

/// <summary>中継サーバーで、いま見ている・待っている端末の数を得る。</summary>
public interface IRemoteViewViewerQuery
{
    /// <param name="authorization">「ユーザー名:パスワード」。</param>
    int CountViewers(string publishUrl, string authorization);
}

/// <summary>
/// 中継サーバー（MediaMTX）の「WebRTC の接続の一覧」を読んで数える。PC のほうから聞きに行くだけで、PC に待ち受けは作らない。
/// 聞く先は送り先の URL の隣（…/&lt;path&gt;/whip に対して …/&lt;path&gt;/viewers）。中継サーバーの前段が、一覧を読む口へ渡す。
/// </summary>
public sealed class HttpRemoteViewViewerQuery : IRemoteViewViewerQuery, IDisposable
{
    private readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(5) };

    public int CountViewers(string publishUrl, string authorization)
    {
        ArgumentException.ThrowIfNullOrEmpty(authorization);
        using var request = new HttpRequestMessage(HttpMethod.Get, ViewersUrl(publishUrl));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(authorization)));
        using var response = client.Send(request);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"中継サーバーが視聴の問い合わせを断りました（HTTP {(int)response.StatusCode}）。");
        }

        using var reader = new StreamReader(response.Content.ReadAsStream(), Encoding.UTF8);
        return CountReaders(reader.ReadToEnd(), PathName(publishUrl));
    }

    public void Dispose() => client.Dispose();

    /// <summary>…/&lt;path&gt;/whip → …/&lt;path&gt;/viewers。</summary>
    public static Uri ViewersUrl(string publishUrl) => new(Publish(publishUrl), "viewers");

    /// <summary>送り先の URL の中の、中継サーバーでの path の名前。</summary>
    public static string PathName(string publishUrl)
    {
        var segments = Publish(publishUrl).AbsolutePath.Trim('/').Split('/');
        return Uri.UnescapeDataString(string.Join('/', segments[..^1]));
    }

    /// <summary>一覧のうち、この path を読んでいる（見ている・送り手を待っている）接続の数。</summary>
    public static int CountReaders(string json, string pathName)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("中継サーバーの応答に接続の一覧（items）がありません。");
        }

        var count = 0;
        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("state", out var state) && state.GetString() == "read"
                && item.TryGetProperty("path", out var path) && path.GetString() == pathName)
            {
                count++;
            }
        }

        return count;
    }

    private static Uri Publish(string publishUrl)
    {
        if (!Uri.TryCreate(publishUrl, UriKind.Absolute, out var uri)
            || uri.AbsolutePath.Trim('/').Split('/') is not { Length: >= 2 } segments
            || segments[^1] != "whip")
        {
            throw new InvalidOperationException("送り先の URL が「…/<path>/whip」の形ではないため、視聴の有無を問い合わせる先を決められません。");
        }

        return uri;
    }
}

public enum RemoteViewOnDemandAction { None, Start, Stop }

/// <summary>
/// 見ている端末の数から、送り始める・止めるを決める。時計と数だけで決まる pure な状態機。
/// <list type="bullet">
/// <item>誰もいない所へ見に来たら、送り始める。</item>
/// <item>誰もいなくなって <c>idleStop</c> たったら、止める。ページの再試行の合間（数秒）では止めない。</item>
/// <item>送り始めた後に配信が失敗しても、見ている端末がいる間は送り直さない（自動では再起動しない）。いなくなってから次に見に来た時に送り直す。</item>
/// </list>
/// </summary>
public sealed class RemoteViewOnDemandPolicy(TimeSpan idleStop)
{
    private TimeSpan? emptySince;
    private bool engaged;

    /// <summary>見に来たのを受けて開始を出した後で、まだ「いなくなった」と決めていない間。</summary>
    public bool Engaged => engaged;

    public RemoteViewOnDemandAction Step(TimeSpan now, int viewers, bool running)
    {
        if (viewers > 0)
        {
            emptySince = null;
            if (engaged)
            {
                return RemoteViewOnDemandAction.None;
            }

            engaged = true;
            return running ? RemoteViewOnDemandAction.None : RemoteViewOnDemandAction.Start;
        }

        emptySince ??= now;
        if (now - emptySince.Value < idleStop)
        {
            return RemoteViewOnDemandAction.None;
        }

        engaged = false;
        return running ? RemoteViewOnDemandAction.Stop : RemoteViewOnDemandAction.None;
    }

    public void Reset()
    {
        emptySince = null;
        engaged = false;
    }
}
