# Phase 12 Supervised Visual Macro Runner

## 目的

Phase 11で生成できるVisual Macroを、利用者が各stepを視認できる教師付き実行へ接続する。実行は毎回、操作前画面の一致、Nano Serial HIDによる一回だけの入力、操作後画面の一致を順に記録し、期待と違う時はその場で停止する。

## 成功条件

1. 保存済みLearning RouteからVisual Macroを再構築し、実行対象の版をrun開始時に固定する。
2. 保存page／actionをcurrent frameへ再同定できないstepは入力を送らない。OCR完全一致、locator revision一致、verification labelは要求しない。
3. dispatch eventを永続化した後にだけNano Serial HIDへ一回送信し、失敗・timeout・結果不明を自動再送しない。
4. 10秒の意味比較が`Moved`ならdestination ID不一致でも次stepへ進む。`Stayed`／`Undetermined`は観測証拠を残し、AI再探索を許可する。
5. 利用者はアプリ上で現在step、操作、期待画面、前後監査、送信有無、停止理由、履歴を読める。
6. NIKKEの安全な可逆sliceを実ゲームで一巡し、SendInput 0、Computer Use input dispatch 0、fallback 0を証拠化する。
7. ダイヤ、希少資源、現金を消費するrisk tagはcompile時とrun時の両方で拒否する。

## 実行契約

- 実行対象は`VisualMacroProgram`の`ProgramId`、`RouteVersionId`、`StructureRevisionId`をrun開始時にpinする。
- 画面監査は10の基盤機能の`WaitStable`と`Compare`だけで決める。ACK、API戻り値、AI予測、destination ID完全一致を成功根拠にしない。
- 入力はNano Serial HIDだけを許可する。SendInputやComputer Use入力へのfallbackを持たない。
- 一つのAttemptが未解決の間は次のdispatchを作らない。dispatchし得た後の例外は`OutcomeUnknown`として停止する。
- UIの「次の一手」は一stepだけを進める。連打や二重commandは同じstepを二回送らない。
- 教師付きrunの修正はLearning Routeの新revisionとして行い、実行中programを黙って差し替えない。

## 非対象

- NIKKE全日課の無人完遂
- mismatch時のAI自動修復
- background長時間運転
- 外部AI API、課金API、cloud送信
- SendInput、onboard write、Computer Useによるゲーム入力
- Peertable、Dotagent、円卓インジケータの改修
- cross-platform CI、Windows以外の対応

## Lattice task仕様

### t01-run-contract

Supervised runの状態、step監査、停止理由、pin、表示snapshotをgame非依存contractとして定義する。`Confirmed`以外では継続できず、未解決Attempt中は次へ進めない不変条件をpure testで固定する。

### t02-runner-core

Visual Macro、ObservedScene、AttemptDispatchGateを接続する決定的runnerを実装する。Before監査、arm後の一回dispatch、After監査、停止、完了を一方向状態遷移にし、自動retryを持たない。

### t03-host-nano-adapter

保存済みroute／structureからprogramをpinし、capture／scene同定とNano Serial HID primitiveをrunnerへ渡すHost境界を実装する。禁止risk、別environment、Nano未接続、stale frameはdispatch前に明示停止する。

### t04-supervised-ui

「学習した操作」画面へ教師付き実行面を追加し、現在step、前後監査、送信状態、停止理由、履歴と「開始」「次の一手」「停止」を表示する。保存・元に戻すの右下配置を維持する。

### t05-focused-tests

Playbooks、Host、Desktop、Persistenceの変更直結testを実行する。二重command、Before不一致、After不一致、Nano例外、復元、全step完了を含め、Windows buildと実SQLite scenarioを確認する。

### t06-nikke-live-slice

NIKKEの非課金・非消費・可逆sliceを実ゲームで一stepずつ実行する。全入力がNano Serial HIDであること、前後画面が一致すること、SendInput／Computer Use入力／fallbackが0であることを証拠へ残す。ダイヤや希少資源を使う操作は行わない。

### t07-phase12-exit

focused test後に関連testを一回、Windows実画面、NIKKE実証、git diffを監査し、Exit判定、claim境界、計画正本を更新して対象path限定commit／pushで閉じる。

## 依存順

`t01 → t02 → t03 → t04 → t05 → t06 → t07`

## 受入時の根拠4値

- 確認済み: focused test、Windows実走、実ゲーム観測で成立したもの。
- 強い推定: fake／fixtureで成立し、実ゲーム未実証のもの。
- 未確認: 実装・観測のどちらかが不足するもの。
- 非対応: 本Phaseの非対象として明示したもの。
