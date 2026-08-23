# ADR: Serial HID hard-kill解放と実ゲーム受入の判定境界

- 日付: 2026-08-23
- 状態: accepted
- 対象: `t09-hard-kill-and-game-observation`

## 決定

- hard-kill解放は、物理key-downを保持したままhost processを強制終了し、Windows HIDで対応key-upを観測する。予算起点はkill完了ではなくkill要求時刻とし、250ms以内だけを成立とする。緊急all-upが必要になった実測は成立に数えない。
- 実ゲーム受入は、製品trace上の単一physical down/up、対応output tokenのmatching ACK完了、drop/faultなし、ゲーム内の単一反応を別々に記録し、全条件成立で確認済みとする。
- 対象ゲーム前面中にWindows低レベルフックがeventを観測しない場合、その未観測をSerial HID送信失敗またはゲーム内不受理へ読み替えない。フック結果は独立した根拠欄へ残し、製品traceや人のゲーム内観測の代替にしない。
- `ResidentInputHost`は、構成済みprofileに対応するdevice sourceだけを`FastPathPump`へ配線する。unknown deviceを受けた`FastPathPump`のfault停止契約は維持する。

## 理由

Pro Microのdead-man保証を確認するには、正常終了やwatchdogのrelease処理を通さない強制終了が必要である。また、kill API完了時刻を起点にするとprocess終了処理の時間を予算から除外してしまうため、ユーザーが停止を要求した時点に相当するkill要求を起点とする。

NIKKE実測では、低レベルフックはEscを観測しなかったが、G13 G1のdown/up各1回がfast pathを通り、両方のSerial HID matching ACKが完了し、ゲーム内でも1回反応した。観測面の制約を送信経路の失敗に混ぜると、成立した経路を誤判定する。反対に、ACKだけをゲーム受理と扱うこともできないため、ゲーム内反応はオーナー観測として独立条件にした。

G13だけの一時profileでG600 sourceまで配線していた欠陥はhost構成の責務違反だった。`FastPathPump`側でunknown deviceを無視するとfault契約を弱めて原因を隠すため、source選択をhostへ戻すのが最小の根治である。

## 検証

- hard-kill: kill要求から148.6321msでrelease、250ms以内、緊急all-upなし
- NIKKE: G13 G1 down/up各1、Esc ACK各1、drop/faultなし、ゲーム内反応1回
- Windows低レベルフック: NIKKE前面中は未観測として分離
- Host focused test: 109件成功
- Probe focused test: 20件成功

## 非対象

この決定はrepeat、toggle、画像認識、ゲーム自動化、descriptor偽装を導入しない。NIKKE以外のゲームやanti-cheat実装ごとの受理、アカウント運用上の判断も一般化しない。
