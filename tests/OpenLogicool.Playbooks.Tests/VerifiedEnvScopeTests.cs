using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class VerifiedEnvScopeTests
{
    [Fact]
    public void GameLabでVerifiedになった根拠は同じGameLab環境だけへ適用する()
    {
        var scope = new VerifiedEnvScope("gamelab:scenario-01");

        Assert.True(scope.AppliesTo("gamelab:scenario-01"));
    }

    [Fact]
    public void GameLabでVerifiedになった根拠は実gameへ継承しない()
    {
        var scope = new VerifiedEnvScope("gamelab:scenario-01");

        Assert.False(scope.AppliesTo("game:nikke"));
    }
}
