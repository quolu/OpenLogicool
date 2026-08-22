using System.IO;
using System.Text.Json;

namespace OpenLogicool.Host;

public sealed record SerialHidOutputSettings(
    string SchemaVersion,
    ResidentOutputRoute RequestedRoute,
    string? SelectedDeviceInstanceId)
{
    public const string CurrentSchemaVersion = "1.0";

    public static SerialHidOutputSettings Default { get; } =
        new(CurrentSchemaVersion, ResidentOutputRoute.SendInput, null);

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"output settings schema '{SchemaVersion}' は未対応です（対応: {CurrentSchemaVersion}）。");
        }

        if (!Enum.IsDefined(RequestedRoute))
        {
            throw new InvalidDataException($"requested output route '{RequestedRoute}' は未対応です。");
        }

        if (SelectedDeviceInstanceId is not null)
        {
            if (string.IsNullOrWhiteSpace(SelectedDeviceInstanceId))
            {
                throw new InvalidDataException("selected device instance IDが空です。");
            }

            if (SelectedDeviceInstanceId.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(SelectedDeviceInstanceId.AsSpan(3), out _))
            {
                throw new InvalidDataException("COM番号はdevice identityとして保存できません。");
            }
        }
    }
}

/// <summary>machine-local output route設定。COM番号でなくPnP device instance IDだけを永続化する。</summary>
public sealed class SerialHidOutputSettingsStore
{
    public const string FileName = "serial-hid-output-settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;

    public SerialHidOutputSettingsStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, FileName);
    }

    public static SerialHidOutputSettingsStore ForDatabase(string databasePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException($"database path has no directory: {databasePath}");
        }

        return new SerialHidOutputSettingsStore(directory);
    }

    public SerialHidOutputSettings Load()
    {
        if (!File.Exists(_path))
        {
            return SerialHidOutputSettings.Default;
        }

        var document = JsonSerializer.Deserialize<SettingsDocument>(File.ReadAllText(_path), JsonOptions)
            ?? throw new InvalidDataException("output settings JSONが空です。");
        var route = document.RequestedRoute switch
        {
            "send-input" => ResidentOutputRoute.SendInput,
            "serial-hid" => ResidentOutputRoute.SerialHid,
            _ => throw new InvalidDataException($"requestedRoute '{document.RequestedRoute}' は未対応です。"),
        };
        var settings = new SerialHidOutputSettings(document.SchemaVersion, route, document.SelectedDeviceInstanceId);
        settings.Validate();
        return settings;
    }

    public void Save(SerialHidOutputSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        var document = new SettingsDocument(
            settings.SchemaVersion,
            settings.RequestedRoute == ResidentOutputRoute.SerialHid ? "serial-hid" : "send-input",
            settings.SelectedDeviceInstanceId);
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions));
        File.Move(temporaryPath, _path, overwrite: true);
    }

    private sealed record SettingsDocument(
        string SchemaVersion,
        string RequestedRoute,
        string? SelectedDeviceInstanceId);
}
