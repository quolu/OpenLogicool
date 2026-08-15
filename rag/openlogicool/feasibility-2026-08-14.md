# OpenLogicool feasibility research — G13/G600 on Windows

- 取得日: 2026-08-14
- 確度: 高（オーナーのWindows実機情報と一次資料を突合）
- 対象: Logicool G13 (`046D:C21C`) / G600 (`046D:C24A`)
- 非対象: このPhaseでの製品コード実装、ドライバー実装、USB書込み実験

## 結論

公開先行実装から、G13入力とG600設定通信を含む主要経路の成立可能性は確認できた。ただし、本製品のWindows実機環境で致命的ブロッカーがないことは未実証である。G13入力、G600 Feature Report、G600 live input routeが成功するまで、ユーザーモード製品の成立性はUnverifiedとする。最初のprobeは署名済みカーネルドライバーなしで行い、driver要否を実測で判定する。

根拠は次の2点が強い。

1. G13は、LGSプロファイルを消去した状態でベンダー定義HID Usage Page `0xFF00`の8バイト入力レポートを`WM_INPUT`から取得し、22個のGキー、スティック、補助ボタン、3レイヤーを扱うWindows実装が公開されている。
2. G600はオンボード3プロファイルをReport ID `0xF3`〜`0xF5`、現在プロファイルを`0xF0`で扱う154バイトのFeature Reportが解析され、libratbagで読み書き実装されている。G-Shift、DPI、ポーリングレート、RGBもレポート内に含まれる。

カーネルドライバーが必要になるのは「デバイスを区別したまま標準入力をOS全体から確実に抑止する」「物理入力と同等に見える仮想キーボード／マウスを提供する」場合である。最小製品では、G13のベンダー定義入力とG600のオンボード割当を利用して、この要求を避けられる可能性が高い。

## 確認済み・推定・未確認

### 確認済み

- 実機のVID/PID、Windowsデバイス構成、LGS仮想キーボード／バスの存在はオーナー実測済み。
- G13の入力レポートは8バイトで、Report ID、X/Y、G1〜G22のビット列、補助キー群を含む公開実装が複数存在する。
- Windows上でG13を`WM_INPUT`から読むMIT実装が存在する。ただし、公開実装はUsage Page全体を登録しており、製品実装では`GetRawInputDeviceInfo`による厳密なVID/PIDフィルタが必要。
- G13のM LEDとRGBは5バイトのHID class Set Report例、LCDは160×43の1-bit画像＝960バイトに32バイトヘッダーを付けた992バイトのinterrupt OUT転送例が存在する。
- G600は3個の154バイトオンボードプロファイルを持つ。各プロファイルに通常20ボタン、G-Shift側20ボタン、RGB、DPI、レポートレートが含まれる。
- G600はWindows上で標準mouse、keyboard、vendor HID、Logitech Gaming HID、LampArrayとして列挙されている。
- Windowsはユーザーモードから`ReadFile`、`HidD_GetFeature`等でHIDレポートを取得できる。Raw Inputはバックグラウンド受信とデバイスハンドル識別を提供する。
- `SendInput`はユーザーモードでキーボード／マウス入力を挿入できるが、UIPIにより高いintegrity levelのアプリへは届かない場合がある。また挿入イベントには識別可能なフラグが付く。
- Windows VHFによる仮想HID sourceは現行仕様ではkernel modeのみ。
- G600のLampArrayはWindows Dynamic Lightingの公式APIで制御できる可能性がある。実機列挙は確認済みだが、OpenLogicoolからの制御成功は未確認。

### 強い推定

- G13のキー／スティック入力はLGS常駐なしでユーザーモードRaw Inputから取得できる。
- G600の`0xF0`、`0xF3`〜`0xF5` Feature ReportはWindows標準HID経路またはhidapiから取得できる。
- G600の単純なキー割当とG-Shiftは、オンボードメモリへ書けばLGS常駐なしで動く。
- アプリ別プロファイル、設定保存、常駐、レイヤー処理は通常のWindowsユーザーモードだけで実装できる。
- G600 LEDは独自Feature ReportまたはLampArrayのどちらかで制御できる。競合を避けるため、最初はLampArrayを優先して可用性を実測する。

