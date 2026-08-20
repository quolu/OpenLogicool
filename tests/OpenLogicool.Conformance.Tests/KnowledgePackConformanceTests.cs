using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Perception;
using Xunit;

namespace OpenLogicool.Conformance.Tests;

public sealed class KnowledgePackConformanceTests
{
    [Fact]
    public void Import_keeps_pack_untrusted_and_downgrades_screen_graph_to_candidate()
    {
        var (document, sections) = ValidPack();
        var imported = KnowledgePackValidator.Import(document with
        {
            ScreenGraph = document.ScreenGraph with
            {
                Nodes = new[] { new ScreenGraphNode("0.1.0", "state.main-menu", ScreenGraphVerificationState.Verified) },
                Edges = new[] { new ScreenGraphEdge("0.1.0", "state.main-menu", "state.main-menu", "target.start", "action.start", ScreenGraphVerificationState.Verified) },
            },
        }, sections);

        Assert.Equal(KnowledgePackTrust.Untrusted, imported.Manifest.Trust);
        Assert.All(imported.ScreenGraph.Nodes, node => Assert.Equal(ScreenGraphVerificationState.Candidate, node.VerificationState));
        Assert.All(imported.ScreenGraph.Edges, edge => Assert.Equal(ScreenGraphVerificationState.Candidate, edge.VerificationState));
    }

    [Fact]
    public void Import_rejects_non_data_section_and_unsafe_section_path()
    {
        var (document, sections) = ValidPack();
        var extraSection = document.Manifest with
        {
            Sections = new Dictionary<string, KnowledgePackSection>(document.Manifest.Sections)
            {
                ["script"] = new("0.1.0", "setup.ps1", Hash),
            },
        };
        Assert.Throws<InvalidDataException>(() => KnowledgePackValidator.Import(document with { Manifest = extraSection }, sections));

        var unsafePathSections = new Dictionary<string, KnowledgePackSection>(document.Manifest.Sections)
        {
            ["states"] = new("0.1.0", "../states.json", Hash),
        };
        Assert.Throws<InvalidDataException>(() => KnowledgePackValidator.Import(document with
        {
            Manifest = document.Manifest with { Sections = unsafePathSections },
        }, sections));
    }

    [Fact]
    public void Import_rejects_state_and_screen_graph_that_do_not_share_one_stable_state_id_set()
    {
        var (document, sections) = ValidPack();
        var graph = document.ScreenGraph with
        {
            Nodes = new[] { new ScreenGraphNode("0.1.0", "state.other", ScreenGraphVerificationState.Candidate) },
        };

        Assert.Throws<InvalidDataException>(() => KnowledgePackValidator.Import(document with { ScreenGraph = graph }, sections));
    }

    [Fact]
    public void Import_rejects_unknown_trust_value()
    {
        var (document, sections) = ValidPack();
        var manifest = document.Manifest with { Trust = (KnowledgePackTrust)99 };

        Assert.Throws<InvalidDataException>(() => KnowledgePackValidator.Import(document with { Manifest = manifest }, sections));
    }

    [Fact]
    public void Import_rejects_section_content_with_mismatched_hash()
    {
        var (document, sections) = ValidPack();
        var mismatched = new Dictionary<string, byte[]>(sections)
        {
            ["states.json"] = Encoding.UTF8.GetBytes("different"),
        };

        Assert.Throws<InvalidDataException>(() => KnowledgePackValidator.Import(document, mismatched));
    }

    [Fact]
    public void Manifest_fixture_deserializes_with_the_closed_contract_shape()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            Converters = { new JsonStringEnumConverter() },
        };
        var manifest = JsonSerializer.Deserialize<KnowledgePackManifest>(File.ReadAllText(FixturePath("knowledge-pack-manifest.sample.json")), options)
            ?? throw new InvalidDataException("Knowledge Pack manifest fixture is null.");
        Assert.Equal("pack.gamelab-prototype", manifest.PackId);
        Assert.All(manifest.Sections.Values, section => Assert.Equal("0.1.0", section.SchemaVersion));
    }

    private const string Hash = "0000000000000000000000000000000000000000000000000000000000000000";

    private static (KnowledgePackDocument Document, IReadOnlyDictionary<string, byte[]> Sections) ValidPack()
    {
        var content = Encoding.UTF8.GetBytes("{}");
        var sectionHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content));
        var sections = new Dictionary<string, KnowledgePackSection>
        {
            ["application-identities"] = new("0.1.0", "application-identities.json", sectionHash),
            ["semantic-actions"] = new("0.1.0", "semantic-actions.json", sectionHash),
            ["states"] = new("0.1.0", "states.json", sectionHash),
            ["recognizers"] = new("0.1.0", "recognizers.json", sectionHash),
            ["visual-targets"] = new("0.1.0", "visual-targets.json", sectionHash),
            ["screen-graph"] = new("0.1.0", "screen-graph.json", sectionHash),
            ["playbooks"] = new("0.1.0", "playbooks/", sectionHash),
            ["fixtures"] = new("0.1.0", "fixtures/", sectionHash),
            ["policy-record"] = new("0.1.0", "policy-record.json", sectionHash),
            ["migrations"] = new("0.1.0", "migrations/", sectionHash),
        };
        var manifest = new KnowledgePackManifest(
            "0.1.0",
            "pack.gamelab-prototype",
            "0.1.0",
            new KnowledgePackGame("0.1.0", "GameLab Prototype", "gamelab-2026.08", "ja-JP"),
            new[] { new SupportedEnvironment("0.1.0", "gamelab-2026.08", "ja-JP", 1, "1920x1080", "windowed", 96, false, CaptureBackend.WindowsGraphicsCapture, "SendInput", "0.1.0", "gamelab-recognizers 0.1.0") },
            sections,
            new KnowledgePackProvenance("0.1.0", "OpenLogicool", DateTimeOffset.UnixEpoch, "first-party", "internal-prototype"),
            KnowledgePackTrust.Untrusted,
            Array.Empty<KnowledgePackMigration>());
        var state = new KnowledgePackState("0.1.0", "state.main-menu", new[] { "anchor.menu" }, new[] { "success.idle" }, new[] { "action.start" });
        var graph = new ScreenGraph(
            "0.1.0",
            "0.1.0",
            new[] { new ScreenGraphNode("0.1.0", state.StateId, ScreenGraphVerificationState.Candidate) },
            new[] { new ScreenGraphEdge("0.1.0", state.StateId, state.StateId, "target.start", "action.start", ScreenGraphVerificationState.Candidate) },
            "gamelab-2026.08/ja-JP/1920x1080");
        var document = new KnowledgePackDocument(manifest, new[] { state }, graph);
        var contentByPath = manifest.Sections.Values.ToDictionary(section => section.Path, _ => content, StringComparer.Ordinal);
        return (document, contentByPath);
    }

    private static string FixturePath(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "fixtures", "contracts", fileName);
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new DirectoryNotFoundException($"fixture '{fileName}' を含む repository root を特定できません。");
    }
}
