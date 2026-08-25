# t01 目的run契約とknown-first phase分離

判定日: 2026-08-25

- Structure edgeのtarget semantic keyとprimitiveを保存actionへ類似照合するroute hintを追加した。
- goal文字列と中間action名が似ていなくても、保存routeが指定したactionをAI 0で選べる。
- `WaitStable`／`Compare`中はlocal OCR／保存profileだけを観測し、次step用AI discoveryを呼ばない。
- 保存actionの10秒非遷移後は、`Moved`が成立するまで古い保存actionへ戻らずAI修復側を維持する。
- OCR完全一致、destination ID、locator revision、verification、ReviewStatus、一手承認、復帰edge、反復回数をgateへ追加していない。
- focused test: `WindowsKnownFirstTargetDiscoveryTests`と`ProductGameExplorerRuntimeTests`、15件green。
- `git diff --check`: error 0。
