using System.Globalization;
using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Persistence;

/// <summary>
/// IEngineeringLogSink のローカルファイル実装（OPS-009）。
/// journal（SQLite）とは別の保存先＝日付別テキストファイルへ 1 event 1 行を追記する。
/// 行は EngineeringLogEntry の相関情報だけで、payload 本文は型として受け取れない。
/// retention は 14 日ローテーション（Data Flow Contract）。削除は PurgeOlderThanRetention の
/// 明示呼び出しだけで行い、削除したファイル一覧を返す。
/// </summary>
public sealed class FileEngineeringLog(string directoryPath) : IEngineeringLogSink
{
    public const int RetentionDays = 14;
    private const string FilePrefix = "engineering-";
    private const string FileSuffix = ".log";

    public void Record(EngineeringLogEntry entry)
    {
        Directory.CreateDirectory(directoryPath);
        var line = string.Join(
            '\t',
            entry.PersistedUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture),
            entry.CorrelationId,
            entry.CausationId,
            entry.RunId,
            entry.RunSequence.ToString(CultureInfo.InvariantCulture),
            entry.EventId,
            entry.PayloadType);
        File.AppendAllLines(FilePathFor(entry.PersistedUtc), [line]);
    }

    /// <summary>retention（14 日）を超えた日付のログファイルを削除し、削除した full path 一覧を返す。</summary>
    public IReadOnlyList<string> PurgeOlderThanRetention(DateTimeOffset asOfUtc)
    {
        if (!Directory.Exists(directoryPath))
        {
            return [];
        }

        var cutoffDate = asOfUtc.UtcDateTime.Date.AddDays(-RetentionDays);
        var purged = new List<string>();
        foreach (var filePath in Directory.EnumerateFiles(directoryPath, $"{FilePrefix}*{FileSuffix}"))
        {
            var stem = Path.GetFileName(filePath)[FilePrefix.Length..^FileSuffix.Length];
            if (!DateTime.TryParseExact(stem, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fileDate))
            {
                throw new InvalidOperationException($"engineering log の日付部が読めません: '{filePath}'");
            }

            if (fileDate < cutoffDate)
            {
                File.Delete(filePath);
                purged.Add(filePath);
            }
        }

        return purged;
    }

    private string FilePathFor(DateTimeOffset persistedUtc) =>
        Path.Combine(
            directoryPath,
            $"{FilePrefix}{persistedUtc.UtcDateTime:yyyyMMdd}{FileSuffix}");
}
