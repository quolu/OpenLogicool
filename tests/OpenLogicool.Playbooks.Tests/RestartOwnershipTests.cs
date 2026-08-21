using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class RestartOwnershipTests
{
    [Fact]
    public void Host_restart_blocks_next_dispatch_until_ownership_is_reconciled()
    {
        var ownership = RestartOwnership.AfterHostRestart();

        Assert.Equal(RestartOwnershipState.PendingReconciliation, ownership.State);
        Assert.False(ownership.CanDispatch);
        Assert.Throws<InvalidOperationException>(ownership.RequireDispatchAllowed);
    }

    [Fact]
    public void Missing_release_confirmation_does_not_unlock_dispatch()
    {
        var ownership = RestartOwnership.AfterHostRestart();

        Assert.Throws<InvalidOperationException>(() => ownership.CompleteReconciliation(priorOutputReleaseConfirmed: false));
        Assert.Equal(RestartOwnershipState.PendingReconciliation, ownership.State);
        Assert.False(ownership.CanDispatch);
    }

    [Fact]
    public void Confirmed_release_unlocks_dispatch_after_reconciliation()
    {
        var ownership = RestartOwnership.AfterHostRestart();

        ownership.CompleteReconciliation(priorOutputReleaseConfirmed: true);

        Assert.Equal(RestartOwnershipState.Reconciled, ownership.State);
        Assert.True(ownership.CanDispatch);
        ownership.RequireDispatchAllowed();
    }
}
