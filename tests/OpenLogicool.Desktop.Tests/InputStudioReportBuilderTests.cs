using OpenLogicool.Contracts.Devices.G13;
using OpenLogicool.Contracts.Devices.G600;
using OpenLogicool.Domain;
using Xunit;

namespace OpenLogicool.Desktop.Tests;

public sealed class InputStudioReportBuilderTests
{
    private static MappingProfile G600Profile() =>
        new(
            "profile-r1",
            "map-r1",
            defaultLayerId: "base",
            layerIds: ["base", "shift"],
            latchSelectors: new Dictionary<string, string>(),
            holdSelectors: new Dictionary<string, string> { ["G6"] = "shift" },
            bindings:
            [
                new MappingBinding("G9", "base", ["Key:F13"]),
                new MappingBinding("G9", "shift", ["Key:F14"]),
                new MappingBinding("G10", "base", ["Tap:Key:LCtrl+Key:C", "Tap:Key:V"]),
            ]);

    private static InputStudioReport Build(MappingProfile? g600Profile = null) =>
        InputStudioReportBuilder.Build(
            new DeviceDisplayInput("G13", 1, null, null),
            new DeviceDisplayInput("G600", 1, g600Profile is null ? null : "g600-main", g600Profile));

    [Fact]
    public void All_catalog_controls_appear_without_omission()
    {
        var report = Build();

        var g13 = Assert.Single(report.Devices, device => device.DeviceKind == "G13");
        Assert.Equal(G13Controls.Buttons, g13.ControlRows.Select(row => row.ControlId));

        var g600 = Assert.Single(report.Devices, device => device.DeviceKind == "G600");
        Assert.Equal(G600Controls.Buttons, g600.ControlRows.Select(row => row.ControlId));
    }

    [Fact]
    public void Capability_reflects_measured_evidence_only()
    {
        var report = Build();

        var g13Rows = report.Devices.Single(device => device.DeviceKind == "G13").ControlRows;
        Assert.Equal("Supported", g13Rows.Single(row => row.ControlId == "G20").Capability);
        Assert.Equal("確認済み", g13Rows.Single(row => row.ControlId == "STICK_PRESS").Evidence);
        Assert.Equal("Experimental", g13Rows.Single(row => row.ControlId == "M1").Capability);
        Assert.Equal("強い推定", g13Rows.Single(row => row.ControlId == "G5").Evidence);

        var g600Rows = report.Devices.Single(device => device.DeviceKind == "G600").ControlRows;
        Assert.All(g600Rows, row => Assert.Equal("Supported", row.Capability));
    }

    [Fact]
    public void Binding_conversion_is_shown_per_layer_with_default_layer_first()
    {
        var report = Build(G600Profile());
        var rows = report.Devices.Single(device => device.DeviceKind == "G600").ControlRows;

        Assert.Equal("base: Key:F13 ／ shift: Key:F14", rows.Single(row => row.ControlId == "G9").BindingSummary);
        Assert.Equal(
            "base: Tap:Key:LCtrl+Key:C, Tap:Key:V",
            rows.Single(row => row.ControlId == "G10").BindingSummary);
        Assert.Equal("未割当", rows.Single(row => row.ControlId == "G11").BindingSummary);
    }

    [Fact]
    public void Layer_selector_role_is_shown_instead_of_binding()
    {
        var report = Build(G600Profile());
        var g6 = report.Devices.Single(device => device.DeviceKind == "G600")
            .ControlRows.Single(row => row.ControlId == "G6");

        Assert.Equal("layer selector（hold → shift）", g6.Role);
        Assert.Equal("—", g6.BindingSummary);
    }

    [Fact]
    public void Missing_profile_is_stated_not_defaulted()
    {
        var report = Build();
        var g600 = report.Devices.Single(device => device.DeviceKind == "G600");

        Assert.Contains("profile 未設定", g600.ProfileSummary);
        Assert.All(g600.ControlRows, row => Assert.Equal("—", row.BindingSummary));
    }

    [Fact]
    public void G600_constraints_state_route_slots_f6_and_elevated()
    {
        var constraints = Build().Devices.Single(device => device.DeviceKind == "G600").Constraints;

        Assert.Contains(constraints, constraint => constraint.Contains("B変種"));
        Assert.Contains(constraints, constraint => constraint.Contains("3 slot"));
        Assert.Contains(constraints, constraint => constraint.Contains("F6"));
        Assert.Contains(constraints, constraint => constraint.Contains("elevated"));
    }

    [Fact]
    public void G13_report_states_native_lcd_support_without_claiming_lgs_applet_parity()
    {
        var report = Build();
        var g13 = report.Devices.Single(device => device.DeviceKind == "G13");

        Assert.Contains("native LCD write", g13.OwnershipSummary);
        Assert.Contains(g13.Constraints, constraint =>
            constraint.Contains("app-first") && constraint.Contains("実機確認済み"));
        Assert.Contains(g13.Constraints, constraint => constraint.Contains("LGS LCD applet互換"));
        Assert.DoesNotContain(g13.Constraints, constraint => constraint.Contains("LCD 出力") && constraint.Contains("未実装"));
    }

    [Fact]
    public void Environment_note_matches_companion_process_detection_capability()
    {
        var report = Build();

        Assert.Contains(report.EnvironmentNotes, note =>
            note.Contains("LGS") && note.Contains("実行中processは検出する"));
        Assert.DoesNotContain(report.EnvironmentNotes, note => note.Contains("検出照合は未実装"));
    }
}
