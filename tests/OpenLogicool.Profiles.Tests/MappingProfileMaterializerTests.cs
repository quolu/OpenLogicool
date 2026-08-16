using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Profiles;
using Xunit;

namespace OpenLogicool.Profiles.Tests;

public sealed class MappingProfileMaterializerTests
{
    private static MappingProfileDocument Document() =>
        new(
            ContractSchemaVersions.Revision01,
            ProfileId: "profile-1",
            DeviceKind: "G600",
            ProfileRevision: "rev-1",
            MappingRevision: "map-1",
            DefaultLayerId: "normal",
            LayerIds: ["normal", "shift"],
            LatchSelectors: [],
            HoldSelectors: [new LayerSelectorEntry("G6", "shift")],
            Bindings:
            [
                new MappingBindingEntry("G9", "normal", ["Key:F13"]),
                new MappingBindingEntry("G9", "shift", ["Key:F24"]),
            ]);

    [Fact]
    public void Valid_document_materializes_into_a_resolvable_profile()
    {
        var profile = MappingProfileMaterializer.ToProfile(Document());

        Assert.Equal("rev-1", profile.ProfileRevision);
        Assert.Equal("normal", profile.DefaultLayerId);
        Assert.Equal("shift", profile.HoldSelectors["G6"]);
        Assert.True(profile.TryResolve("G9", "normal", out var normalOutputs));
        Assert.Equal(["Key:F13"], normalOutputs);
        Assert.True(profile.TryResolve("G9", "shift", out var shiftOutputs));
        Assert.Equal(["Key:F24"], shiftOutputs);
    }

    [Fact]
    public void Unknown_schema_version_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            MappingProfileMaterializer.ToProfile(Document() with { SchemaVersion = "9.9.9" }));
    }

    [Fact]
    public void Duplicate_selector_control_is_rejected()
    {
        var document = Document() with
        {
            HoldSelectors = [new LayerSelectorEntry("G6", "shift"), new LayerSelectorEntry("G6", "normal")],
        };

        Assert.Throws<ArgumentException>(() => MappingProfileMaterializer.ToProfile(document));
    }

    [Fact]
    public void Invalid_content_is_rejected_by_the_domain_model()
    {
        var document = Document() with
        {
            Bindings = [new MappingBindingEntry("G9", "no-such-layer", ["Key:F13"])],
        };

        Assert.Throws<ArgumentException>(() => MappingProfileMaterializer.ToProfile(document));
    }
}
