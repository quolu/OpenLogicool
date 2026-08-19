using OpenLogicool.Contracts.Playbooks;
using Xunit;

namespace OpenLogicool.Persistence.Tests;

public sealed class FileEngineeringLogTests : IDisposable
{
    private readonly string _directoryPath = Path.Combine(
        Path.GetTempPath(), $"openlogicool-engineering-log-{Guid.NewGuid():N}");

    private static EngineeringLogEntry Entry(string persistedUtc, string correlationId = "correlation-1") =>
        new(
            "0.1.0",
            DateTimeOffset.Parse(persistedUtc),
            correlationId,
            "cause-1",
            "run-1",
            1,
            "event-1",
            RunEventPayloadTypes.Observation);

    [Fact]
    public void Record_appends_one_line_per_entry_into_a_dated_file()
    {
        var log = new FileEngineeringLog(_directoryPath);
        log.Record(Entry("2026-08-19T01:00:00Z", "correlation-a"));
        log.Record(Entry("2026-08-19T02:00:00Z", "correlation-b"));
        log.Record(Entry("2026-08-20T01:00:00Z", "correlation-c"));

        var lines19 = File.ReadAllLines(Path.Combine(_directoryPath, "engineering-20260819.log"));
        var lines20 = File.ReadAllLines(Path.Combine(_directoryPath, "engineering-20260820.log"));
        Assert.Equal(2, lines19.Length);
        Assert.Single(lines20);
        Assert.Contains("correlation-a", lines19[0]);
        Assert.Contains("correlation-b", lines19[1]);
        Assert.Contains("correlation-c", lines20[0]);
    }

    [Fact]
    public void Purge_deletes_only_files_older_than_the_fourteen_day_retention()
    {
        var log = new FileEngineeringLog(_directoryPath);
        log.Record(Entry("2026-08-01T00:00:00Z"));
        log.Record(Entry("2026-08-05T00:00:00Z"));
        log.Record(Entry("2026-08-19T00:00:00Z"));

        var purged = log.PurgeOlderThanRetention(DateTimeOffset.Parse("2026-08-19T12:00:00Z"));

        Assert.Equal([Path.Combine(_directoryPath, "engineering-20260801.log")], purged);
        Assert.False(File.Exists(Path.Combine(_directoryPath, "engineering-20260801.log")));
        Assert.True(File.Exists(Path.Combine(_directoryPath, "engineering-20260805.log")));
        Assert.True(File.Exists(Path.Combine(_directoryPath, "engineering-20260819.log")));
    }

    [Fact]
    public void Purge_of_a_missing_directory_deletes_nothing()
    {
        var log = new FileEngineeringLog(_directoryPath);

        Assert.Empty(log.PurgeOlderThanRetention(DateTimeOffset.Parse("2026-08-19T00:00:00Z")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directoryPath))
        {
            Directory.Delete(_directoryPath, recursive: true);
        }
    }
}
