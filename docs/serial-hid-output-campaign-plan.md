# Serial HID Output campaign — G13／G600 共通物理USB出力

- status: **planned（設計正本を記録済み・実装未着手）**
- 起票: 2026-08-23
- campaign key（予定）: `serial-hid-output`
- 上位正本: [development-plan.md](development-plan.md) の製品境界、DEV-006〜009、MAP-008、NFR-002／008／012
- 先行実装: `Device Input → Mapping Runtime → IOutputEmitter`、SendInput emitter、Windows watchdog、G13／G600 live adapter、app-first workspace、G600 onboard補完
- 実行TODOの正本: Lattice store。2026-08-23の `lattice status --json` は `ready`、active run なし。ただし既存完了planがactive planとして残り `can_create_plan=false` のため、本campaignのLattice planは未作成。store整合を回復してから起票し、Markdownへ実行状態を二重化しない
- 本書の役割: campaignの目的、設計裁定、非目標、F/A/H、受入条件、既知の罠を所有する。Taskの進行状態とwitnessはLattice storeだけが所有する

## 1. 背景と目的

OpenLogicoolの現在のfast pathはG13／G600の物理入力を読み、workspace、foreground app、layer、binding revisionを解決し、`IOutputEmitter`へoutput edge列を渡す。既定の`SendInputEmitter`はWindows標準アプリでは成立したが、NIKKE実機ではfast pathの送出成立に対してゲーム内反映が無かった（[NIKKE SendInput rejection](../rag/openlogicool/nikke-sendinput-rejection-2026-08-22.md)）。

G600は方式Aのonboard F3書込みにより本体からHID keyboard入力を出せることを実機確認した。一方、G13はvendor-defined input collectionだけを持ち、単体でkeyboard usageを送る経路が無い。G13／G600の共通出力をSendInputへ依存せず実現するには、既存fast pathの出口だけを物理USB HIDへ差し替える必要がある。

本campaignは、接続済みのSparkFun Pro Micro（ATmega32U4、5V／16MHz、CDC serial＋HID対応）を、OpenLogicool専用の正直なUSB keyboard／mouse bridgeとして使う。PC側はG13／G600の入力を従来どおり解決し、変換後のHID状態をserialでPro Microへ送り、Pro MicroがUSB HID reportとしてWindowsへ出力する。

成果は「コードがあること」ではなく、次の利用可能な一巡である。

1. firmwareを再現可能にbuild／flashできる
2. UIで出力経路を明示選択できる
3. G13／G600の両方を同じbridge経由で操作できる
4. crash／抜線／停止時にstuck outputを残さない
5. 成立・不達・未確認を分けた実機証拠と復旧手順がある

## 2. 統括レーン判定とF/A/H

本campaignはfirmware、Windows emitter、設定UI、flash、実機入力、hard-kill、ゲーム内観測の受入が多段に連鎖し、途中に人が機械を動かさないと得られない観測を含むため統括レーンとする。Phase Exitや合否をオーナー待ちにはしない。

- **F**: protocol version、出力切替契約、fault／release境界、製品境界、G600 onboardとの排他、計画正本更新、task受入、最終合否、対象限定commit／push
- **A**: firmware、protocol codec、HID usage変換、Serial HID emitter、port discovery、machine-local設定、UI、focused test、probe、手順書
- **H**: 自動bootloader捕捉が成立しない場合のPro Micro double-reset、G13／G600の物理押下、ゲーム内1:1入力の画面反応観測。Hは証拠取得だけで、設計・合否・ExitはFが閉じる

同一repoへのwriterが複数になり得るが、Lattice plan未作成の間は並列writerを起動しない。Lattice復旧後もindependence未検証のTODOを自前判断で並列化しない。firmwareと.NETの書込境界が独立とLatticeが記録した場合だけ同時実装を許す。

## 3. 固定する設計裁定

### D-01: raw edge転送ではなくHID状態snapshotを送る

単純な`DOWN usage`／`UP usage`列だけをfire-and-forgetで送る案は採らない。途中frameの欠落、chordの部分成立、host crash後の保持状態不明、serial write成功とfirmware適用成功の混同を防げないためである。

PC側は現在押下中のkeyboard／mouse状態をpure state machineで保持し、変更後の完全snapshotを`SET_STATE`として送る。firmwareはsnapshot全体を受理してからUSB HID reportへ反映し、対応sequenceのACKを返す。

### D-02: protocol v1はversioned binary frameとする

