using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Research;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Persistence;
using Xunit;

namespace OpenLogicool.Persistence.Tests;

public sealed class SqliteWebReferenceStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ReopenRestoresAppendOnlyRevisionsAndExport()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openlogicool-web-reference-{Guid.NewGuid():N}.db");
        try
        {
            using (var connection = Open(path))
            {
                var store = new SqliteWebReferenceStore(connection);
                store.AppendSource(Source("source-1", "https://example.test/guide"));
                store.AppendDocument(Document("document-1", 1, null, "source-1", "# Guide v1"));
                store.AppendFact(Fact("fact-1", 1, null, "document-1", "daily v1"));
                Assert.Equal(1, store.AppendResearchRun(Run("run-1", ResearchRunStatus.Planned)));
                Assert.Equal(2, store.AppendResearchRun(Run("run-1", ResearchRunStatus.Running)));
                store.AppendExclusion(new(
                    ContractSchemaVersions.Revision01,
                    "exclude-1",
                    new Uri("https://excluded.test/"),
                    Now,
                    "利用者指定"));
                store.AppendReacquisitionRequest(new(
                    ContractSchemaVersions.Revision01,
                    "request-1",
                    "source-1",
                    Now,
                    "更新確認"));
            }

            using (var reopened = Open(path))
            {
                var restored = new SqliteWebReferenceStore(reopened);
                restored.AppendDocument(Document("document-2", 2, "document-1", "source-1", "# Guide v2"));
                restored.AppendFact(Fact("fact-2", 2, "fact-1", "document-2", "daily v2"));

                var exported = restored.Export(Now.AddMinutes(1));

                Assert.Single(exported.Sources);
                Assert.Equal(["document-1", "document-2"], exported.Documents.Select(value => value.DocumentId));
                Assert.Equal(["fact-1", "fact-2"], exported.Facts.Select(value => value.FactId));
                Assert.Equal([1L, 2L], exported.ResearchRuns.Select(value => value.RevisionNumber));
                Assert.Single(exported.Exclusions);
                Assert.Single(exported.ReacquisitionRequests);
                Assert.Empty(exported.Tombstones);
                Assert.Contains("Guide v2", JsonSerializer.Serialize(exported), StringComparison.Ordinal);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public void BrokenRevisionChainAndDuplicateAppendAreRejected()
    {
        using var connection = OpenInMemory();
        var store = new SqliteWebReferenceStore(connection);
        var source = Source("source-1", "https://example.test/guide");
        store.AppendSource(source);
        store.AppendDocument(Document("document-1", 1, null, source.SourceId, "v1"));

        Assert.Throws<SqliteException>(() => store.AppendSource(source));
        Assert.Throws<InvalidOperationException>(() =>
            store.AppendDocument(Document("document-3", 3, "document-1", source.SourceId, "v3")));
        Assert.Throws<SqliteException>(() =>
            store.AppendDocument(Document("document-other-root", 1, null, source.SourceId, "other root")));
    }

    [Fact]
    public void FactAndContradictionRejectMissingSourceReferences()
    {
        using var connection = OpenInMemory();
        var store = new SqliteWebReferenceStore(connection);
        store.AppendSource(Source("source-1", "https://example.test/guide"));
        store.AppendDocument(Document("document-1", 1, null, "source-1", "v1"));
        store.AppendFact(Fact("fact-1", 1, null, "document-1", "one"));
        store.AppendFact(Fact("fact-2", 1, null, "document-1", "two"));

        Assert.Throws<InvalidOperationException>(() =>
            store.AppendFact(Fact("fact-missing", 1, null, "missing-document", "missing")));
        var contradictedWithMissingRecord = Fact("fact-contradicted", 1, null, "document-1", "contradicted") with
        {
            Validity = WebReferenceFactValidity.Contradicted,
            ContradictionIds = ["missing-contradiction"],
        };
        Assert.Throws<InvalidOperationException>(() => store.AppendFact(contradictedWithMissingRecord));
        Assert.Throws<InvalidOperationException>(() => store.AppendContradiction(new(
            ContractSchemaVersions.Revision01,
            "contradiction-missing",
            1,
            null,
            "fact-1",
            "fact-2",
            ["missing-document"],
            Now,
            "missing source")));
    }

    [Fact]
    public void ResearchRunRejectsMissingAcquisitionReferences()
    {
        using var connection = OpenInMemory();
        var store = new SqliteWebReferenceStore(connection);
        var attempt = new WebReferenceAcquisitionAttempt(
            ContractSchemaVersions.Revision01,
            "attempt-1",
            new Uri("https://example.test/guide"),
            Now,
            Now.AddSeconds(1),
            WebReferenceAcquisitionStatus.Succeeded,
            "missing-source",
            "missing-document",
            null,
            null);
        var run = new ResearchRun(
            ContractSchemaVersions.Revision01,
            "run-1",
            "nikke",
            "日課候補を調べる",
            ResearchRunStatus.Completed,
            Now,
            Now.AddMinutes(1),
            [attempt]);

        Assert.Throws<InvalidOperationException>(() => store.AppendResearchRun(run));
    }

    [Fact]
    public void DeleteSourceRemovesPayloadAndKeepsOnlyTombstone()
    {
        using var connection = OpenInMemory();
        var store = new SqliteWebReferenceStore(connection);
        store.AppendSource(Source("source-1", "https://example.test/one"));
        store.AppendSource(Source("source-2", "https://example.test/two"));
        store.AppendDocument(Document("document-1", 1, null, "source-1", "RAW_PAYLOAD_ONE"));
        store.AppendDocument(Document("document-2", 1, null, "source-2", "KEEP_PAYLOAD_TWO"));
        store.AppendFact(Fact("fact-1", 1, null, "document-1", "fact one"));
        store.AppendFact(Fact("fact-2", 1, null, "document-2", "fact two"));
        store.AppendContradiction(new(
            ContractSchemaVersions.Revision01,
            "contradiction-1",
            1,
            null,
            "fact-1",
            "fact-2",
            ["document-1", "document-2"],
            Now,
            "sources disagree"));

        var payloadBytesBefore = ReadAllPayloadBytes(connection);
        var preview = store.PreviewDeleteSource("source-1");
        var tombstone = store.DeleteSource("source-1", "tombstone-1", Now, "利用者削除");
        var exported = store.Export(Now);

        Assert.Equal(["document-1"], preview.DocumentIds);
        Assert.Equal(["fact-1"], preview.FactIds);
        Assert.Equal(["contradiction-1"], preview.ContradictionIds);
        Assert.Equal(payloadBytesBefore - ReadAllPayloadBytes(connection), preview.PayloadBytes);
        Assert.Equal(preview.DocumentIds, tombstone.DeletedDocumentIds);
        Assert.Equal(preview.FactIds, tombstone.DeletedFactIds);
        Assert.Equal(preview.ContradictionIds, tombstone.DeletedContradictionIds);
        Assert.Equal(["source-2"], exported.Sources.Select(value => value.SourceId));
        Assert.Equal(["document-2"], exported.Documents.Select(value => value.DocumentId));
        Assert.Equal(["fact-2"], exported.Facts.Select(value => value.FactId));
        Assert.Empty(exported.Contradictions);
        Assert.Single(exported.Tombstones);
        Assert.DoesNotContain("RAW_PAYLOAD_ONE", ReadAllPayloadJson(connection), StringComparison.Ordinal);
        Assert.Contains("KEEP_PAYLOAD_TWO", ReadAllPayloadJson(connection), StringComparison.Ordinal);
    }

    [Fact]
    public void DeleteSourceAlsoDeletesRevisionDescendants()
    {
        using var connection = OpenInMemory();
        var store = new SqliteWebReferenceStore(connection);
        store.AppendSource(Source("source-1", "https://example.test/one"));
        store.AppendSource(Source("source-2", "https://example.test/two"));
        store.AppendDocument(Document("document-1", 1, null, "source-1", "one"));
        store.AppendDocument(Document("document-2", 1, null, "source-2", "two"));
        store.AppendFact(Fact("fact-1", 1, null, "document-1", "old"));
        store.AppendFact(Fact("fact-2", 2, "fact-1", "document-2", "new"));
        store.AppendFact(Fact("fact-3", 1, null, "document-2", "other"));
        store.AppendContradiction(new(
            ContractSchemaVersions.Revision01,
            "contradiction-1",
            1,
            null,
            "fact-1",
            "fact-3",
            ["document-1"],
            Now,
            "old conflict"));
        store.AppendContradiction(new(
            ContractSchemaVersions.Revision01,
            "contradiction-2",
            2,
            "contradiction-1",
            "fact-2",
            "fact-3",
            ["document-2"],
            Now.AddMinutes(1),
            "new conflict"));

        var preview = store.PreviewDeleteSource("source-1");
        store.DeleteSource("source-1", "tombstone-1", Now, "利用者削除");

        Assert.Equal(["fact-1", "fact-2"], preview.FactIds);
        Assert.Equal(["contradiction-1", "contradiction-2"], preview.ContradictionIds);
        Assert.Equal(["fact-3"], store.ListFacts().Select(value => value.FactId));
        Assert.Empty(store.ListContradictions());
    }

    [Fact]
    public void DeleteSourceAlsoDeletesFactsThatReferenceAffectedContradictions()
    {
        using var connection = OpenInMemory();
        var store = new SqliteWebReferenceStore(connection);
        store.AppendSource(Source("source-1", "https://example.test/one"));
        store.AppendSource(Source("source-2", "https://example.test/two"));
        store.AppendDocument(Document("document-1", 1, null, "source-1", "one"));
        store.AppendDocument(Document("document-2", 1, null, "source-2", "two"));
        store.AppendFact(Fact("fact-a", 1, null, "document-2", "a"));
        store.AppendFact(Fact("fact-b", 1, null, "document-2", "b"));
        store.AppendContradiction(new(
            ContractSchemaVersions.Revision01,
            "contradiction-1",
            1,
            null,
            "fact-a",
            "fact-b",
            ["document-1"],
            Now,
            "source one conflict"));
        store.AppendFact(Fact("fact-a-v2", 2, "fact-a", "document-2", "a contradicted") with
        {
            Validity = WebReferenceFactValidity.Contradicted,
            ContradictionIds = ["contradiction-1"],
        });

        var preview = store.PreviewDeleteSource("source-1");
        store.DeleteSource("source-1", "tombstone-1", Now, "利用者削除");

        Assert.Equal(["fact-a-v2"], preview.FactIds);
        Assert.Equal(["contradiction-1"], preview.ContradictionIds);
        Assert.Equal(["fact-a", "fact-b"], store.ListFacts().Select(value => value.FactId));
        Assert.Empty(store.ListContradictions());
    }

    [Fact]
    public void SummaryOnlyDocumentCannotPersistAnUnrepresentedRawBody()
    {
        using var connection = OpenInMemory();
        var store = new SqliteWebReferenceStore(connection);
        var source = SummarySource("gamewith-1");
        store.AppendSource(source);
        store.AppendDocument(new ReferenceDocument(
            ContractSchemaVersions.Revision01,
            "summary-1",
            1,
            null,
            source.SourceId,
            SourcePolicy.SummaryOnly,
            Now,
            new SummaryReferenceBody(
                ContractSchemaVersions.Revision01,
                "# 日課候補",
                ["短い根拠"],
                ["日課"])));

        const string forbiddenRaw = "FULL_GAMEWITH_PAGE";
        var persisted = ReadAllPayloadJson(connection);
        Assert.DoesNotContain(forbiddenRaw, persisted, StringComparison.Ordinal);
        Assert.IsType<SummaryReferenceBody>(store.ListDocuments().Single().Body);
        Assert.DoesNotContain(
            typeof(SummaryReferenceBody).GetProperties(),
            property => property.Name.Contains("Raw", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Html", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Full", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnknownSchemaIsNotSilentlySkipped()
    {
        using var connection = OpenInMemory();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                INSERT INTO web_reference_sources (source_id, schema_version, policy, source_json, payload_bytes)
                VALUES ('future', '9.0', 'FullTextAllowed', '{}', 2);
                """;
            command.ExecuteNonQuery();
        }

        var store = new SqliteWebReferenceStore(connection);
        Assert.Throws<InvalidOperationException>(() => store.ListSources());
    }

    [Fact]
    public void PayloadSchemaMismatchIsNotSilentlyAccepted()
    {
        using var connection = OpenInMemory();
        var store = new SqliteWebReferenceStore(connection);
        store.AppendSource(Source("source-1", "https://example.test/guide"));
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                UPDATE web_reference_sources
                SET source_json = replace(source_json, '"SchemaVersion":"0.1.0"', '"SchemaVersion":"9.0"')
                WHERE source_id = 'source-1';
                """;
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        Assert.Throws<ArgumentException>(() => store.ListSources());
    }

    [Fact]
    public void PublicStoreSurfaceHasNoUpdateOrUpsert()
    {
        var mutationNames = typeof(SqliteWebReferenceStore)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .Where(name => name.Contains("Update", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Upsert", StringComparison.OrdinalIgnoreCase));

        Assert.Empty(mutationNames);
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
        return connection;
    }

    private static SqliteConnection OpenInMemory()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
        return connection;
    }

    private static AcquiredWebReferenceSource Source(string id, string url)
    {
        var uri = new Uri(url);
        var evidence = new SourcePolicyEvidence(
            ContractSchemaVersions.Revision01,
            SourceTermsDisposition.FullTextAllowed,
            RobotsDisposition.Allowed);
        return new AcquiredWebReferenceSource(
            ContractSchemaVersions.Revision01,
            id,
            uri,
            uri,
            $"title-{id}",
            "publisher",
            null,
            Now,
            "ja-JP",
            WebReferenceSourceKind.Official,
            evidence,
            SourcePolicyEvaluator.Evaluate(uri, uri, evidence),
            new WebReferenceProvenance(
                ContractSchemaVersions.Revision01,
                $"digest-{id}",
                WebReferenceAcquisitionMethod.DirectHttp,
                Now,
                null,
                Now.AddDays(1)));
    }

    private static AcquiredWebReferenceSource SummarySource(string id)
    {
        var uri = new Uri("https://gamewith.jp/nikke/article/show/1");
        var evidence = new SourcePolicyEvidence(
            ContractSchemaVersions.Revision01,
            SourceTermsDisposition.SummaryAllowed,
            RobotsDisposition.Allowed);
        return new AcquiredWebReferenceSource(
            ContractSchemaVersions.Revision01,
            id,
            uri,
            uri,
            "NIKKE日課",
            "GameWith",
            null,
            Now,
            "ja-JP",
            WebReferenceSourceKind.Guide,
            evidence,
            SourcePolicyEvaluator.Evaluate(uri, uri, evidence),
            new WebReferenceProvenance(
                ContractSchemaVersions.Revision01,
                "digest-summary",
                WebReferenceAcquisitionMethod.DirectHttp,
                Now,
                new AiSummaryProvenance(
                    ContractSchemaVersions.Revision01,
                    "provider",
                    "model",
                    "prompt-v1",
                    Now,
                    AiExecutionLocation.LocalDevice,
                    0m),
                Now.AddDays(1)));
    }

    private static ReferenceDocument Document(
        string id,
        long revision,
        string? parent,
        string sourceId,
        string markdown) => new(
            ContractSchemaVersions.Revision01,
            id,
            revision,
            parent,
            sourceId,
            SourcePolicy.FullTextAllowed,
            Now.AddMinutes(revision),
            new FullTextReferenceBody(ContractSchemaVersions.Revision01, markdown));

    private static WebReferenceFact Fact(
        string id,
        long revision,
        string? parent,
        string sourceReference,
        string claim) => new(
            ContractSchemaVersions.Revision01,
            id,
            revision,
            parent,
            WebReferenceFactKind.Daily,
            claim,
            [sourceReference],
            0.7m,
            WebReferenceFactValidity.Hypothesis,
            new WebReferenceFactScope(ContractSchemaVersions.Revision01, null, "ja-JP", Now, null),
            [],
            Now.AddMinutes(revision));

    private static ResearchRun Run(string id, ResearchRunStatus status) => new(
        ContractSchemaVersions.Revision01,
        id,
        "nikke",
        "日課候補を調べる",
        status,
        Now,
        status is ResearchRunStatus.Completed or ResearchRunStatus.Failed or ResearchRunStatus.Cancelled
            ? Now.AddMinutes(1)
            : null,
        []);

    private static string ReadAllPayloadJson(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT source_json FROM web_reference_sources
            UNION ALL SELECT document_json FROM web_reference_documents
            UNION ALL SELECT fact_json FROM web_reference_facts
            UNION ALL SELECT contradiction_json FROM web_reference_contradictions;
            """;
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return string.Join('\n', values);
    }

    private static long ReadAllPayloadBytes(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COALESCE((SELECT SUM(payload_bytes) FROM web_reference_sources), 0)
                 + COALESCE((SELECT SUM(payload_bytes) FROM web_reference_documents), 0)
                 + COALESCE((SELECT SUM(payload_bytes) FROM web_reference_facts), 0)
                 + COALESCE((SELECT SUM(payload_bytes) FROM web_reference_contradictions), 0);
            """;
        return (long)command.ExecuteScalar()!;
    }
}
