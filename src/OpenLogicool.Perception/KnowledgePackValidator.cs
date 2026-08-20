using OpenLogicool.Contracts.Perception;
using System.Security.Cryptography;

namespace OpenLogicool.Perception;

/// <summary>
/// Knowledge Pack の import 境界。pack は data だけを表し、信頼や verified 状態を持ち込めない。
/// </summary>
public static class KnowledgePackValidator
{
    private static readonly HashSet<string> RequiredSections = new(StringComparer.Ordinal)
    {
        "application-identities",
        "semantic-actions",
        "states",
        "recognizers",
        "visual-targets",
        "screen-graph",
        "playbooks",
        "fixtures",
        "policy-record",
        "migrations",
    };

    public static KnowledgePackDocument Import(
        KnowledgePackDocument document,
        IReadOnlyDictionary<string, byte[]> sectionContents)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateManifest(document.Manifest);
        ValidateStates(document.States);
        ValidateScreenGraph(document.States, document.ScreenGraph);
        ValidateSectionContents(document.Manifest, sectionContents);

        return document with
        {
            Manifest = document.Manifest with { Trust = KnowledgePackTrust.Untrusted },
            ScreenGraph = document.ScreenGraph with
            {
                Nodes = document.ScreenGraph.Nodes
                    .Select(node => node with { VerificationState = ScreenGraphVerificationState.Candidate })
                    .ToArray(),
                Edges = document.ScreenGraph.Edges
                    .Select(edge => edge with { VerificationState = ScreenGraphVerificationState.Candidate })
                    .ToArray(),
            },
        };
    }

    private static void ValidateManifest(KnowledgePackManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        Required(manifest.SchemaVersion, "manifest schemaVersion");
        Required(manifest.PackId, "packId");
        Required(manifest.PackVersion, "packVersion");
        ArgumentNullException.ThrowIfNull(manifest.Game);
        ArgumentNullException.ThrowIfNull(manifest.SupportedEnvironments);
        ArgumentNullException.ThrowIfNull(manifest.Sections);
        ArgumentNullException.ThrowIfNull(manifest.Provenance);
        Required(manifest.Game.SchemaVersion, "game schemaVersion");
        Required(manifest.Game.Name, "game name");
        Required(manifest.Game.Build, "game build");
        Required(manifest.Game.Locale, "game locale");

        if (manifest.Trust != KnowledgePackTrust.Untrusted)
        {
            throw new InvalidDataException("import する Knowledge Pack の trust は Untrusted だけです。");
        }

        if (manifest.SupportedEnvironments.Count == 0)
        {
            throw new InvalidDataException("Knowledge Pack には少なくとも一つの supported environment が必要です。");
        }

        foreach (var environment in manifest.SupportedEnvironments)
        {
            Required(environment.SchemaVersion, "supported environment schemaVersion");
            Required(environment.Build, "supported environment build");
            Required(environment.Locale, "supported environment locale");
            Required(environment.Resolution, "supported environment resolution");
            Required(environment.DisplayMode, "supported environment displayMode");
            Required(environment.InputRoute, "supported environment inputRoute");
            Required(environment.ScreenGraphVersion, "supported environment screenGraphVersion");
            Required(environment.RecognizerVersion, "supported environment recognizerVersion");
            if (environment.UiScale <= 0 || environment.Dpi <= 0)
            {
                throw new InvalidDataException("supported environment の UI scale と DPI は正でなければなりません。");
            }
        }

        if (!manifest.Sections.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(RequiredSections))
        {
            throw new InvalidDataException("Knowledge Pack sections は data-only の既定section集合と完全一致しなければなりません。");
        }

        foreach (var section in manifest.Sections)
        {
            Required(section.Value.SchemaVersion, $"section {section.Key} schemaVersion");
            ValidateRelativePath(section.Value.Path, section.Key);
            if (section.Value.Sha256.Length != 64 || !section.Value.Sha256.All(Uri.IsHexDigit))
            {
                throw new InvalidDataException($"section {section.Key} の SHA-256 は64桁の16進数でなければなりません。");
            }
        }

        Required(manifest.Provenance.SchemaVersion, "provenance schemaVersion");
        Required(manifest.Provenance.Author, "provenance author");
        Required(manifest.Provenance.Source, "provenance source");
        Required(manifest.Provenance.License, "provenance license");
    }

    private static void ValidateStates(IReadOnlyList<KnowledgePackState> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        if (states.Count == 0)
        {
            throw new InvalidDataException("Knowledge Pack には少なくとも一つの state が必要です。");
        }

        var stateIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var state in states)
        {
            Required(state.SchemaVersion, "state schemaVersion");
            Required(state.StateId, "stateId");
            if (!stateIds.Add(state.StateId))
            {
                throw new InvalidDataException($"stateId '{state.StateId}' が重複しています。");
            }

            ValidateReferences(state.AnchorRefs, state.StateId, "anchor");
            ValidateReferences(state.SuccessConditionRefs, state.StateId, "success condition");
            ValidateReferences(state.ActionRefs, state.StateId, "action");
        }
    }

    private static void ValidateSectionContents(
        KnowledgePackManifest manifest,
        IReadOnlyDictionary<string, byte[]> sectionContents)
    {
        ArgumentNullException.ThrowIfNull(sectionContents);
        var expectedPaths = manifest.Sections.Values.Select(section => section.Path).ToHashSet(StringComparer.Ordinal);
        if (!sectionContents.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(expectedPaths))
        {
            throw new InvalidDataException("Knowledge Pack section content は manifest の全section pathと完全一致しなければなりません。");
        }

        foreach (var section in manifest.Sections)
        {
            var content = sectionContents[section.Value.Path]
                ?? throw new InvalidDataException($"section {section.Key} の content がありません。");
            var actualHash = Convert.ToHexString(SHA256.HashData(content));
            if (!actualHash.Equals(section.Value.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"section {section.Key} の content hash が manifest と一致しません。");
            }
        }
    }

    private static void ValidateScreenGraph(IReadOnlyList<KnowledgePackState> states, ScreenGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(graph.Nodes);
        ArgumentNullException.ThrowIfNull(graph.Edges);
        Required(graph.SchemaVersion, "screen graph schemaVersion");
        Required(graph.GraphVersionId, "screen graph version");
        Required(graph.EnvironmentScope, "screen graph environment scope");

        var stateIds = states.Select(state => state.StateId).ToHashSet(StringComparer.Ordinal);
        var nodeIds = graph.Nodes.Select(node => node.StateId).ToHashSet(StringComparer.Ordinal);
        if (!nodeIds.SetEquals(stateIds) || nodeIds.Count != graph.Nodes.Count)
        {
            throw new InvalidDataException("Screen Graph の node stateId は Knowledge Pack states と一対一でなければなりません。");
        }

        foreach (var node in graph.Nodes)
        {
            Required(node.SchemaVersion, "screen graph node schemaVersion");
        }

        foreach (var edge in graph.Edges)
        {
            Required(edge.SchemaVersion, "screen graph edge schemaVersion");
            if (!stateIds.Contains(edge.FromStateId) || !stateIds.Contains(edge.ToStateId))
            {
                throw new InvalidDataException("Screen Graph edge は Knowledge Pack states に存在する stateId だけを参照できます。");
            }
        }
    }

    private static void ValidateReferences(IReadOnlyList<string> references, string stateId, string kind)
    {
        ArgumentNullException.ThrowIfNull(references);
        if (references.Count == 0 || references.Any(string.IsNullOrWhiteSpace) || references.Distinct(StringComparer.Ordinal).Count() != references.Count)
        {
            throw new InvalidDataException($"state '{stateId}' の {kind} reference は空・重複なしで少なくとも一つ必要です。");
        }
    }

    private static void ValidateRelativePath(string path, string section)
    {
        Required(path, $"section {section} path");
        if (Path.IsPathRooted(path) || path.Contains("..", StringComparison.Ordinal) || path.Contains('\\'))
        {
            throw new InvalidDataException($"section {section} の path は pack 内の安全な相対pathでなければなりません。");
        }
    }

    private static void Required(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"{field} は必須です。");
        }
    }
}
