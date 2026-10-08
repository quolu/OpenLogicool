using System.IO;
using System.Text.Json;
using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class VisualAssistReviewNotifierTests
{
    [Fact]
    public void Bonus_notification_offers_the_three_screen_choices_and_stop()
    {
        using var result = JsonDocument.Parse("""{"ReviewOptions":[{"Id":"choice-1","Label":"DEX成長6段階"},{"Id":"choice-2","Label":"LUCK成長8段階"},{"Id":"choice-3","Label":"WILL成長1段階"}]}""");
        var options = VisualAssistReviewNotifier.ReviewOptions(result.RootElement);
        Assert.Equal(new[] { "choice-1", "choice-2", "choice-3", "stop" }, options.Select(option => option.Id));
        Assert.Equal("LUCK成長8段階", options[1].Label);
    }

    [Fact]
    public void Approval_preflight_is_a_required_handshake_despite_the_MCP_error_marker()
    {
        using var wire = JsonDocument.Parse("""{"isError":true,"structuredContent":{"error":"confirm_required","check_token":"札","decisions":[]}}""");
        var result = VisualAssistReviewNotifier.ReadToolResult(wire.RootElement);
        Assert.Equal("札", result.GetProperty("check_token").GetString());
    }

    [Fact]
    public void Duplicate_and_network_errors_do_not_become_successful_notifications()
    {
        using var wire = JsonDocument.Parse("""{"isError":true,"structuredContent":{"error":"duplicate_suspected","existing":[{"decision_id":"過去の申請"}]}}""");
        Assert.Throws<IOException>(() => VisualAssistReviewNotifier.ReadToolResult(wire.RootElement));
    }

    [Fact]
    public void Human_readable_tool_text_cannot_override_the_structured_receipt()
    {
        using var wire = JsonDocument.Parse("""{"content":[{"type":"text","text":"{\"decision_id\":\"誤ったID\"}"}],"structuredContent":{"decision_id":"正しいID"}}""");
        Assert.Equal("正しいID", VisualAssistReviewNotifier.ReadToolResult(wire.RootElement).GetProperty("decision_id").GetString());
    }
}