外部serial境界の破損、部分read、古いfirmwareを明示検出するため、protocolはversion付きbinary frameとする。初期frameは次の論理項目を持つ。byte幅とCRC多項式はt01のgolden vectorで固定する。

- magic
- protocol version
- message kind
- sequence number
- payload length
- payload
- checksum

message kind:

- `HELLO`: host versionと要求capability
- `READY`: firmware version、protocol version、keyboard／mouse capability
- `SET_STATE`: modifier mask、通常keyboard usage最大6個、mouse button mask
- `ALL_UP`: 全keyboard／mouse button解放
- `HEARTBEAT`: lease更新
- `ACK`: 対象sequenceを適用済み
- `FAULT`: 対象sequenceと拒否理由

不明version、checksum不一致、長さ不一致、未知messageは適用せず`FAULT`とする。hostはACK timeout／sequence不一致／FAULTを`OutputEmitFaultException`相当で明示し、自動再送しない。

### D-03: chordとfinite sequenceを既存意味のまま保つ

- key chordのdown集合またはup集合は、1個のtentative snapshotへ反映して1回の`SET_STATE`で送る
- finite sequenceはedge列をdown群／up群のcheckpointへ分け、checkpointごとのsnapshotを順序通り送る
- edge列の最終状態だけを送ってfinite sequenceをall-upのno-opへ潰さない
- keyboardとmouse buttonが同じEmitに含まれる場合、同じ`SET_STATE` payloadで確定する

### D-04: outputの重複所有は参照数で合成する

G13とG600が同じoutputを同時に保持しても、片方のupで早期releaseしないよう、PC側stateはresolved outputごとの参照数を持つ。既存runtimeが生成する正しいdown／up対を前提とし、対応downのないupは明示faultにする。状態更新はACK成立後だけcommitし、ACK前のtentative stateを成立扱いしない。

### D-05: v1は6KROを明示制約にする

v1はUSB boot keyboard互換のmodifier＋通常key最大6個を採用する。7個目を部分適用、rollover error、先頭6個だけ送出する挙動は採らない。tentative stateが上限を超える場合は送出前にfault停止する。NKROは実需要とdescriptor受入を別campaignで実証するまで非対応と表示する。

### D-06: Serial HIDのwatchdogはfirmware leaseが所有する

現在のWindows watchdogはSendInputでreleaseを送るため、Pro Microが保持するHID状態を解放できない。Serial HID選択時にWindows watchdogで見かけ上のreleaseを重ねない。

- hostは初期target 50ms間隔で`HEARTBEAT`を送る
- firmwareは最終有効frameから150msを初期leaseとし、期限切れで全reportをall-upにする
- hard-kill実測で250ms以内releaseを満たすことをSupported条件とする
- handled stopはpump停止→所有output release→`ALL_UP` ACK→serial closeの順とする
- firmware起動、USB再列挙、protocol resetは必ずall-upから始める

50ms／150msは先に緩めないengineering targetであり、実測で満たせない場合は原因を特定してprotocol／I/Oを修正する。SendInputへfallbackして成功扱いしない。

### D-07: 出力経路はresident session単位の明示選択とする

出力経路はmachine-localに一つ選ぶ。

- `Windows標準`: 現在の`GuardedOutputEmitter(new SendInputEmitter(), WatchdogChannel)`
- `USB変換器`: `SerialHidOutputSession`

実行中のhot swapは行わない。キー保持中の切替とwatchdog所有権の混在を避けるため、設定保存後の次回handled restartから反映する。UIは「保存済み」と「現在適用中」を分けて表示する。

Serial HIDが未接続、複数候補、protocol不一致、ACK不達の場合、hostは起動またはfast pathをfault停止する。SendInputへ黙ってfallbackしない。

### D-08: COM番号をidentityとして保存しない

flash／抜差し／bootloader遷移でCOM番号は変わり得る。SetupAPIでSparkFun Pro MicroのCDC interfaceだけを候補化し、protocol v1 handshakeが一致した1台だけを採用する。COM8をproduct設定へ固定しない。無関係なserial portを総当たりでopenしない。0台／複数台／version不一致を別エラーにする。

### D-09: USB identityを偽装しない

firmwareは自分を`OpenLogicool Serial HID`として正直に名乗る。一般keyboard、Logicool純正device、他社VID／PIDへの偽装を行わない。実装目的は物理USB bridgeであり、software injection flagやanti-cheat識別の欺罔機能を持たせない。

