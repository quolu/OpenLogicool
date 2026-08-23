# Serial HID Output campaign Exit Assessment

- 作成: 2026-08-23
- 計画正本: [serial-hid-output-campaign-plan.md](serial-hid-output-campaign-plan.md)
- 工程正本: Lattice plan `serial-hid-output`
- 証拠: [evidence/serial-hid-output/](../evidence/serial-hid-output/)
- 根拠4値: 確認済み／強い推定／未確認／非対応
- 製品公開claim: **Partial LGS Replacement**（変更なし）
- Exit判定: **成立（CLOSE）**

## Exit条件

| # | 条件 | 判定 | 根拠 |
|---|---|---|---|
| 1 | firmware sourceから再現可能にbuild／flashでき、復旧手順がある | 確認済み | 固定toolchain build、exact identity flash／verify、[運用手順](serial-hid-output-operation.md) |
| 2 | SendInput／Serial HIDを明示選択し、未接続時にfallbackしない | 確認済み | requested／active分離、保存前handshake、terminal fault、no fallback |
| 3 | G13／G600が同じSerial HID経路でkey、mouse button、chord、finite sequenceを送れる | 確認済み | t07 direct、t08 live |
| 4 | profile、layer、app-first、generation releaseを壊さない | 確認済み | t08両device／両layer／foreground／再起動、Input 143 green |
| 5 | handled stopとhard killの両方で250ms以内release | 確認済み | handled `ALL_UP`観測、hard kill 148.6321ms |
| 6 | drop 0、wrong release 0、stuck 0 | 確認済み | t08／t09 |
| 7 | dispatch latency p99 10ms以下、最大値記録 | 確認済み | 200 edge、p99 3.425ms、max 12.902ms |
| 8 | power cycle／抜線後にphantom downがない | 確認済み | composite PnP抜線／再列挙、unexpected down 0、明示再起動復帰 |
| 9 | 6KROとunsupported usageをUI／support matrixへ明示 | 確認済み | 設定画面と公開matrixが同じ制約文を共有、Desktop 78 green |
| 10 | focused／related green後のfull regressionがgreen | 確認済み | 最終source 20 project、829件、失敗／skip 0 |
| 11 | 実機証拠、操作／復旧手順、未確認範囲がrepoへ還流済み | 確認済み | t07〜t10 evidence、本書、運用手順、RAG索引、AGENTS現在地 |

## task受入

| task | feature commit | 判定 |
|---|---|---|
| t00 | `7c977ab` | 確認済み |
| t01 | `f8e7468` | 確認済み |
| t02 | `d1e9bbd` | 確認済み |
| t03 | `36e7a55` | 確認済み |
| t04 | `f7fbdf6` | 確認済み |
| t05 | `6f5b22e` | 確認済み |
| t06 | `ce501cf` | 確認済み |
| t07 | `a951375` | 確認済み |
| t08 | `000cfde` | 確認済み |
| t09 | `015356a` | 確認済み |
| t10 | 本書と[t10 evidence](../evidence/serial-hid-output/t10-campaign-exit.md) | 確認済み |

t08の旧`passed=false` 3件とt09の旧失敗1件は失敗記録のまま保持した。最終成立へ流用していない。後続の確定probeが何を置き換えたかは[t10 evidence](../evidence/serial-hid-output/t10-campaign-exit.md)へ逐語で記録した。

## support matrix

### Supported

| capability | 条件 | 根拠 |
|---|---|---|
| Serial HID v1のfirmware build／flash／再列挙 | Pro Micro 5V / 16MHz、firmware 1.0.0 | t02／t07 |
| G13／G600からのkey、mouse button、chord、finite sequence | Windows 11 build 26200 / x64 | t08 |
| layer／profile／app-first／保存後再起動 | FOX reference machine | t08 |
| handled stop／hard kill release | firmware lease 150ms、予算250ms | t07／t09 |
| NIKKEのG13 G1→Esc | 2026-08-23の1回押下・1回反応だけ | t09 |

### Unverified

- Windows低レベルhookでのNIKKE前面中Esc観測
- NIKKEの他action、他game、他anti-cheat、規約／account risk、長時間運用
- Windows 10、ARM64、別board／clock／bootloader
- raw USB report byteの独立capture
- public installer、firmware auto-update、署名済み公開artifact
- LCD、LGS applet、power mode、LGS全機能parity

### Unsupported

- Serial HID v1のNKRO
- 通常キー7個以上の同時押し
- mouse移動、wheel、consumer control
- runtime中のEmitter hot swap
- Serial HID fault時のSendInput fallback

## 検証

関連gateはInput 142、Host 109、Desktop 77、Architecture 6、G13 10、G600 69、Probe 20とfirmware compileがgreenだった。Exit条件9の表示不足修理後にDesktop 78、Architecture 6を、Fable指摘の診断文修理後にInput 143をfocusedで再確認した。

最終sourceをsolution buildしwarning 0／error 0。MSBuildの`TargetPath`から20 test assemblyを解決し、xUnit公式`AssemblyRunner`で829件を実行した。failed 0、skipped 0。

VSTestはこのhost固有のSocket 10013で起動不能だった。runnerの出力先解決を誤った不完全実行2件は合否に使わず、原因と確定実行を[t10 evidence](../evidence/serial-hid-output/t10-campaign-exit.md)へ残した。

## 独立read-only監査

Peertable room `OpenLogicool`の監査席へ依頼したが回答がなく、オーナー指示により別sessionのClaude Fable 5によるread-only反証監査へ代替した。同じmodel系列でcross-providerではない制約を明記した上で、計画の独立監査1回として採用する。

監査はP0なし、技術本体はCLOSE相当と判定した。終了前の指摘は、AGENTS現在地未反映、未追跡probeの仕分け漏れ、Serial HIDでもwatchdogを名指しする診断文だった。3件とも修理し、[監査証拠](../evidence/serial-hid-output/t10-fable-exit-audit.md)へ固定した。

## 公開claim

Serial HID v1は上記reference条件のcapabilityとしてSupportedに追加する。製品全体はLGS inventoryの未確認行が残るため、`InputStudioSupportMatrix.PublicClaim`を**Partial LGS Replacement**から変更しない。

NIKKEはG13 G1→Escの単一観測だけを確認済みとする。全NIKKE対応、全game対応、規約許可、anti-cheat回避を名乗らない。Windows hook未観測をACKまたはgame内反応へ読み替えない。
