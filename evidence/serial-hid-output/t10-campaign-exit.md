# t10 Serial HID Output campaign終端受入

- 日付: 2026-08-23
- Task: `t10-campaign-exit`
- 対象: `22d2bef17227f09ca86a470f5baf778a1304fb4e..HEAD`
- 実測機: `FOX`、Windows 11 build 26200 / x64
- board: SparkFun Pro Micro ATmega32U4 5V / 16MHz
- firmware: 1.0.0、sketch SHA-256 `698364A4852F3CFC55B3419253188EB1B43F8EA0956CAB8A6966BEC540B10A22`

## t00〜t09受入

| Task | feature commit | 判定 | 主証拠 |
|---|---|---|---|
| t00 | `7c977ab` | 確認済み | G600未割当G2〜G5 baseline保持、G6〜G20無動作 |
| t01 | `f8e7468` | 確認済み | protocol v1、golden vector、ACK後commit、no retry/fallback |
| t02 | `d1e9bbd` | 確認済み | firmware source、固定toolchain compile |
| t03 | `36e7a55` | 確認済み | host protocol session／emitter、terminal fault |
| t04 | `f7fbdf6` | 確認済み | resident output ownership、handled stop順序 |
| t05 | `6f5b22e` | 確認済み | SetupAPI、PnP identity、requested/active route UI |
| t06 | `ce501cf` | 確認済み | C#／firmware共通vector、fake lease／partial reader |
| t07 | `a951375` | 確認済み | flash、direct HID、ALL_UP、lease、power cycle |
| t08 | `000cfde` | 確認済み | G13／G600 live、layer／profile、抜線、latency |
| t09 | `015356a` | 確認済み | hard kill、NIKKE単一押下観測 |

旧`passed=false` probeは失敗記録として保持し、最終成立へ流用していない。

- `serial-hid-live-smoke-20260823-135800-688.json`: Windows typematicを別down漏れと誤判定した旧observer。製品traceの各edgeは1回。
- `serial-hid-live-smoke-20260823-143629-743.json`: 機能条件は成立、旧`Thread.Sleep(1)` latencyだけ不合格。
- `serial-hid-live-smoke-20260823-145222-263.json`: 4 edgeの最大値をp99扱いした旧gate。
- `serial-hid-game-observation-20260823-171815-713.json`: Windows hook依存の旧失敗判定。

確定証拠は200 edgeの`serial-hid-fastpath-latency-20260823-144637-361.json`と、ACK trace／game反応／hook未観測を分離した`serial-hid-game-observation-20260823-172401-688.json`である。

## Exit監査で見つけた不足と修理

Exit条件9の「6KROとunsupported usageをUI／support matrixへ明示」を再確認し、runtime拒否は実装済みだが、設定画面と公開matrixの事前表示が不足していることを検出した。

修理はDesktopの表示責務だけに限定した。`SerialHidSettingsPresentation.LimitNotice`を設定画面と`InputStudioSupportMatrix`で共有し、通常キー同時6個、7個以上、mouse移動／wheel／特殊キー非対応、部分送出なしを一つの文言で表示する。protocol、emitter、host ownershipには変更を加えていない。

修正後focused:

- Desktop: 78件成功、失敗0
- Architecture: 6件成功、失敗0
- build: warning 0、error 0

## related gate

受入監査前の関連gateは次の全件greenだった。

- Input 143
- Host 109
- Desktop 78
- Architecture 6
- Devices.G13 10
- Devices.G600 69
- Probe 20
- firmware compile: flash 6080 / 28672 byte、RAM 256 / 2560 byte

最初のArchitecture実行は、一時runnerをrepo外へ置いたため`AppContext.BaseDirectory`からrepo rootを発見できず6件失敗した。製品欠陥ではなくharness配置違反と確認し、ignoredの`obj/xunit-runner/`へ出力して同じ6件をgreenにした。VSTest外殻はhost固有のSocket 10013で利用不能のため、xUnit公式`AssemblyRunner`でassembly全件を実行した。

## full regression

表示不足と監査指摘の診断文を修理した最終sourceを`dotnet build OpenLogicool.sln --no-restore`でbuildし、warning 0／error 0を確認した。

direct runnerの出力先解決に2回誤りがあった。1回目は長いTFM名の3番目projectへ到達せず停止し、2回目はHost／Probeの古い短縮TFM DLLを拾って754件で終わった。どちらも不完全実行として不採用にした。

各test projectの`TargetPath`をMSBuildから取得して全20 projectを最初から実行した確定結果:

- total: 829
- failed: 0
- skipped: 0
- Desktop: 78
- Host: 109
- Input: 143
- Probe: 20

## 未追跡probe-outputの仕分け

次のJSONは診断中のraw出力または重複再走であり、確定証拠に使わずcommitしない。ファイル自体はオーナー領分として削除・変更しない。

- `g13-adapter-smoke-20260823-164449.json`: G13入力経路の重複再確認。Serial HID Exitの成立証拠には不使用。
- `serial-hid-direct-smoke-20260823-162248-723.json`: Hello timeoutの失敗記録。後続の確定t07証拠で置換。
- `serial-hid-direct-smoke-20260823-162920-307.json`: direct smoke成功の途中再走。確定t07証拠と重複。
- `serial-hid-direct-smoke-20260823-164233-444.json`: direct smoke成功の途中再走。確定t07証拠と重複。
- `serial-hid-direct-smoke-20260823-170931-897.json`: direct smoke成功の再確認。確定t07証拠と重複。
- `serial-hid-hard-kill-20260823-162140-958.json`: host child READY前のHello timeout失敗。後続の確定t09証拠で置換。
- `ui-test-scenario-20260822-094519-943.json`: campaign開始前から存在するオーナー所有物。対象外。

## 独立監査

Peertable room `OpenLogicool`の監査席へ依頼したが回答がなく、オーナー指示により別sessionのClaude Fable 5へ代替した。実装、firmware、証跡、diffを対象とするread-only反証監査でP0なし、技術本体はCLOSE相当と判定された。

検出されたAGENTS現在地未反映、未追跡probeの仕分け漏れ、経路非中立の診断文は終了前に修理した。詳細は[Fable監査証拠](t10-fable-exit-audit.md)。

## 未確認・非対応

- Windows低レベルhookでのNIKKE前面中Esc観測: 未確認
- 他game／他anti-cheat／長時間運用: 未確認
- Windows 10／ARM64／別board: 未確認
- raw USB report byteの独立capture: 未確認
- NKRO、mouse移動、wheel、consumer control: 非対応
- LCD／LGS applet／power mode: 未確認、本campaign対象外
- public installer／firmware auto-update／署名済みartifact: 本campaign対象外

未確認をSupportedへ昇格せず、製品公開claimは`Partial LGS Replacement`のまま維持する。
