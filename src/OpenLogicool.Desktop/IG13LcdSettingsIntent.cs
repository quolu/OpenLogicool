using OpenLogicool.Contracts.Profiles;

namespace OpenLogicool.Desktop;

/// <summary>
/// 画像decode・font描画をDesktopから隔離するHost境界。結果はworkspaceへ保存できる960-byte frameとなる。
/// </summary>
public interface IG13LcdSettingsIntent
{
    WorkspaceG13LcdSetting FromImageFile(string imagePath);

    WorkspaceG13LcdSetting FromText(string text);
}
