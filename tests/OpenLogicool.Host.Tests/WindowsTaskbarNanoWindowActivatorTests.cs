using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class WindowsTaskbarNanoWindowActivatorTests
{
    [Fact]
    public void Same_title_launcher_is_excluded_even_when_it_is_closer_to_the_cursor()
    {
        // 実機で取得した同名ボタン。以前は距離によってランチャーが選ばれた。
        var buttons = new[]
        {
            (Name: "NIKKE - 1 個の実行中ウィンドウ", Id: @"Appid: C:\NIKKE\Launcher\nikke_launcher.exe", X: 741),
            (Name: "nikke - 1 個の実行中ウィンドウ", Id: @"Appid: C:\NIKKE\NIKKE\game\nikke.exe", X: 785),
        };

        var matching = buttons.Where(button => WindowsTaskbarNanoWindowActivator.MatchesExecutable(
            button.Id, @"C:\NIKKE\NIKKE\game\nikke.exe")).ToArray();

        Assert.Equal(785, Assert.Single(matching).X);
    }

    [Fact]
    public void Executable_matching_preserves_windows_path_case_insensitivity()
    {
        Assert.True(WindowsTaskbarNanoWindowActivator.MatchesExecutable(
            @"Appid: c:\nikke\nikke\game\NIKKE.EXE", @"C:\NIKKE\NIKKE\game\nikke.exe"));
    }

    [Fact]
    public void Unknown_app_identity_is_not_selected_by_its_display_name()
    {
        Assert.False(WindowsTaskbarNanoWindowActivator.MatchesExecutable(
            "Appid: unknown-app", @"C:\NIKKE\NIKKE\game\nikke.exe"));
    }
}
