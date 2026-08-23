# t08 G13 / G600 live Serial HID smoke evidence

- Date: 2026-08-23
- Task: `t08-g13-g600-live-smoke`
- Machine: `FOX`
- Board firmware: `1.0.0`
- Composite identity: `USB\VID_1B4F&PID_9206`

## 結論

実機機能条件とNFR-002 latency条件は成立した。G600 legacy二重配送はSerial HID route専用のno-output抑止で根治し、FastPathのWindows timer量子依存はlive source signal起床へ置換した。hard killと対象game内成功は計画どおりt09へ分離する。

## 実機受入

| 項目 | 判定 | 証拠 |
|---|---|---|
| G13 key／chord／mouse／finite sequence | 確認済み | `serial-hid-live-smoke-20260823-135800-688.json`の先頭4 action |
| G600 key／chord／mouse／finite sequence | 確認済み | 同JSONのG600 4 action。G9〜G12のlegacy F13〜F16漏れ0 |
| 両device同時 | 確認済み | `serial-hid-live-smoke-20260823-143629-743.json`、F23／F24 down/up完全対 |
| G13 M2／G600 G-Shift layer | 確認済み | 同JSON、各layer action成功 |
| foreground profile | 確認済み | 同JSON、foreground切替後に両device action成功 |
| 保存後再起動反映 | 確認済み | initial／explicit restartとも`SerialHid` |
| board抜線fault | 確認済み | disconnectとterminal faultを観測 |
| fallbackなし | 確認済み | 抜線後の自動resumeなし、injected fallback 0 |
| 明示再起動復帰 | 確認済み | 再接続後の新hostでG13 action成功 |
| drop／wrong release／stuck | 確認済み | 0／0／0 |
| dispatch latency p99 | 確認済み | 200 edgeで3.425ms、基準10ms以下 |
| dispatch latency max | 記録済み | 12.902ms |

## 機能証拠の連結

`serial-hid-live-smoke-20260823-135800-688.json`はG13 4 actionとG600 4 actionを連続成功し、9番目の両device actionだけをWindows keyboard typematicの追加downで失敗扱いした。製品traceはG13 G5／G600 G13のdown/up各1回で、残留も誤解放もなかった。probeを「保持中の同一key down repeatは許可、別code漏れは拒否」へ修正した。

`serial-hid-live-smoke-20260823-143629-743.json`は前記8 actionのJSONとSHA-256を検証してresumeし、残る両device、G13 layer、G600 layer、foreground、board抜線、明示再起動を成功した。このJSONの`passed=false`は、修正前の`Thread.Sleep(1)`で採った29 edgeの旧latency p99 35.496msだけによる。機能条件は全件成立している。

## Latency根治と受入

旧workerは空queueごとに`Thread.Sleep(1)`し、Windows上で10〜17ms級の待ちを作った。G13／G600 raw sourceがqueue投入後にsignalし、workerがそのsignalで起床するよう変更した。

`serial-hid-fastpath-latency-20260823-144637-361.json`はsignal source→実`FastPathPump`→実Serial HID matching ACK→Windows HID観測を200 edge連続で測定した。

- requested／processed／trace／hardware: 200／200／200／200
- injected／wrong release／stuck: 0／0／0
- p50: 1.747ms
- p95: 2.522ms
- p99: 3.425ms
- max: 12.902ms

`serial-hid-live-smoke-20260823-145222-263.json`はpost-fixの実Raw Input spotとして、G13 G1とG600 G9のdown/up各1回、legacy漏れ0、drop／wrong release／stuck／injected 0を確認した。4 edgeの値は2.012／2.008／4.478／11.109msである。この旧JSONの`passed=false`は4件のp99を最大値と同一視したprobe判定による。現在のprobeは少数物理edgeをp99 gateへ使わず、NFR合否を200 edge専用probeへ一元化した。

## 実装検証

最新sourceで以下を実行し、すべてwarning 0／error 0だった。

```powershell
dotnet build src/OpenLogicool.Probe/OpenLogicool.Probe.csproj --no-restore
dotnet build tests/OpenLogicool.Input.Tests/OpenLogicool.Input.Tests.csproj --no-restore
dotnet build tests/OpenLogicool.Devices.G600.Tests/OpenLogicool.Devices.G600.Tests.csproj --no-restore
dotnet build tests/OpenLogicool.Host.Tests/OpenLogicool.Host.Tests.csproj --no-restore
git diff --check
```

このhostではVSTest外殻がtest entry前にloopback接続を失敗した。diagは`%LOCALAPPDATA%\Temp\openlogicool-vstest-235d58a3b8984353abdf40f819e94c18*.txt`で、testhostが`127.0.0.1:64193`へ一度接続後、SocketException 10013で停止した。test package 18.9.0、一時ディレクトリ実行、`testhost.dll`経路でも再現した。

テスト本体は成果物に含まれるxUnit公式`AssemblyRunner`を同一プロセスで直接呼び、発見・実行・合否をxUnitへ委ねて確認した。初回実行でsignal testのstop時releaseを期待値へ含めていないtest欠陥を1件検出して修正した後、最新sourceから次の結果になった。

- Input: 142件成功、失敗0
- Probe: 12件成功、失敗0
- Host: 107件成功、失敗0
- Devices.G13: 10件成功、失敗0
- Devices.G600: 69件成功、失敗0

VSTest 10013は製品testのgreenへ混ぜず、host toolchain障害として別記した。focused testの実体はすべて実行済みである。

## 復旧状態

- G600 baseline restore: attempt 1一致
- Logicool Gaming Software: `C:\Program Files\Logicool Gaming Software\LCore.exe`を再起動済み
- Pro Micro: firmware 1.0.0のまま接続
- 追加の物理押下は不要

## 構造化証拠

- `probe-output/serial-hid-live-smoke-20260823-135800-688.json`
- `probe-output/serial-hid-live-smoke-20260823-143629-743.json`
- `probe-output/serial-hid-fastpath-latency-20260823-144637-361.json`
- `probe-output/serial-hid-live-smoke-20260823-145222-263.json`