### D-10: G600 onboard modeとは排他にする

G600 onboard適用中はruntime側G600出力が抑止されるため、両deviceをSerial HIDへ流す要件と両立しない。Serial HIDを選択・有効化する前に、既存UIの「本体の書込みを解除」でG600 onboard状態を明示解除する。自動restoreはdevice writeを伴うため行わない。G600 leftoverのB変種残置はraw input routeの前提として従来どおり維持する。

## 4. machine-local設定とUI

出力経路はworkspace exportへ含めない。接続hardwareとresident processに属するmachine-local設定であり、DBと同じdirectoryのversioned設定file（予定名`output-route.json`）へ置く。

設定項目:

- schema version
- requested route: `send-input`／`serial-hid`
- 選択board identity（firmwareが安定した固有IDを提供できる場合だけ。COM番号は保存しない）

UI表示:

- 「入力の届け方: Windows標準」
- 「入力の届け方: USB変換器」
- 「USB変換器: 接続済み／見つかりません／複数あります／版が違います」
- 「接続テスト」
- 「保存済み・次回起動から反映」または「現在使用中」

UIはinternal token、COM実装、ACK、protocol frameを通常面へ漏らさない。診断画面だけがport、firmware version、protocol version、直近fault、latencyを表示できる。Serial HID設定UIのためにG13／G600 Raw Input sourceを二重生成しない。Windows Raw Input登録は同一process内で最後の登録が勝つ既知罠があり、`ui --resident`の入力を奪うためである。

## 5. firmwareとtoolchain

追加予定:

- `firmware/OpenLogicool.SerialHid/` — firmware source
- `firmware/OpenLogicool.SerialHid/protocol-v1.md` — byte level contractとgolden vector
- `scripts/build-serial-hid.ps1` — pinned toolchain build
- `scripts/flash-serial-hid.ps1` — board identity確認、bootloader捕捉、flash、再列挙確認

実装時点でArduino CLIとSparkFun AVR coreの公式catalogを確認し、動作確認したversionを固定する。`latest`を毎回取得しない。downloaded toolchain、build directory、hex等の生成物を無断でgit管理しない。公開artifact化は本campaignの実機成立後に別判定する。

flashは既存firmwareを上書きする。オーナーは2026-08-22に上書きを明示許可済み。書込み直前には高リスク操作として次を短く再提示する。

- 影響: 現在の`HIDFG` firmwareは失われる
- 戻し方: Caterina bootloaderをdouble-resetで開き、既知green firmwareを再flashする
- 停止条件: target VID／PID、board、clock、bootloader portを確定できない場合は書かない

Arduino coreのreport ID、descriptor、product／serial文字列、USB packet挙動を現在の旧firmwareから推測しない。buildしたfirmwareのdescriptorと列挙を実測して受入する。

## 6. Task構成

Lattice plan作成後、次を独立受入単位として登録する。Task status、dependency、witnessはstoreだけに置く。

### t00-close-g600-dogfood-fix

既存dirty 3ファイルを新featureから分離して閉じる。

- G2〜G5の未割当cellを上書きせずbaselineの右／中click等を保持
- G6〜G20の未割当は従来どおり無動作化
- G600反映不能時のUSB挿直し案内
- Host focused test green
- 未追跡`probe-output/ui-test-scenario-20260822-094519-943.json`を無断で含めない
- 対象限定commit／push

G600 writeは既存`G600EvidenceWrite`のfresh open、settle、handle非再利用、fresh open verify、一致までの規定試行を正とする。direct SET_FEATURE直後の同一stream readbackを成立証拠にしない（[incident](incidents/2026-08-15-g600-f3-writeback-shift.md)、[write protocol](../rag/openlogicool/g600-write-protocol-2026-08-15.md)）。本Taskは新しいraw write経路を増やさない。

### t01-protocol-contract

- protocol v1のbyte幅、endianness、CRC、message、fault codeを固定
- C#とfirmwareで共有するgolden vectorを作る
- chord、sequence、duplicate ownership、6KRO、ACK commit ruleをpure testで固定
- development-planへ新capability requirementを追加し、NFR-002を選択Emitterの成立確認へ一般化

### t02-firmware

- CDC serial＋keyboard＋mouse button descriptor
- versioned handshake
- SET_STATEのatomic apply
- ACK／FAULT
- heartbeat lease expiry all-up
- boot時／protocol reset時all-up
- pinned CLI/coreでcompile green

