# t05 Game OperatorマクロUI

- 既存`GameOperatorWindow`のTabControlへ「マクロ」tabを一つ追加した。別app／別windowは作っていない。
- 利用者goal入力、対象app選択、AI作成、保存済みmacro一覧、AI監視あり／なし再生、停止、複数macroの追加・削除・上下移動・統合保存を同じtabで操作する。
- UIは`IMacroAutomationIntents`だけを呼び、AI・capture・SQLite・入力deviceへ直接到達しない。
- focused test: `MacroAutomationWorkspaceTests` 2件 green（2026-08-26）。