### 未確認

- 現在のG13がLGS停止状態でUsage Page `0xFF00`の入力をそのまま出すか。
- 「LGSプロファイルを消去する」の正確な手順、デバイス側状態への影響、復元方法。
- Windows `HidUsb`を維持したままG13 LCDのinterrupt OUTへ書けるか。libusb/WinUSBへドライバーを差し替える必要があるか。
- G13のRGB/M LEDがWindowsのHID Feature/Output Report APIで通るか。
- G600 Feature Reportを、現行Logitech driverやDynamic Lightingと共存したまま排他的でなく開けるか。
- G600プロファイル書込みの耐久性、更新反映タイミング、チェックサム／未知byteの意味。未知byteは必ずread-modify-writeで保持する必要がある。
- G600のLampArray制御権、バックグラウンド制御に必要なMSIX identityの実運用。
- `SendInput`を対象ゲームが受理するか。アンチチート互換性はゲームごとの実測事項であり、保証不能。

## 既存製品・OSSの比較

| 資産 | G13 | G600 | Windows | 得られるもの | 判定 |
|---|---:|---:|---:|---|---|
| LGS 9.04.49 | 対応 | 対応 | 対応 | 現行の完全基準、プロファイル、仮想入力 | 2022年以降更新なし。比較対象として保持 |
| Logitech G HUB | 非対応とみられるが公式根拠未確認 | 利用事例あり | 対応 | G600設定 | G13代替にならず、本計画の代替には不足 |
| `dmicsa/logitech-g13-ahk-bind` | 入力・3層 | なし | 対応 | Raw Inputの成立証拠、MIT | G13 MVPの最重要先行例 |
| `khampf/g13`系 | 入力、RGB、M LED、LCD | なし | Linux | G13プロトコル資産 | ライセンス不明のため、事実だけ参考に独自実装 |
| libratbag / Piper | なし | 専用driverあり | Linux | G600プロファイルの完全な構造 | MIT部分は再利用候補。Windows UIそのものではない |
| `python-hidapi-logitech-g600` | なし | 読み書き | macOS中心 | hidapiでの可搬性確認 | ライセンスなし。コードはコピーしない |
| OpenRGB | なし | issueのみ | 対応 | RGB基盤 | 現状はG600代替ではない |
| Solaar | なし | support request止まり | 主にLinux | descriptor情報 | 代替ではない |
| AutoHotInterception | 汎用 | 汎用 | 対応 | デバイス別の抑止・再送 | 署名済み第三者driver依存、既知のID枯渇問題。MVP依存にしない |
| HidHide | 汎用HIDの可視性制御 | 汎用HIDの可視性制御 | 対応 | 重複入力回避 | カーネルfilter。入力変換器ではなく、必要性実証後の候補 |

## 機能別実現性

| 機能 | G13 | G600 | 方式 | 難易度／注意 |
|---|---|---|---|---|
| デバイス認識 | 高 | 高 | SetupAPI / Raw Input device info / HID enumeration | 低 |
| キー／ボタン読取 | 高 | 中 | G13 Raw Input vendor report。G600はオンボードkey化またはRaw Input | G600のソフト割当方式は要実測 |
| スティック | 高 | 該当なし | G13 report X/Y、dead zone | 低〜中 |
| G-Shift相当 | ソフト3層 | 高 | G13内部state、G600オンボードsecond mode | 低 |
| アプリ別プロファイル | 高 | 高 | foreground executable監視＋設定切替 | 低。G600 hardware slotは3個制限 |
| 通常キー／マウス出力 | 高 | 高 | `SendInput` | 低。ただしUIPI・injected flagあり |
| 複雑なマクロ | 中 | 中 | ユーザーモードscheduler＋`SendInput` | アンチチートと倫理面から後回し |
| G13 RGB/M LED | 中〜高 | 該当なし | HID Set Report | 実機write試験が必要 |
| G13 LCD | 中 | 該当なし | 960-byte framebuffer + interrupt OUT | Windows endpoint accessが最大の未確認点 |
| G600 RGB | 該当なし | 高 | LampArrayまたはprofile Feature Report | LampArray制御権を先に試す |
| G600 DPI/report rate | 該当なし | 高 | profile Feature Report | 既存プロトコルあり |
| G600オンボードmemory | 該当なし | 高 | `0xF3`〜`0xF5` read-modify-write | 変更前backupと未知byte保持が必須 |
| 標準入力の完全抑止 | 不要の可能性大 | 条件付き | filter driver / Interception | 必要性が実証されるまで実装しない |
| 仮想HID | 不要の可能性大 | 条件付き | KMDF + VHF | 署名・配布負担が最大 |

