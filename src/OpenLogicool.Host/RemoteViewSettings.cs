using System.IO;
using System.Text.Json;
using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Host;

/// <summary>遠隔表示の設定。パスワードは含めない（資格情報マネージャーだけが持つ）。</summary>
public sealed record RemoteViewSettings(
    string SchemaVersion,
    string? PublishUrl,
    string? ViewerUrl,
    string? PublishUser,
    RemoteViewQuality Quality)
{
    public const string CurrentSchemaVersion = "1.0";

    public static RemoteViewSettings Default { get; } =
        new(CurrentSchemaVersion, null, null, null, RemoteViewQuality.Standard);

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"remote view settings schema '{SchemaVersion}' は未対応です（対応: {CurrentSchemaVersion}）。");
        }

        if (!Enum.IsDefined(Quality))
        {
            throw new InvalidDataException($"画質 '{Quality}' は未対応です。");
        }

        if (PublishUrl is not null)
        {
            if (!Uri.TryCreate(PublishUrl, UriKind.Absolute, out var publish)
                || !(publish.Scheme == Uri.UriSchemeHttps
                     || (publish.Scheme == Uri.UriSchemeHttp && publish.Host is "127.0.0.1" or "localhost")))
            {
                throw new InvalidDataException(
                    "送信先のURLはhttpsだけ使えます（実験用に http://127.0.0.1 と http://localhost は許します）。");
            }
        }

        if (ViewerUrl is not null
            && (!Uri.TryCreate(ViewerUrl, UriKind.Absolute, out var viewer)
                || viewer.Scheme is not ("https" or "http")))
        {
            throw new InvalidDataException("視聴用のURLはhttpまたはhttpsの絶対URLで指定してください。");
        }

        if (PublishUser is not null && (string.IsNullOrWhiteSpace(PublishUser) || PublishUser.Contains(':')))
        {
            throw new InvalidDataException("送信用のユーザー名は空にできず、':' を含められません。");
        }
    }
}

/// <summary>machine-local の遠隔表示設定。DB の隣の remote-view-settings.json に保存する。</summary>
public sealed class RemoteViewSettingsStore
{
    public const string FileName = "remote-view-settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;

    public RemoteViewSettingsStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, FileName);
    }

    public static RemoteViewSettingsStore ForDatabase(string databasePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException($"database path has no directory: {databasePath}");
        }

        return new RemoteViewSettingsStore(directory);
    }

    public RemoteViewSettings Load()
    {
        if (!File.Exists(_path))
        {
            return RemoteViewSettings.Default;
        }

        var document = JsonSerializer.Deserialize<SettingsDocument>(File.ReadAllText(_path), JsonOptions)
            ?? throw new InvalidDataException("remote view settings JSONが空です。");
        var quality = document.Quality switch
        {
            "standard" => RemoteViewQuality.Standard,
            "fine" => RemoteViewQuality.Fine,
            _ => throw new InvalidDataException($"quality '{document.Quality}' は未対応です。"),
        };
        var settings = new RemoteViewSettings(
            document.SchemaVersion, document.PublishUrl, document.ViewerUrl, document.PublishUser, quality);
        settings.Validate();
        return settings;
    }

    public void Save(RemoteViewSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        var document = new SettingsDocument(
            settings.SchemaVersion,
            settings.PublishUrl,
            settings.ViewerUrl,
            settings.PublishUser,
            settings.Quality == RemoteViewQuality.Fine ? "fine" : "standard");
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions));
        File.Move(temporaryPath, _path, overwrite: true);
    }

    private sealed record SettingsDocument(
        string SchemaVersion,
        string? PublishUrl,
        string? ViewerUrl,
        string? PublishUser,
        string Quality);
}
