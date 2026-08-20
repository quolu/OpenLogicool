# t10 Failure UX 証跡

- capture fault、認識不能、絶対座標だけの操作、未確認対応を別の利用者向け message にした。capture 失敗を別 backend へ黙って fallback しない。
- **確認済み**: focused test で遮蔽 fault、Ambiguous／Unknown／Unavailable、absolute-only 操作、Unverified 表示を確認する。
- **未確認**: 実 game での失敗 UI。一般対応とは表示しない。

| コマンド | 結果 |
| --- | --- |
| `dotnet test tests/OpenLogicool.GameLab.Tests/OpenLogicool.GameLab.Tests.csproj` | 23/23 green |