### t03-serial-hid-core

- protocol codec
- VK→USB HID usage mapper
- modifier／normal key／mouse button state
- output参照数
- tentative→ACK後commit
- sequence checkpoint builder
- timeout／NACK／破損／unsupported usageの明示fault
- 自動再送・fallbackなし

### t04-resident-output-session

- SendInputとSerial HIDのlifecycleを`ResidentOutputSession`相当へ分離
- Serial HID選択時はWindows watchdogを起動しない
- output sessionのbackground faultをresident failureへ伝播
- handled stopのrelease順序
- 抜線時fault停止、再接続で暗黙resumeしない
- G600 onboard排他

### t05-discovery-settings-ui

- SetupAPIによる候補限定
- handshakeで1台に確定
- versioned machine-local setting
- requested／active routeの区別
- 接続testと日本語status
- 次回起動反映
- raw input sourceを二重生成しないarchitecture test

### t06-focused-and-fake-gates

- protocol golden vector
- frame partial read／checksum／unknown version
- ACK timeout／sequence mismatch／FAULT
- chord、finite sequence、duplicate ownership、wrong up
- 6KRO境界、unsupported usage
- handled stop、hard-crash leaseのfake clock
- no fallback
- SendInput既存characterization
- Host／Desktop設定scenario

### t07-flash-and-direct-smoke

- firmware compile
- target identity確認
- flash
- keyboard／mouse／CDC列挙
- HELLO／READY
- host direct commandによるkey、chord、mouse button、sequence
- all-up、timeout release、power cycle all-up
- JSON証拠

### t08-g13-g600-live-smoke

- G13単体→Serial HID
- G600単体→Serial HID
- 両device同時
- layer、foreground profile、保存後再起動反映
- G600 legacy二重配送の有無
- drop 0、wrong release 0、stuck 0
- dispatch latency p50／p95／p99／max
- board抜線fault、fallbackなし、明示再起動復帰

### t09-hard-kill-and-game-observation

- held key中にhost hard killし、250ms以内releaseを実測
- ownerの物理押下による1:1入力だけを対象gameで観測
- USB HID送出成立とゲーム内反応を別の結果として記録
- 自動操作、repeat、画像認識連動、descriptor偽装を行わない
- game policy／account riskは製品のSupported claimと分離し、未確認をSupportedにしない

### t10-campaign-exit

- 各Taskのdiff、focused test、証拠、未確認範囲をFが受入
- related gate 1回
- 全focused／related green後にfull regression 1回
- 契約クリティカル範囲の独立read-only反証監査を1回
- `docs/serial-hid-output-operation.md`
- `docs/serial-hid-output-exit-assessment.md`
- support matrix、rag index、AGENTS現在地の還流
- 対象限定commit／通常push
- 合否と公開claimを親が宣言して閉じる

## 7. focused検証と実機受入

### 7.1 fake／pure受入

1. 同じinputからC# codecとfirmware decoderが同じsnapshotを得る
2. checksum、version、length、sequence不正はHIDへ反映されない
3. chord down／upは各1 snapshot
4. finite sequenceの各down／up checkpointが順序通り
5. G13／G600が同じkeyを保持し、一方のupでreleaseされない
6. ACK前にhost stateをcommitしない
7. timeout、FAULT、抜線で再送／fallbackしない
8. 7個目の通常keyを部分送出しない
9. handled stopは新規downを止めてall-upを完了する
10. SendInput routeの既存挙動が変わらない

### 7.2 実機順序

通し試験を個別動作確認へ使わない。以下をfocused smokeとして順に成立させる。

1. firmware compile
2. Pro Micro flash／再列挙
3. direct handshake
4. direct keyboard／mouse／chord／sequence
5. heartbeat停止release
6. G13 live
7. G600 live
8. G13＋G600 live
9. layer／profile切替
10. hard kill
11. board抜線／再起動復帰
12. latency測定
13. 対象game手動1:1観測

一つの段が失敗したら後段へ進まず、その機能の最小再現で原因を特定する。修正後は同じfocused smokeだけを再実行し、最後のfull regressionはcampaign Exitで1回だけ行う。

### 7.3 Exit受入条件

