# Serial HID Output 運用・復旧手順

- 対象: OpenLogicool Input Studio の USB出力（Serial HID v1）
- 確認済み環境: Windows 11 build 26200 / x64、SparkFun Pro Micro ATmega32U4 5V / 16MHz、firmware 1.1.3
- device identity: `USB\VID_1B4F&PID_9206\HIDFG`
- protocol: v1、keyboard 6KRO、mouse button 5個、relative pointer／wheel、firmware lease 150ms

## 通常運用

1. Pro Micro、G13、G600を接続する。
2. LGS、G HUB、Logi Options+を終了する。LGSは将来のLCD調査用にインストールしたままでよいが、自動起動を無効にし、Serial HID運用中は`LCore.exe`を起動しない。
3. Input Studioを`ui --resident`で起動する。
4. 画面上部の「出力方式」を開き、「USB出力（SparkFun Pro Micro）」を選ぶ。
5. 候補を選び、「接続を確認して保存」を押す。接続確認が失敗した場合は保存されない。
6. 表示が「保存済み」なら、常駐を正常終了して再起動する。出力方式は実行中に切り替わらず、次のresident sessionから有効になる。
7. 「使用中: USB出力（Serial HID）」を確認してから使う。

開発workspaceからの起動例:

```powershell
dotnet run --project src/OpenLogicool.Host/OpenLogicool.Host.csproj -- ui --resident
```

Serial HID v1は通常キー同時6個までである。relative pointer／wheelはfirmware 1.1.0以降の`MOUSE_DELTA`で扱う。firmware 1.1.3はCDC応答停止からのHello回復に加え、fail-closed releaseが1秒継続した場合と、USB／CDC処理からmain loopへ2秒戻らない場合にwatchdogでUSBを自己再列挙する。actionは自動再送しない。7個以上の同時押し、音量などのconsumer controlには対応しない。対応外の割り当ては部分送出せず、明示faultで停止する。

## 1回のキー入力

画面を確認しながらキーを1回だけ送る場合は、Hostの製品入口を使う。

```powershell
OpenLogicool.Host.exe game-index key-tap --process MabinogiMobile --db <記録用DBのパス> --keys Key:Space
```

現在の対象ウィンドウを取得して前面へ出し、取得したframeへキー操作を束縛してNanoの有限down/upを1回だけ送る。`Dispatch.Status=Dispatched`は送出結果だけを示し、ゲーム内の進行は次の実画面で確認する。AI、操作後10秒の判定待ち、自動再送は使わない。既存の`game-index back`は同じ入口でEscを送る。

## 画像・文字条件によるキー入力

`game-index key-assist`は、利用者指定の停止画像、文字表示、通常間隔の順でキー入力を判断する開発用の製品入口である。

`--progress-profile <設定.json>`は、確認済みの会話送り・クエスト報酬・自動着用・装備確認・退出を画像と文字の条件で実行する。現在のゲーム描画領域内の位置で照合し、クリック先はその画像のOCRで求める。会話は8〜12秒の間隔を維持し、「スキップ＋会話文」または「吹き出しの形＋会話文」が必要。同じ表示への再送をせず、結果未確認・未知画面・選択画面では進行と回復の両処理を終了する。回復設定だけで通常Spaceを停止する場合との違いに注意する。進行設定には`--recovery-profile`も必要。規則の正本は`fixtures/visual-recovery/mabinogi-20261008/progress.json`。

`--review-mcp <接続.json>`を付けると、両処理の終了後にApproval Boxの公開MCPへ画像・停止理由・記録場所を送る。接続設定は`Executable`と`Arguments`でMCPの標準入出力接続を指定する。申請前の一覧確認と確認札による再申請を行い、同じ停止理由の未決申請には画像と状況を更新する。通知失敗はエラーとして返す。選択画面の`ChoiceBounds`は各選択肢の見出し領域で、読み取った選択肢を個別に通知する。OCRで読めないものは位置と読取不能を明記して画像を添え、自動で決めない。

`--recovery-only --recovery-profile <設定.json>`は、進行処理を起動せず250ms周期の回復監視だけを実行する。自動戦闘を待つなど、進行入力を止めたまま回復を続ける場合に使う。停止画像・HUD・食事・薬の条件と状態保存は通常実行と共通。`--cue-text`・`--cue-image`・`--keys`は不要で、`--progress-profile`・`--observe-only`とは併用できない。`--measure-only`を加えると回復判定のみ計測して入力しない。死亡・回復判定の確認要求・実行期限・取消で終了する。

