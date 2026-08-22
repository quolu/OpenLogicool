using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Desktop;
using OpenLogicool.Devices.G600;
using OpenLogicool.Profiles;

namespace OpenLogicool.Host;

/// <summary>
/// <see cref="IG600OnboardIntent"/> の実装。書き込み本体は <see cref="G600OnboardService"/>、
/// 常駐同居時（<c>ui --resident</c>）は書き込み成立後に G600 の SendInput 送出を live で抑止し
/// （二重入力防止）、解除後は送出を戻して残置（leftover）を再適用する。
/// 常駐が別 process の場合は送出抑止を同期できないため書き込みを拒否する。
/// </summary>
public sealed class HostG600OnboardIntent(
    G600OnboardService service,
    ResidentInputHost? residentHost,
    G600LeftoverSession? leftover) : IG600OnboardIntent
{
    public G600OnboardUiState QueryState()
    {
        var mode = service.CurrentMode();
        return mode is null
            ? new G600OnboardUiState(Active: false, "G600 本体: 書き込みなし")
            : new G600OnboardUiState(Active: true, $"G600 本体: 「{mode.WorkspaceId}」の割当を書き込み中");
    }

    public G600OnboardUiResult Apply(WorkspaceDocument document)
    {
        if (residentHost?.OutputRoute == ResidentOutputRoute.SerialHid)
        {
            return new(false, "Serial HID出力中はG600本体書き込みを同時に使えません。Serial HIDを停止してから実行してください。");
        }

        if (residentHost is null && OtherResidentRunning())
        {
            return new(false, "別の常駐が動作中のため書き込めません。常駐を終了してから実行してください。");
        }

        var compilation = WorkspaceCompiler.Compile(document);
        var g600Document = compilation.Profiles.FirstOrDefault(profile => profile.DeviceKind == "G600");
        if (g600Document is null)
        {
            return new(false, "この設定に G600 の割当がないため書き込みません。");
        }

        var result = service.Apply(document.WorkspaceId, g600Document);
        if (result.Success)
        {
            residentHost?.EnterG600OnboardSuppression();
        }

        return new(result.Success, result.Message);
    }

    public G600OnboardUiResult Restore()
    {
        if (residentHost is null && OtherResidentRunning())
        {
            return new(false, "別の常駐が動作中のため戻せません。常駐を終了してから実行してください。");
        }

        var result = service.Restore();
        if (!result.Success)
        {
            return new(false, result.Message);
        }

        var message = result.Message;
        if (residentHost is not null)
        {
            // 送出を戻し、出荷割当の残置無害化（B変種）を再適用して通常運用へ復帰する。
            residentHost.ExitG600OnboardSuppression();
            if (leftover is not null)
            {
                var leftoverResult = leftover.Apply(managed: true);
                message += $"\n{G600LeftoverHostSupport.Describe(leftoverResult)}";
            }
        }

        return new(true, message);
    }

    private static bool OtherResidentRunning()
    {
        if (!Mutex.TryOpenExisting(SingleInstanceGuard.DefaultName, out var mutex))
        {
            return false;
        }

        mutex.Dispose();
        return true;
    }
}