## 本当のブロッカー

### 1. G13のWindows出力endpointアクセス

入力はRaw Inputでほぼ解決している。残る技術的な未知はLCD/RGBへの書込みであり、特にLCDのinterrupt OUTを現在の`HidUsb`のまま開けるかが分岐点である。WinUSBへのdriver差替えは標準HID入力を壊し得るため、最初の手段にしない。

### 2. G600の既存driverとの共存

Feature Report自体は解析済みだが、Windows上でLGS/G HUB/Logitech Gaming HID/Dynamic Lightingが開いている状態でも共有アクセスできるかは未確認。まずread-onlyで確認し、競合プロセスを止める条件を特定する。

### 3. 入力抑止が本当に必要か

Raw Inputの`RIDEV_NOLEGACY`は登録したアプリ内のlegacy messageを止めるもので、OS全体・特定デバイスだけの抑止ではない。low-level hookはイベントを止められるが物理デバイスを識別できない。したがって、G600が元キーを発生させたままソフト再割当する設計で「特定デバイスだけ元入力を消す」ならdriverが必要になる。

このブロッカーは、G600オンボードをF13〜F24等の予約用途へ割り当て、ユーザーモードで変換するか、オンボードprofileを直接切り替えることで回避できる可能性がある。

### 4. ゲーム／アンチチートの入力受理

`SendInput`はWindowsの通常APIだが、物理入力と同一ではなくinjectedとして識別可能である。各ゲームが受理するかは実測が必要。OpenLogicoolはゲームprocessへのinject、memory操作、anti-cheat回避を行わない。

## ブロッカーを潰す実験

すべて小さく、read-onlyから始める。書込み実験前にLGS設定と該当Feature Reportを保存する。

### 実験A: G13 Raw Input inventory（最優先）

1. `GetRawInputDeviceList`と`GetRawInputDeviceInfo`で全TLCを列挙する。
2. `VID_046D&PID_C21C`だけを選ぶ。
3. LGS稼働中／停止後の各状態で8バイトreportを記録する。
4. G1〜G22、スティック四方向、補助ボタンを一回ずつ操作しbit mappingを確定する。

受入: LGS停止状態で全主要入力を一意に識別できる。できなければ、LGS profile状態を変更する前に差分と復元方法を決める。

### 実験B: G600 Feature Report read-only

1. HID TLCを列挙し、Feature Report長とreport IDを取得する。
2. `0xF0`、`0xF3`、`0xF4`、`0xF5`を読む。
3. 各154バイトを二度読みし、libratbag構造へdecodeする。
4. LGS稼働中／停止後で可否を比較する。

受入: 現在profileと3 profileを一貫して読み出せ、未知byteを含む完全backupを作れる。

### 実験B2: G600 live input route

1. 全HID TLC、Raw Input、legacy keyboard／mouse messageを同時記録する。
2. 通常20 button、G-Shift側20 button、wheel、tilt、profile操作のdown／upを個別に記録する。
3. 各物理操作をinstance単位で一意に識別できるか、元入力と変換後入力が重複するかを判定する。
4. 3 onboard slot直接利用、中間usageのuser-mode変換、物理入力抑止＋virtual HIDのどれを採るか決める。

受入: G600のapp別profileを成立させる入力route、profile数、元入力、driver要否を一つの方式として記録できる。未決定のまま製品UIとprofile契約を固定しない。

### 実験C: G600の可逆な最小write

1. 実験Bのbackupを保存する。
2. active profile `0xF0`だけを切り替える。
3. active profile操作と154-byte永続profile writeを別capabilityとして扱う。
4. LED色1項目の論理変更でもprofile report全体を書き戻すことを前提に、未知byteを保持してread-modify-writeする。
5. byte diff、再読、再接続、power cycle後の状態を確認し、元reportを完全復元する。

