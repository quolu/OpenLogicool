using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class DemonstrationSessionValidatorTests
{
    private const string Schema = ContractSchemaVersions.Revision03;
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch.AddHours(1);

    [Fact]
    public void Click_operation_bound_to_its_own_before_observation_is_accepted()
    {
        var draft = OperationDraft(DemonstrationTestData.Click());

        DemonstrationSessionValidator.ValidateAppend(DemonstrationTestData.Session(), [], draft);
    }

    [Fact]
    public void Operation_whose_before_observation_differs_from_its_binding_is_rejected()
    {
        var click = DemonstrationTestData.Click();
        var mismatched = click with
        {
            Target = click.Target with { ObservationId = "obs-later" },
        };

        var error = Assert.Throws<ArgumentException>(() =>
            DemonstrationSessionValidator.ValidateAppend(
                DemonstrationTestData.Session(), [], OperationDraft(mismatched)));
        Assert.Contains("操作束縛のObservation", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Operation_bound_to_a_stale_frame_of_the_same_observation_is_rejected()
    {
        var click = DemonstrationTestData.Click();
        var stale = click with
        {
            Target = click.Target with { FrameSequence = click.Target.FrameSequence - 1 },
        };

        var error = Assert.Throws<ArgumentException>(() =>
            DemonstrationSessionValidator.ValidateAppend(
                DemonstrationTestData.Session(), [], OperationDraft(stale)));
        Assert.Contains("frameが操作束縛", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    public void Pointer_coordinates_outside_the_normalized_client_frame_are_rejected(double x)
    {
        var click = DemonstrationTestData.Click();
        var outside = click with
        {
            Target = click.Target with { NormalizedPoint = [x, 0.5] },
        };

        var error = Assert.Throws<ArgumentException>(() =>
            DemonstrationSessionValidator.ValidateAppend(
                DemonstrationTestData.Session(), [], OperationDraft(outside)));
        Assert.Contains("正規化した0〜1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Key_tap_keeps_key_tokens_and_refuses_a_pointer_position()
    {
        var keyboardTap = DemonstrationTestData.KeyTap();
        DemonstrationSessionValidator.ValidateAppend(
            DemonstrationTestData.Session(), [], OperationDraft(keyboardTap));

        var withPointer = keyboardTap with
        {
            Target = keyboardTap.Target with { NormalizedPoint = [0.5, 0.5] },
        };
        var pointerError = Assert.Throws<ArgumentException>(() =>
            DemonstrationSessionValidator.ValidateAppend(
                DemonstrationTestData.Session(), [], OperationDraft(withPointer)));
        Assert.Contains("pointer座標は保存しません", pointerError.Message, StringComparison.Ordinal);

        var withoutTokens = keyboardTap with { KeyTokens = null };
        var tokenError = Assert.Throws<ArgumentException>(() =>
            DemonstrationSessionValidator.ValidateAppend(
                DemonstrationTestData.Session(), [], OperationDraft(withoutTokens)));
        Assert.Contains("KeyTokensが必要", tokenError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Device_control_id_is_required_for_g13_and_forbidden_for_mouse()
    {
        var g13 = DemonstrationTestData.KeyTap() with
        {
            Source = DemonstrationInputSource.G13,
            DeviceControlId = "G13:G1",
        };
        DemonstrationSessionValidator.ValidateAppend(DemonstrationTestData.Session(), [], OperationDraft(g13));

        var g13WithoutControl = g13 with { DeviceControlId = null };
        Assert.Throws<ArgumentException>(() =>
            DemonstrationSessionValidator.ValidateAppend(
                DemonstrationTestData.Session(), [], OperationDraft(g13WithoutControl)));

        var mouseWithControl = DemonstrationTestData.Click() with { DeviceControlId = "G600:G9" };
        Assert.Throws<ArgumentException>(() =>
            DemonstrationSessionValidator.ValidateAppend(
                DemonstrationTestData.Session(), [], OperationDraft(mouseWithControl)));
    }

    [Fact]
    public void Keyboard_source_cannot_record_a_pointer_primitive()
    {
        var keyboardClick = DemonstrationTestData.Click() with { Source = DemonstrationInputSource.Keyboard };

        var error = Assert.Throws<ArgumentException>(() =>
            DemonstrationSessionValidator.ValidateAppend(
                DemonstrationTestData.Session(), [], OperationDraft(keyboardClick)));
        Assert.Contains("key操作だけを記録", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Scroll_and_drag_carry_only_their_own_parameters()
    {
        var scroll = DemonstrationTestData.Click() with
        {
            Operation = GameInteractionOperations.Scroll,
            VerticalScrollSteps = -3,
        };
        DemonstrationSessionValidator.ValidateAppend(DemonstrationTestData.Session(), [], OperationDraft(scroll));

        var idleScroll = scroll with { VerticalScrollSteps = 0 };
        Assert.Throws<ArgumentException>(() =>
            DemonstrationSessionValidator.ValidateAppend(
                DemonstrationTestData.Session(), [], OperationDraft(idleScroll)));

        var drag = DemonstrationTestData.Click() with
        {
            Operation = GameInteractionOperations.Drag,
            DragDestinationNormalized = [0.8, 0.2],
        };
        DemonstrationSessionValidator.ValidateAppend(DemonstrationTestData.Session(), [], OperationDraft(drag));

        var dragWithScroll = drag with { HorizontalScrollSteps = 1 };
        Assert.Throws<ArgumentException>(() =>
            DemonstrationSessionValidator.ValidateAppend(
                DemonstrationTestData.Session(), [], OperationDraft(dragWithScroll)));
    }

    [Fact]
    public void Comparison_must_point_at_the_recorded_before_and_stable_after_observations()
    {
        var click = DemonstrationTestData.Click();
        var wrongAfter = click with
        {
            Comparison = click.Comparison with { AfterObservationId = "obs-other" },
        };
        Assert.Throws<ArgumentException>(() =>
            DemonstrationSessionValidator.ValidateAppend(
                DemonstrationTestData.Session(), [], OperationDraft(wrongAfter)));

        var timedOut = click with
        {
            After = click.After with
            {
                Status = GameInteractionStabilityStatus.TimedOut,
                StableScene = null,
            },
            Comparison = click.Comparison with
            {
                AfterObservationId = null,
                Judgement = GameTransitionJudgement.Undetermined,
            },
        };
        DemonstrationSessionValidator.ValidateAppend(DemonstrationTestData.Session(), [], OperationDraft(timedOut));
    }

    [Fact]
    public void Operations_are_refused_while_focus_is_lost_and_resume_needs_a_new_observation()
    {
        var session = DemonstrationTestData.Session();
        var focusLost = Stored(
            1,
            DemonstrationEventKind.FocusLost,
            focusChange: new DemonstrationFocusChange(Schema, @"C:\other\chat.exe", null, Start.AddSeconds(5)));

        var operationError = Assert.Throws<ArgumentException>(() =>
            DemonstrationSessionValidator.ValidateAppend(
                session, [focusLost], OperationDraft(DemonstrationTestData.Click(), Start.AddSeconds(6))));
        Assert.Contains("focus喪失中の操作は記録しません", operationError.Message, StringComparison.Ordinal);

        var resumeWithoutObservation = new DemonstrationEventDraft(
            Schema,
            session.SessionId,
            DemonstrationEventKind.FocusRegained,
            Start.AddSeconds(7),
            FocusChange: new DemonstrationFocusChange(Schema, session.TargetApplicationPath, null, Start.AddSeconds(7)));
        var resumeError = Assert.Throws<ArgumentException>(() =>
            DemonstrationSessionValidator.ValidateAppend(session, [focusLost], resumeWithoutObservation));
        Assert.Contains("新しいObservationから再開", resumeError.Message, StringComparison.Ordinal);

        var resume = resumeWithoutObservation with
        {
            FocusChange = new DemonstrationFocusChange(
                Schema, session.TargetApplicationPath, "obs-resumed", Start.AddSeconds(7)),
        };
        DemonstrationSessionValidator.ValidateAppend(session, [focusLost], resume);

        var storedResume = Stored(2, DemonstrationEventKind.FocusRegained, focusChange: resume.FocusChange);
        DemonstrationSessionValidator.ValidateAppend(
            session,
            [focusLost, storedResume],
            OperationDraft(DemonstrationTestData.Click(), Start.AddSeconds(8)));
    }

    [Fact]
    public void Focus_regained_must_return_to_the_recorded_target_application()
    {
        var session = DemonstrationTestData.Session();
        var focusLost = Stored(
            1,
            DemonstrationEventKind.FocusLost,
            focusChange: new DemonstrationFocusChange(Schema, @"C:\other\chat.exe", null, Start.AddSeconds(5)));
        var wrongResume = new DemonstrationEventDraft(
            Schema,
            session.SessionId,
            DemonstrationEventKind.FocusRegained,
            Start.AddSeconds(6),
            FocusChange: new DemonstrationFocusChange(Schema, @"C:\other\chat.exe", "obs-resumed", Start.AddSeconds(6)));

        var error = Assert.Throws<ArgumentException>(() =>
            DemonstrationSessionValidator.ValidateAppend(session, [focusLost], wrongResume));
        Assert.Contains("focus復帰先が対象app", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stopped_original_refuses_further_appends()
    {
        var session = DemonstrationTestData.Session();
        var stopped = Stored(
            1,
            DemonstrationEventKind.Stopped,
            stop: new DemonstrationStop(Schema, "利用者が停止", Start.AddSeconds(30)));

        var error = Assert.Throws<ArgumentException>(() =>
            DemonstrationSessionValidator.ValidateAppend(
                session, [stopped], OperationDraft(DemonstrationTestData.Click(), Start.AddSeconds(31))));
        Assert.Contains("停止済み", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Events_must_not_go_backwards_in_time_and_carry_exactly_one_payload()
    {
        var session = DemonstrationTestData.Session();
        var first = Stored(
            1,
            DemonstrationEventKind.Operation,
            operation: DemonstrationTestData.Click());

        Assert.Throws<ArgumentException>(() =>
            DemonstrationSessionValidator.ValidateAppend(
                session, [first], OperationDraft(DemonstrationTestData.Click(), Start.AddSeconds(-1))));

        var twoPayloads = new DemonstrationEventDraft(
            Schema,
            session.SessionId,
            DemonstrationEventKind.Operation,
            Start.AddSeconds(9),
            Operation: DemonstrationTestData.Click(),
            Stop: new DemonstrationStop(Schema, "停止", Start.AddSeconds(9)));
        var payloadError = Assert.Throws<ArgumentException>(() =>
            DemonstrationSessionValidator.ValidateAppend(session, [], twoPayloads));
        Assert.Contains("ちょうど一つ", payloadError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_schema_versions_are_refused_for_the_session_and_for_events()
    {
        var session = DemonstrationTestData.Session() with { SchemaVersion = ContractSchemaVersions.Revision02 };
        Assert.Throws<ArgumentException>(() => DemonstrationSessionValidator.ValidateSession(session));

        var draft = OperationDraft(DemonstrationTestData.Click()) with
        {
            SchemaVersion = ContractSchemaVersions.Revision01,
        };
        Assert.Throws<ArgumentException>(() =>
            DemonstrationSessionValidator.ValidateAppend(DemonstrationTestData.Session(), [], draft));
    }

    [Fact]
    public void Operations_outside_the_recorded_target_window_are_refused()
    {
        var click = DemonstrationTestData.Click();
        var otherWindow = click with
        {
            Target = click.Target with { TargetWindowSourceId = "window-other" },
        };

        var error = Assert.Throws<ArgumentException>(() =>
            DemonstrationSessionValidator.ValidateAppend(
                DemonstrationTestData.Session(), [], OperationDraft(otherWindow)));
        Assert.Contains("対象windowと一致しません", error.Message, StringComparison.Ordinal);
    }

    private static DemonstrationEventDraft OperationDraft(
        DemonstrationOperation operation,
        DateTimeOffset? occurredUtc = null) =>
        new(
            Schema,
            DemonstrationTestData.Session().SessionId,
            DemonstrationEventKind.Operation,
            occurredUtc ?? operation.OccurredUtc,
            Operation: operation);

    private static DemonstrationEvent Stored(
        long sequence,
        DemonstrationEventKind kind,
        DemonstrationOperation? operation = null,
        DemonstrationFocusChange? focusChange = null,
        DemonstrationStop? stop = null)
    {
        var occurredUtc = operation?.OccurredUtc ?? focusChange?.OccurredUtc ?? stop?.OccurredUtc ?? Start;
        return new DemonstrationEvent(
            Schema,
            DemonstrationTestData.Session().SessionId,
            sequence,
            $"demo-event:{sequence}",
            sequence == 1 ? null : $"demo:{sequence - 1}",
            $"demo:{sequence}",
            kind,
            occurredUtc,
            occurredUtc,
            operation,
            focusChange,
            stop);
    }
}

/// <summary>操作デモ原本のfocused testが共有する最小の実データ。</summary>
internal static class DemonstrationTestData
{
    private const string Schema = ContractSchemaVersions.Revision03;
    public static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch.AddHours(1);

    public static DemonstrationSessionDraft Session() =>
        new(
            Schema,
            "demo-session-1",
            "nikke",
            "env-1",
            "アークを開く",
            @"C:\games\nikke\nikke.exe",
            "window-nikke",
            "windows-demonstration-recorder-v1",
            Start);

    public static DemonstrationOperation Click() =>
        new(
            Schema,
            "demo-op-1",
            GameInteractionOperations.Click,
            DemonstrationInputSource.Mouse,
            new DemonstrationFrameBinding(Schema, "obs-before", 41, 7, "window-nikke", [0.534, 0.628]),
            Scene("obs-before", 41, 7),
            Stability(Scene("obs-after", 96, 7)),
            new GameTransitionComparison(
                Schema,
                "obs-before",
                "obs-after",
                GameTransitionJudgement.Moved,
                [],
                ["意味構造が変化した"]),
            "evidence-1",
            1_000,
            11_059,
            Start.AddSeconds(3));

    public static DemonstrationOperation KeyTap() =>
        new(
            Schema,
            "demo-op-2",
            GameInteractionOperations.KeyTap,
            DemonstrationInputSource.Keyboard,
            new DemonstrationFrameBinding(Schema, "obs-before", 41, 7, "window-nikke", null),
            Scene("obs-before", 41, 7),
            Stability(Scene("obs-after", 96, 7)),
            new GameTransitionComparison(
                Schema,
                "obs-before",
                "obs-after",
                GameTransitionJudgement.Moved,
                [],
                ["意味構造が変化した"]),
            "evidence-2",
            2_000,
            12_020,
            Start.AddSeconds(4),
            KeyTokens: ["Escape"]);

    public static ObservedScene Scene(string observationId, long frameSequence, long transformRevision) =>
        new(
            Schema,
            $"scene-{observationId}",
            observationId,
            new CapturedFrameReference(
                Schema,
                "window-nikke",
                CaptureBackend.WindowsGraphicsCapture,
                frameSequence,
                frameSequence * 16.0,
                Start.AddMilliseconds(frameSequence * 16),
                transformRevision,
                12,
                8),
            CaptureAvailability.Available,
            StateIdentityStatus.Novel,
            null,
            [],
            [],
            "local-target-tracking-v1");

    public static GameInteractionStabilityResult Stability(ObservedScene stable) =>
        new(
            Schema,
            GameInteractionStabilityStatus.Stable,
            [stable],
            stable,
            17,
            8_500,
            10_059,
            null);
}
