# G13 Native LCD campaign 計画

- 起票日: 2026-08-23
- 対象requirement: DEV-011
- 状態: Phase 1 Exit成立、Phase 2機能中核成立（latency／実機hotplugのExit確認待ち）
- 調査正本: [G13 LCD Windows write調査](../rag/openlogicool/g13-lcd-windows-write-2026-08-23.md)

## 1. 目的

LGSを停止または削除した環境でも、OpenLogicoolがWindows標準HID stackのままG13 LCDを所有し、
現在のアプリ／workspace／profileを表示できる製品経路を届ける。

一枚絵のprobeだけを完成とは扱わない。ただしrendererやUIを先に作らず、最初に標準入力と共存するnative writeを実機で成立させる。

## 2. campaign境界

含むもの:

- Windows標準HID collectionの列挙とcaps診断
- 160×43 / 1-bit framebufferと992-byte wire reportのpure実装
- G13実機への揮発性frame write
- 既存Raw Inputとの共存、hotplug、停止時の所有解放
- resident hostからのLCD更新
- Input StudioでのLCD状態表示と最小設定
- 実用表示（現在のアプリ、workspace、profile、device状態）

含まないもの:

- WinUSB/libusb driverへの自動差替え
- firmware、profile、registryの永続write
- LGS LCD applet SDK互換
- 任意plugin／動画／高頻度animation
- G13 RGB／M LED（DEV-011内の別実験）

## 3. Phase

### Phase 1 — 標準HID write gate

1. Microsoft公式仕様とG13公開protocolを正本化する。
2. G13 HID interfaceのcapsを列挙し、992-byte output collectionを一意に選ぶ。
3. pure framebuffer builderと識別用test patternをfocused testで確定する。
4. `WriteFile`一経路だけで実機へ一枚絵を送る。
5. オーナー目視で表示を確認し、直後のG13押下／解放がRaw Inputで欠落しないことを確認する。

Exit:

- 992-byte frameが全長writeとして完了する。
- オーナーが識別用patternをLCD上で確認する。
- write後もG13 inputがdown/up対で届き、drop 0である。
- driver、firmware、profile、LGS設定に変更がない。

標準HID collectionが経路を公開しない場合はPhase 1不成立とする。driver差替え案は別campaignの提案に分離する。

Phase 1結果（2026-08-23）:

- 標準HidUsbの単一collectionが992-byte output reportを公開した。
- `WriteFile`でsolid frameを992/992 bytes送信し、オーナー目視でLCDの白一色への変化を確認した。
- write後もG1 down/upをsequence 1/2で取得し、drop 0だった。
- driver、firmware、profile、registry、LGS設定は変更していない。
- 診断限定の`HidD_SetOutputReport`はerror 31で不成立。fallbackせず不採用とした。

以上によりExit 4条件はすべて確認済み。判定証跡は
[Phase 1標準HID write gate](../evidence/g13-native-lcd/p1-standard-hid-write-gate.md)を正とする。

### Phase 2 — resident LCD runtime

Phase 1成立後だけ着手する。

- LCD I/Oをfast pathから独立した低優先度workerに置く。
- 最新frameだけを採用し、入力処理・mapping・emitterを待たせない。
- host開始、workspace/profile変更、hotplug再接続、handled stopを明示状態で扱う。
- I/O失敗を画面と診断へ出し、旧frame維持を成功と表示しない。

Exit:

- resident hostで現在app／workspace／profileを表示する。
- app/profile切替がLCDへ反映される。
- LCD write中もfocused latency gateとG13 input conformanceを維持する。
- 抜差し後にstale handleを再利用せず再表示する。

Phase 2機能中核結果（2026-08-23）:

- 独立workerの`G13LcdRuntime`、最新frame優先、明示fault、resident host配線を実装した。
- workspaceごとの画像／テキストframe保存と、Input Studio G13ペインの設定UIを実装した。
- 既存app-first判定から対象workspaceのLCD設定を選び、対象外／Unknown／共通設定ではWindows画像へ戻す。
- NIKKE画像とWindows画像を実機LCDで確認し、一時DBのpath一致で`ws-nikke-G13`への自動切替を実測した。
- NIKKEと共通設定が同じprofileを参照していた既存データ構造を発見し、アプリ編集前のworkspace分岐で根治した。実DBもNIKKE=`ws-nikke`、共通設定=LCD未設定へ分離済み。
- 証跡は[プリセット表示・設定 delivery](../evidence/g13-native-lcd/p2-preset-lcd-delivery.md)。
- Phase 2 Exit全体は、LCD更新中のfocused latency gateと実機抜差し後の再表示を確認してから判定する。

### Phase 3 — Input Studio delivery

- G13保守面にLCDの接続状態、最終更新、faultを表示する。
- LCD表示の有効／無効と最小表示内容をworkspace設定として保存する。
- installer／support matrix／運用文書へ反映する。

Exit:

- 利用者がLGSなしでLCD表示を有効化し、通常利用できる。
- fakeと実SQLiteの同一public経路が一致する。
- 実機smoke、関連test、最終full regressionがgreenである。
- public claimは「G13 native LCD status display」に限定し、LGS applet互換とは表記しない。

## 4. Phase 1の安全契約

実機write前に次を表示してから実行する。

- 送るのはLCDの揮発性frame一枚だけ。
- driver／firmware／profile／registryは変更しない。
- 復旧はprobe終了、必要時はG13の抜差し。
- LGSは停止したままにする。

`HidD_SetOutputReport`、WinUSB、libusb、driver差替えはfallbackとして使わない。

## 5. 工程と中断点

このcampaignはPhase 1でオーナーのLCD目視、Phase 3で実用journeyの実機受入を計画しているため統括レーンとする。
工程記録はControlを使う。Lattice planはオーナーから新規適用の指示がないため使わない。

Phase 1の人待ちは一度だけで、probeがframeを送った直後に「識別patternが表示されたか」を確認する。
それまでは親が調査、実装、focused test、caps診断を自律して完了する。
