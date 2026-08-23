# t09 hard-kill解放・NIKKE実ゲーム観測

- 日付: 2026-08-23
- Task: `t09-hard-kill-and-game-observation`
- 実測機: `FOX`
- 対象: G13 G1 → Serial HID `Key:Esc`
- 結論: 成立

## 結果

| 項目 | 判定 | 実測 |
|---|---|---|
| hard-kill後の解放 | 確認済み | kill要求から148.6321ms、kill完了から141.0789msでF13 key-upを観測 |
| NFR-008 250ms以内 | 確認済み | 148.6321ms、緊急all-upは未使用 |
| G13 G1の入力対 | 確認済み | down/up各1、wrong release 0、stuckなし |
| Serial HID送信 | 確認済み | `Key:Esc` down/upの両方がmatching ACK完了、`emitted=true` |
| fast path状態 | 確認済み | processed 2、drop 0、pump/host faultなし |
| NIKKE内の反応 | 確認済み | オーナー観測「うごいた」。1回反応として記録 |
| Windows低レベルフック | 未確認 | NIKKE前面中はEscを観測せず。製品traceとゲーム内反応から分離して記録 |

## hard-kill実測

`serial-hid-hard-kill-20260823-171036-483.json`で、G13 G1を保持した状態の子hostを強制終了した。Serial HID側のF13 downを確認してからkillし、物理G1をまだ保持したままF13 upを観測した。

- down観測: あり
- release観測: あり
- kill要求→release: 148.6321ms
- kill完了→release: 141.0789ms
- emergency all-up: なし
- Windows観測event: F13 down/up各1、`isInjected=false`
- 結果: PASS

これにより、hostが正常終了処理を行えない場合でも、Pro Microのdead-man timeoutが250ms以内に保持中出力を解放することを実機で確認した。

## NIKKE実ゲーム観測

NIKKEを前面にしてG13 G1を1回押した。`serial-hid-game-observation-20260823-172401-688.json`に次を記録した。

- physical control: `G13:G1`
- output token: `Key:Esc`
- trace sequence 1: G1 down、`Key:Esc`、ACK完了、dispatch 11.6239ms
- trace sequence 2: G1 up、`Key:Esc`、ACK完了、dispatch 2.9997ms
- logical press: 1
- wrong release: 0
- stuck: false
- game response: once
- 結果: PASS

NIKKE前面では同時起動したWindows低レベルフックがEscを拾わなかった。一方、製品fast pathは同じ1押下のdown/upを処理し、Serial HIDのmatching ACKまで完了し、NIKKE内でもオーナーが1回の反応を確認した。したがって、低レベルフックの未観測をゲーム入力失敗へ読み替えず、`windowsHookObservation=not-observed-in-nikke-foreground`として独立に残した。

## 調査中に特定した製品欠陥と修正

最初のNIKKE観測ではG13を押しても反応しなかった。子host診断は次のfaultを記録した。

```text
FastPathFaultException: device instance '\\?\HID#VID_046D&PID_C24A...' の Mapping Runtime が構成されていません
```

一時DBにはG13 profileだけが存在したが、`ResidentInputHost`がG600 sourceも無条件に`FastPathPump`へ渡していた。G600を動かすと、profileのないdevice inputとしてfast pathが正しくfault停止し、その後のG13入力を処理できなくなっていた。

修正はhost配線の責務だけに限定した。`ResidentInputSourceSelection`が構成済みprofileのdevice kindに対応するsourceだけを選び、`FastPathPump`のunknown-device fault契約は変更していない。G13 profileだけならG13だけ、両profileがあれば両sourceを配線する。既存契約「profileがない種別は配線しない」に戻した修正である。

LGSは停止状態で最終実測を行った。LGSによる上書きは原因ではなかった。実測終了後に`LCore.exe`を再起動し、通常状態へ復帰した。

## focused test

- Host: 109件成功、失敗0
- Probe: 20件成功、失敗0
- `git diff --check`: エラーなし

VSTest wrapperはこのhost固有のSocket 10013で起動不能のため、既存の公式direct xUnit runnerで同じtest assemblyを実行した。通し試験はt10の最終gateまで実行していない。

## 証拠境界

確認済みなのは、G13実入力、Mapping Runtime、Serial HID matching ACK、hard-kill時dead-man解放、NIKKE内の1回反応である。Windows低レベルフックでのNIKKE前面中のEsc観測、他ゲーム、anti-cheatごとの受理、長時間運用、repeat・toggle・画像認識・descriptor偽装は本taskの成立範囲に含めない。

## 構造化証拠

- `probe-output/serial-hid-hard-kill-20260823-171036-483.json`
- `probe-output/serial-hid-game-observation-20260823-171815-713.json`（旧フック依存判定の失敗記録）
- `probe-output/serial-hid-game-observation-20260823-172401-688.json`（ACK traceとオーナー観測を分離した確定記録）
