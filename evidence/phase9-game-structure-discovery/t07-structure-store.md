# t07 Game Structure Store 実装・検証記録

取得日時: 2026-08-24
対象: Phase 9A / `t07-structure-store`
状態: **成立**

## 結論

Game Structureの正本をappend-only SQLite event chainとし、Screen Graph、Game State Fact、dispatch状態、Knowledge Pack exportをevent replayだけから再構成する経路を実装した。

- migration 009で`structure_events`を追加した。公開APIはAppend／Read／List／LoadRevision／Exportだけで、UPDATE／DELETEを持たない。
- eventはgame、environment、sequence、親revision、結果revision、actor、correlation／causation、Observation／proposal／Attempt、evidence、payload、outcomeを保持する。
- 結果revisionは親revision・sequence・event IDからSHA-256で決定し、optimistic parent一致をAppend時に要求する。
- replayはsequence、親revision、結果revision、game、environment、schemaを再検証する。欠落・改変・別environment混入を黙って復元しない。
- Screen Graph nodeはstable ID、environment、scene signature、variant、evidence、仮label、verification、作成／更新revision、retire状態を持つ。
- edgeはsource／destination仮説、frame-bound affordance／locator revision、primitive、guard、risk、reversibility、before／after Observation、wait条件、outcome分布、evidence、verificationを持つ。
- Factはextractor、evidence、confidence、validity／reset scopeに加え、environment、作成／更新revision、retire状態を持つ。
- 未解決`DispatchArmed`は再起動replay後も`OutcomeUnknown`として残り、対応する`OutcomeRecorded`だけが解決する。armedなしoutcome、二重armed、correlation不一致は拒否する。

## 訂正と反証

mutationはnode／edge／fact追加、label変更、merge、split、edge再帰属、retire、verification変更、contradictionを表す。

- mergeは旧nodeを削除せずRetiredにし、scene signature／evidenceを統合先へ残し、関連edgeを再帰属してCandidateへ降格する。
- splitは旧nodeをRetiredとして残し、分割先へ旧identityと訂正evidenceをvariant関係として残す。
- node retireは依存edgeもRetiredにし、反証contradictionはverified subjectと依存edgeをCandidateへ降格する。
- `CorrectionApplied`はactor=userだけを受理する。user訂正によるupsertはVerifiedへ昇格せずCandidateになり、verification変更も昇格方向を許さない。
- 旧eventと旧evidenceは一切変更せず、新revisionへ投影する。

## Schemaとexport

- event、mutation、projection、exportはschema `0.3.0`で固定した。
- 不正JSON、空mutation batch、未知schema、壊れたrevision chainは明示エラーにする。
- Knowledge Pack exportは、指定revision時点のimmutable projectionと完全なevent historyを同じversioned documentへ格納する。
- process再open後に同じevent chainから同じrevision IDとprojectionを再構成できることをSQLite実ファイルで確認した。

## focused test

- `OpenLogicool.Domain.Tests`: 100件 green
- `OpenLogicool.Persistence.Tests`: 47件 green
- `OpenLogicool.Conformance.Tests`: 57件 green
- `OpenLogicool.Architecture.Tests`: 7件 green
- `git diff --check`: 違反0（既存の改行変換警告だけ）
- `structure_events`へのUPDATE／DELETE、game固有語、TODO／FIXME: 0件

新規試験はrevision chain、SQLite再open、append競合、event重複、crash replay、OutcomeUnknown、export全履歴、append-only公開API、user訂正の非昇格、merge／split、edge再帰属、contradiction降格、壊れたchain、armedなしoutcomeを確認した。

## 次工程との境界

t07は永続化とpure replayを所有する。AIの`StructureDeltaProposal`をschema／identity／policy／evidenceに照合してmutation eventへ変換する権限境界、risk判定、verification昇格条件はt08 Structure Knowledge Controllerが所有する。