受入: 再試行で成功扱いにせず、1回のrequestと再読で成否を判定できる。失敗時は原因解明まで追加writeしない。

### 実験D: G13 output

RGB、M LED、LCDの順に分離して試す。LCDではWinUSB差替えをせず、標準HID collection handleからOutput Reportまたは`WriteFile`で到達できるかだけを確認する。

受入: 各機能を独立した1 requestで変更し、切断・再接続後も標準入力が維持される。

### 実験E: 入力出力互換性

`SendInput`による単一キーdown/upを、Notepad、管理者権限アプリ、利用対象ゲームで個別確認する。アンチチート採用ゲームは利用規約を確認し、禁止される自動化は試さない。

受入: 対象ごとに「受理／UIPIで不達／ゲーム側で不達」を記録する。全ゲーム互換を受入条件にしない。

## 推奨する最小試作

### 技術構成

- C# / .NET 10 Windows desktop
- 1つの常駐processとmessage-only window
- `G13Device`: Raw Input受信、厳密なVID/PID filter
- `G600Device`: HID Feature Report read-onlyから開始
- `ProfileResolver`: foreground executable名からprofileを選択
- `MappingEngine`: key down/up、layer、単純chordのみ
- `InputEmitter`: `SendInput`
- JSON設定とtray UI

最初からWindows Service、データベース、plugin system、kernel driver、MSIX、LCD rendererを入れない。Dynamic Lightingのbackground制御にMSIX identityが必要なら、LED Phaseで追加する。

### MVP受入条件

以下は実験B2でuser-mode routeが成立した場合の候補である。B2がonboard限定またはdriver必要と判定した場合、G600のprofile数と割当能力を制限して表示し、この全項目を達成したとは扱わない。

1. LGSを終了した状態でG13/G600をVID/PID付きで認識できる。
2. G13のG1〜G22とスティック四方向を欠落なく表示できる。
3. G13で単一キー、chord、3レイヤーを割り当てられる。
4. G600の3オンボードprofileをread-onlyで表示できる。
5. 可逆write実験成功後だけ、G600のG-Shiftを含む単純割当を保存できる。
6. foreground appの切替で設定profileが変わる。
7. 再起動後に設定が復元され、LGSの仮想keyboard／busへ依存しない。
8. kernel driverをインストールしない。

LCD、LED演出、複雑マクロ、オンボード書込みUI、driverはMVP後に、個別実験が成功したものだけ追加する。

## 法務・配布上の注意

- `libratbag`のG600 driver fileとG13 AHK実装はMITなので、コードを利用する場合はcopyright noticeとlicenseを同梱する。
- `python-hidapi-logitech-g600`と`khampf/g13`はリポジトリの明示ライセンスを確認できない。通信上の事実を参考に独自実装し、コード・コメント・配列表現をコピーしない。
- プロトコル値そのものとコード表現の著作権上の扱いは同一ではない。実装時は自前の型・命名・テストvectorで再構成し、出典を残す。
- `Logicool`、`Logitech`、`G13`、`G600`は他社ブランド／製品識別子である。公開時は非公式・非提携を明示し、ロゴや公式UI意匠を使わない。`OpenLogicool`という公開名称は提携誤認リスクがあるため、release前に名称変更を検討する。
- リバースエンジニアリングの相互運用例外は法域と具体的行為で要件が異なる。暗号回避やLGS binaryの再配布は行わず、合法に所有する実機との相互運用に必要な通信観測と独自実装に限定する。本節は法的助言ではなく、公開前に対象法域の専門家確認が必要。
- driverを配布する場合、Microsoft signing、EV certificate、Partner Center、HLK等の運用負担が発生する。これがユーザーモードMVPを優先する最大の配布上の理由である。

## 推奨裁定

実装Goの前に、実験A、B、B2とMigration Safety Gateを行う。A/B成功はG13入力候補とG600 read-only設定基盤の成立を示すが、無制限のapp別G600割当の成立を意味しない。B2でuser-mode、onboard限定、driver分岐のいずれかを決めてから対応claimと製品scopeを裁定する。G13 LCD、G600 write、kernel driverは同時に始めない。