回復設定の`IncapacitatedContextText`は、敗北見出しと同時に必要な確認文言。マビモバでは復活操作の表示も必要とし、クリア説明の「行動不能にならずに」を敗北と誤認しない。進行設定の判断要求も、ボーナスを選択する見出しと敗北見出しの領域で照合し、結果一覧の「協力ボーナス」を選択画面と扱わない。

進行規則の`WaitForChange`は、条件に一致している間、入力せず画面変化を待つ。キー・クリックとの併用はできない。低い`Priority`で置くと、会話・着用・退出の操作条件が現れた時にその規則へ移る。確認済みの演出・戦利品表示で使い、待機中も回復監視は継続する。固定ラベルの操作候補は一致した設定文言で識別し、周囲のOCR変動では候補を変更しない。条件が空文字の会話本文は観測文字列を識別に使う。

未知画面は複数回観測し、描画領域の画像の変化中は入力せず待つ。既存の輝度指紋の差が6以上なら待ち時間を数え直し、少なくとも3回観測して、最後の変化から`UnknownTimeoutMs`（標準10秒）が続いた時だけ判断要求を返す。操作後に未知の演出へ入った場合も同じ条件を使う。操作した同じ既知表示が変わらない場合は`ResultTimeoutMs`（標準5秒）を使い、複数回観測と画像変化による延長は共通。明確な選択・敗北の表示は従来どおり通知・終了する。`progress-observation`へ画像差・変化検出・認識結果・待機判断を記録する。画像指紋は8×8の輝度サンプルであり、小さい光点の点滅を画面遷移とは扱わない。一般ゲームのすべての演出を検出する保証はない。

回転する会話待ちの印は、進行規則の`ImageRotates`で指定する。明るい背景上の単色の印を連結画素へ分け、回転した参照形との重なりで照合する。探索範囲・画像・描画幅は設定が所有し、ゲーム名・NPC名・台詞をコードに持たせない。回転中も会話文の存在を必要とし、印が回ったことを会話の進行とは扱わない。OCRが完全に空の場合はこの規則に一致せず、未知画面の通知へ進む。実測と未確認範囲は[記録](../evidence/mabinogi-key-assist-20261008/progress-script-live.md)を参照する。

回答だけではゲーム入力を再開しない。操作者が回答と現在の画面を確認して再開する。Throughlineで会話が切り替わったら、`list_my_decisions`で対象を確認し`resume_decision`で返信先を引き継ぐ。通常の画面判定・入力にAIは不要だが、未知画面の規則追加と選択の反映は操作者が担当する。`--duration-ms`の実行期限でも処理は終了し、時間切れだけでは決裁申請しない。

```powershell
OpenLogicool.Host.exe game-index key-assist --process <対象プロセス> --db <記録用DB> --inhibit-image <停止画像.png> --cue-text Space --cue-text "画面を押してください" --keys Key:Space --duration-ms 60000 --evidence <記録フォルダ> --out <結果.json>
```

`--recovery-profile <設定.json>`を付けると、校正したHUD画像とHPバーの枠から回復・食事を判断する。停止画像を最優先し、HUD非表示中は消費しない。枠の右端を毎回測り、目盛りの個数や使用前の固定幅を分母にしない。水色を含む充填部分の割合が設定基準以下ならポーションキー、食事の使用前表示があり効果アイコンがない時だけ食事キーを送る。ポーション後のHP増加が見えなくても監視を継続し、設定された待ち時間後に次回の使用を判定する。食事後の効果が確認できない場合と、校正外のサイズ・判別不能なHUDは`NeedsReview=true`で入力を止める。利用者への通知・裁定は操作者がApproval Boxで行う。

`fixtures/visual-recovery/mabinogi-20261008/profile.json`は実測した2203×1319画面を基準とし、現在の描画領域の倍率で左上のHUDを正規化する設定である。食事B・ポーションF1・HP70％・薬の待ち10秒・食事の最短間隔20分に加え、白い部分がHPバー全長の20％以上なら包帯F2を優先し、5秒の待ち時間を持つ。包帯後3秒で白い部分の減少を確認できなければ追加使用せず停止する。白い表示を傷・中毒と断定する判定ではなく、利用者指定の画像条件である。設定を付けた`--observe-only`は回復判定も返し、入力しない。消費の試行と待ち時間は`<記録用DB>.visual-recovery.json`へUSB送出前に保存し、同じDBでの再起動後も引き継ぐ。手動で削除して消費を繰り返す運用にはしない。前後画像と判定・送出履歴は指定の記録フォルダへ保存する。

