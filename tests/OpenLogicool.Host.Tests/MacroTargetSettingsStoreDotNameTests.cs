using System.IO;
using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

/// <summary>t07: ドットを含むprocess名が保存で別名にならないこと。</summary>
public sealed class MacroTargetSettingsStoreDotNameTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"openlogicool-target-{Guid.NewGuid():N}");

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Theory]
    [InlineData("OpenLogicool.Probe", "OpenLogicool.Probe")]
    [InlineData("Some.Game", "Some.Game")]
    [InlineData("nikke", "nikke")]
    [InlineData("nikke.exe", "nikke")]
    [InlineData("NIKKE.EXE", "NIKKE")]
    [InlineData(@"C:\games\Some.Game.exe", "Some.Game")]
    public void A_process_name_keeps_its_dots_and_loses_only_a_trailing_exe(string input, string expected)
    {
        var store = new MacroTargetSettingsStore(directory);

        Assert.Equal(expected, store.Save(input).ProcessName);
        Assert.Equal(expected, store.Load()!.ProcessName);
    }
}