1. firmware sourceから再現可能にbuild／flashでき、復旧手順がある
2. SendInput／Serial HIDの選択が明示され、未接続時にfallbackしない
3. G13／G600の両方が同じSerial HID経路でkey、mouse button、chord、finite sequenceを送れる
4. profile、layer、app-first切替、generation releaseの既存契約を壊さない
5. handled stopとhard killの両方で250ms以内release
6. drop 0、wrong release 0、stuck 0
7. dispatch latency p99 10ms以下、最大値記録
8. power cycle／抜線後にphantom downがない
9. 6KROとunsupported usageをUI／support matrixへ明示
10. focused／related green後のfull regressionがgreen
11. 実機証拠、操作手順、復旧手順、未確認範囲がrepoへ還流済み

対象gameが入力を受理しない場合でも、USB HID経路自体の成立を偽らない一方、対象game対応をSupportedとは表示しない。対象game受理は独立した受入行である。

## 8. 非目標

- LGS `LGVirHid`／`LGBusEnum` IOCTLの直接利用
- FakerInput、Interception、独自kernel driver、virtual HID driver
- anti-cheat識別の回避、injected flagの欺罔
- VID／PID、manufacturer、product、serialの他製品への偽装
- DLL injection、game memory read／write
- 画像認識連動、無人操作、連打、repeat、toggle、timed macroの追加
- firmwareからworkspace、app identity、AI、SQLiteへ到達すること
- runtime中のEmitter hot swap
- serial障害時のSendInput fallback
- v1でのNKRO、mouse移動、wheel、consumer control
- public installer、firmware auto-update、署名済み公開artifactの採択
- Phase 9の新設。既存Phase番号は8Bで終わったまま、本件はpost-8B capability campaignとする

既存の有限sequenceは新機能として増やさず、現在のDEV-006契約をSerial HIDでも保持するだけとする。timed／repeat／toggleは本campaignへ持ち込まない。

## 9. 既知の罠と停止条件

1. **G600 direct SET_FEATURE readback**: 同一stream直後の一致は成立証拠にならない。fresh open／settle／handle非再利用／fresh verifyを使う。新raw write経路を作らない
2. **G600 active slot／software control mode**: F3が正しくてもactive slotやLGS使用後状態で反映されない。既存slot 0強制と、必要時USB挿直し案内を維持する
3. **COM番号変化**: COM8を保存しない。bootloaderとapplication portを別物として追跡する
4. **Pro Micro bootloader窓**: 自動1200-baud touchが成立しない場合だけowner double-resetをHにする。targetを確定できないままflashしない
5. **descriptor/report IDの推測**: 旧`HIDFG` firmwareやArduino core既定から決めない。新firmwareを列挙・captureして固定する
6. **SerialPort同期I/O**: ACK待ちがfast pathを止め得る。bounded timeoutとlatency実測を置く。10msを満たさない時に非同期queueを場当たり追加しない
7. **Windows watchdogの誤用**: Serial HID保持をSendInput key-upでは解放できない。firmware leaseと混在させない
8. **finite sequence消失**: edge列の最終all-up snapshotだけを送らない。checkpointを保持する
9. **6KRO超過**: 部分送出しない。送出前faultとUI警告にする
10. **G600 onboard抑止**: onboard activeのままSerial HIDを有効化しない。自動restoreしない
11. **Raw Input登録横取り**: UIのport discoveryでG13／G600 sourceを新設しない
12. **false success**: serial write成功、ACK、USB HID report、foreground app受理、game内成功を別々に記録する
13. **policy claim**: 物理USB deviceであることだけを根拠に全game対応や規約許可を名乗らない

停止条件:

- target board／clock／bootloaderを確定できない
- firmware復旧経路を確認できない
- protocol version／descriptorが設計と一致しない
- ACKが一意に相関しない
- hard kill後250ms以内releaseを満たせない
- p99 10msを満たせず原因未特定
- wrong release、stuck、partial chordが1件でも再現する
- Serial HID障害がSendInput fallbackで隠れる

## 10. 証拠の置き場

- protocol／外部仕様調査: `rag/openlogicool/`
- campaign evidence: `evidence/serial-hid-output/`
- probe JSON: `probe-output/serial-hid-*.json`
- 操作・復旧手順: `docs/serial-hid-output-operation.md`
- Exit判定: `docs/serial-hid-output-exit-assessment.md`
- firmware source: `firmware/OpenLogicool.SerialHid/`

証拠にはfirmware hash、protocol version、board identity、OS、host commit、workspace/profile、latency、release時間、drop、wrong release、stuck、fault、未確認範囲を記録する。API／ACK成功だけでgame内成功を記録しない。
