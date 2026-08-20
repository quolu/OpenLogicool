# phase5-capture-perception 終端監査（terminal audit）

- 実施: 2026-08-20（統括 bell-grok46）
- Exit 宣言: 2026-08-20 親が **Phase 5 Exit 未成立** を宣言。オーナー承認待ちで止めない。

## 受入条件の再確認

1. **development-plan §Phase 5 Exit が4値で揃っている**: 成立（文書）。判定の中身は未成立を含む——[docs/phase5-exit-assessment.md](../../docs/phase5-exit-assessment.md)。
2. **recorded／live 同一 conformance**: **未成立**。合成 frame の同一 `Observe()` と WGC 単体 frame はある。WGC 画素→Observation の recorded／live 結合は無い。
3. **事前固定 metric**: **未成立**。`CorpusPartition` の型分離だけ。eval runner も製品 recognizer も無い。
4. **backend／resize／stale で入力停止**: **未成立**。`CaptureContinuityGate` の bool は focused test で確認済み。Host／dispatch へ未配線。
5. **一つの実 game 成功を一般対応にしない**: **確認済み**。`GameOperatorFailureView`。
6. **実画面 UniqueMatch のときだけ resume**: **未成立**。`LiveResumeGate` はライブラリ。製品ループと recognizer が無い。
7. **各 ToDo の focused test＋証跡＋着地**: 成立（t02 は accept 不能のため cherry-pick）。
8. **未成立の明記**: 成立。assessment が条件1・2・3・5を隠していない。

## full regression

`dotnet test OpenLogicool.sln` 1回（HEAD `e6e7e44`、Perception.Tests 登録後）: 18 project・591 件・失敗 0。

## 監査

親直読＋円卓外 `refuter`。重大3件（metric 不在、製品配線不在、recognizer 不在）を Exit 成立の反証として採用。黙った backend fallback は製品 Capture に無い。

## 判定

campaign の ToDo は閉じる。**Phase 5 Exit は未成立**。次 campaign は frozen metric と WGC→Observation→ContinuityGate→LiveResumeGate→dispatch の製品配線から。
