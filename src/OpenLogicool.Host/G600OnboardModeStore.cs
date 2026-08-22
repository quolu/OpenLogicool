using System.IO;
using System.Text.Json;

namespace OpenLogicool.Host;

/// <summary>onboard 書込み中の永続状態（何をいつ焼いたか）。</summary>
public sealed record G600OnboardModeState(string WorkspaceId, string ProfileId, DateTimeOffset AppliedAtUtc);

/// <summary>
/// 「G600 onboard へ workspace 割当を書込み中」の永続フラグ。baseline store と同じく DB schema を増やさず
/// db ディレクトリのファイルで持つ。このフラグが立っている間、常駐は G600 の SendInput 送出を抑止し
/// （onboard がハードウェアとして送るため二重入力になる）、残置（leftover）の apply/restore も行わない
/// （handled shutdown で焼いた内容を消さない）。
/// </summary>
public sealed class G600OnboardModeStore
{
    public const string FileName = "g600-onboard-mode.json";

    private readonly string _path;

    public G600OnboardModeStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, FileName);
    }

    public static G600OnboardModeStore ForDatabase(string databasePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException($"database path has no directory: {databasePath}");
        }

        return new G600OnboardModeStore(directory);
    }

    public G600OnboardModeState? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        var state = JsonSerializer.Deserialize<G600OnboardModeState>(File.ReadAllText(_path));
        if (state is null || string.IsNullOrWhiteSpace(state.WorkspaceId) || string.IsNullOrWhiteSpace(state.ProfileId))
        {
            throw new InvalidDataException($"onboard mode state at {_path} is not valid.");
        }

        return state;
    }

    public void Save(G600OnboardModeState state)
    {
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state));
        File.Copy(temp, _path, overwrite: true);
        File.Delete(temp);
    }

    public void Clear()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