回復キーの送出直前に画像条件を再照合し、条件が変わっていれば送らず`recovery-input-withheld`へ直前の観測と見送り理由を保存する。包帯は白20％への到達で1回分の要求を保持し、停止表示・HUD非表示・HPゼロ・判定障害で取り消す。`recovery`の操作候補は送出の証拠ではない。実際の送出は`recovery-input`で確認する。包帯5→4の実使用は[実測記録](../evidence/mabinogi-key-assist-20261008/bandage-latch-live.md)で確認済み。白の減少すべてを治療効果と断定していない。

回復監視は250ms周期の独立処理で、進行用のOCR・Space後の1秒待ち・画面比較を待たない。Nanoへの送出だけを直列にし、進行側が使用中でも回復の観測を継続する。死亡・障害・時間切れでは両処理を終了して回収する。`recovery-sample`に観測間隔と画像の鮮度、`recovery-input`に条件検出時刻と送出までの時間を記録する。`--measure-only`は同じ撮影・判定を指定時間実行し、前面化・Space・消耗品の送出と回復状態の保存を行わない。Nanoとの接続確認・全解放は通常の製品入口と共通である。

食事だけが未判別の場合と食事の最短使用間隔内はBを送らず、HP監視を継続する。HUDは表示されているがHP枠が読めない場合は最大2秒入力せず観測し、識別できれば復帰、できなければ確認待ちで停止する。描画領域を取得できない場合はエラーで停止する。回復設定を付けた実行で通常Space後の画面変化が未確認になった場合は、`progress-paused`を記録し以後のSpaceだけを止め、指定時間まで回復監視を続ける。終了結果は`NeedsReview=true`を維持する。行動不能表示の検出時は実行を終了する。

回復設定の`Viewport`は基準画像のゲーム描画領域、`CanvasAspectRatio`はHUDの倍率を決める縦横比。実行中はWindowsから現在の描画領域を取得し、窓枠を除いて倍率を求める。左上のHUDだけを基準座標へ戻してHPを測り、食事Bは対応する小領域へ参照画像を縮小して照合する。停止画像とキー画像の倍率も追従する。移動はウィンドウ単位の撮影で追従し、撮影と描画領域の寸法が食い違う間は入力せず再観測する。食事の成功判定には、送出後の効果アイコンと基準座標でのHP全長増加の両方を要する。

停止画像の一致を最優先し、一致中はキーを送らない。指定文字はWindows OCRの結果から空白を除いて照合する。文字表示がある場合はキーを送り、ない場合は送出時刻から8〜12秒の乱数による期限を設ける。実際の送出時刻には画面取得・照合・前面化の処理時間も含まれる。探索範囲は画面全体で、`--search-bounds x,y,width,height`で指定もできる。画像による入力条件は`--cue-image <png>`で指定できる。

通常間隔で入力した場合は、1秒後の画面をOCRと画像特徴で比較する。変化が確認できなければ`NeedsReview=true`で終了し、`review-before.png`と`review-after.png`を残す。呼び出した操作者はそこで画面を確認し、判断できない場合はApproval Boxへ利用者の判断を申請する。比較結果はゲーム内の成功やページ遷移の確定を意味しない。自動戦闘やアニメーションの影響で変化が出ることがある。

入力と前面化はNano経由で行い、AI・ネットワークによる判断は呼ばない。前面化やNanoの送出に失敗した場合はエラー終了し、別方式へ切り替えず、入力を再送しない。`--observe-only`ではキー入力・前面化をせず、条件の照合結果と`observation.png`を保存する。指定時間の終了はクエスト完了を意味しない。

## 正常終了

Input Studioを閉じると、fast pathの所有出力を解放し、Serial HIDへ`ALL_UP`を送り、ACK後にserial transportを閉じる。G600を管理している場合は、起動時に適用したlegacy出力抑止を保存済みbaselineへ戻す。

終了時にG600復元エラーが表示された場合は、LGS／G HUB／Options+が停止していることを確認して次を実行する。

```powershell
dotnet run --project src/OpenLogicool.Host/OpenLogicool.Host.csproj -- leftover restore
```

baselineが無い、またはbyte一致を確認できない場合は成功扱いにしない。`probe g600-restore-retry`の既存復旧手順へ戻る。

## fault・抜線・hard kill

何も押していない時の心拍遅延は接続を失効させない。入力を保持中の150ms期限、全解放、期限切れsessionの拒否は維持する。`serial-hid-test --idle-ms 200`はゲーム入力を保持せず通信を空け、同じ接続で次のHEARTBEATが通るかを検査する。

`serial-hid-test --read-pause-ms 100`は送信後のhost読取り処理を意図的に遅らせ、受信済みの正常応答が時間切れ扱いにならないことを検査する。未受信や不完全な応答を追加で待つ期限は変更しない。

