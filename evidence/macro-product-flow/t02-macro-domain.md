# t02 macro token・catalog・合成契約

- macro正本は既存Learning Route revisionのまま。新schema／新DBは追加していない。
- `MacroInvocationTokens`はroute ID、exact versionまたはlatest追従、AI監視modeだけを保持する。
- latest追従tokenにより、AI修復後のappend-only新版をdevice bindingの再保存なしで実行できる。
- `MacroRouteComposer`は2件以上のCompiled／Verified sourceを選択順に連結し、同一game／environmentとedge連続性を既存`LearningRouteValidator`で検証する。
- `HostMacroCatalog`はSQLiteの最新macro一覧、exact/latest解決、合成route appendを既存store上で提供する。
- source macro revisionは変更しない。

## focused

- `MacroInvocationTokensTests`／`MacroRouteComposerTests`: 10件green。
- `HostMacroCatalogTests`: 1件green。最新version一覧、合成、source履歴保持、SQLite復元を確認。