- Pro Microが未接続、複数候補、firmware／protocol不一致、ACK timeout、破損frame、sequence不一致になった場合はterminal faultで停止する。SendInputへ自動fallbackしない。
- Pro Microを再接続しても同じsessionは再開しない。Input Studioを終了し、候補が1台だけ応答することを確認して明示的に再起動する。
- hostがhard killされてもfirmware leaseが保持中出力を解放する。実機ではkill要求から148.6321msでkey-upを観測し、250ms予算内だった。
- G600のlegacy抑止がhard kill後に残った場合は、共存ソフトを停止して`leftover restore`を実行する。

## firmware再flash

### 無応答時のWindows機器再起動

HELLOが時間切れでもNanoのUSB・CDCが列挙されている場合、管理者権限でWindows標準`pnputil /restart-device <Nano本体のPnP機器ID>`を使い、物理抜き挿しせず通信が戻る場合がある。対象は出力設定で選択したNanoと同一ContainerIdの本体一台だけとし、USBコントローラー全体へ操作しない。BotやNanoを使う通常割当の接続を先に回収する。

再認識後は正規`serial-hid-test --repeat 1`でHELLO/READYと全解放を確認してからBotを明示的に再開する。失敗した入力を再送せず、応答回復と障害原因の特定を区別する。再起動も診断も失敗した場合はその結果を残す。実測は[復旧記録](../evidence/nano-software-recovery-20261009/acceptance.md)。

### 再flash手順

repo内のfirmwareを再flashする場合:

```powershell
pwsh.exe -NoLogo -NoProfile -File scripts/build-serial-hid.ps1
pwsh.exe -NoLogo -NoProfile -File scripts/flash-serial-hid.ps1 -ExpectedDeviceInstanceId 'USB\VID_1B4F&PID_9206\HIDFG'
```

flash scriptはexact target identity、固定toolchain、upload verify、CDC＋keyboard＋mouseの再列挙を検証する。自動bootloader捕捉が失敗した場合だけ、Pro Microをdouble-resetしてCaterina bootloaderを開く。targetが一意に決まらない状態ではflashしない。

旧firmwareのwatchdogが自動resetを妨げる場合は、同じ正規scriptへ`-WaitForReset`を付ける。scriptが同じ物理USB接続のbootloaderを待ち、手動double-reset後に自動で書き込む。USBを抜き挿しせず、HIDの無効化やPC再起動を代行手段にしない。書込み用1200-baud close中にwatchdogを給餌しない修理はfirmwareが所有する。

以前の第三者firmwareへ戻すには、そのfirmwareの保持済みsketchまたはhexが別途必要である。OpenLogicool repoには第三者firmwareを同梱しない。

## 確認済み範囲

| 項目 | 判定 | 条件 |
|---|---|---|
| firmware build／flash／再列挙 | 確認済み | 固定toolchain、Pro Micro 5V / 16MHz |
| G13／G600 key・mouse button・chord・finite sequence | 確認済み | 同一Serial HID経路 |
| relative pointer／wheel | 確認済み | firmware 1.1.3、Windows hookでnon-injected move／wheelを観測 |
| CDC session回復 | 確認済み | firmware 1.1.3、Hello 2秒／通常action 80ms、releasePending 1秒またはmain loop block 2秒でwatchdog自己再列挙 |
| layer／profile／app-first切替／保存後再起動 | 確認済み | FOX reference machine |
| handled stop／hard kill release | 確認済み | 250ms以内 |
| dispatch latency | 確認済み | 200 edge、p99 3.425ms、max 12.902ms |
| drop／wrong release／stuck | 確認済み | 0／0／0 |
| NIKKEのG13 G1→Esc | 確認済み | 1回押下に1回反応した単一観測だけ |
| Windows低レベルhookでのNIKKE前面中Esc | 未確認 | hookは未観測。ACKやgame反応と混同しない |
| NIKKE前面中F13／wheelの管理者hook受信 | 確認済み | 完全順序、全event `IsInjected=false`、injected 0 |
| 他game／他anti-cheat／長時間運用 | 未確認 | 一般対応を名乗らない |
| Windows 10／ARM64／別board | 未確認 | reference machine外 |
| raw USB report byteの独立capture | 未確認 | ACK／Windows HID観測で代用しない |
| NKRO、consumer control | 非対応 | Serial HID v1の固定境界 |
| LCD、LGS applet、power mode | 未確認 | 本campaignの対象外 |

製品全体の公開claimは引き続き`Partial LGS Replacement`である。Serial HIDの成立を、LGS全機能parity、全game対応、または利用規約上の許可へ拡張しない。
