# t02-windows-input-recorder 証跡

工程正本: Lattice plan `phase14-product-completion` / task `t02-windows-input-recorder`
担当: koharu（設計・実装） / 記録日: 2026-08-29

## 工程指定（正本の逐語）

> Windows環境別mouse／keyboard recorderと、既存G13／G600 edge observerを実装する。対象game foregroundだけを記録し、focus喪失pause、client座標正規化、key down／upの有限化、記録・再生排他、fast path非blockingをfocused testとWindows native self-windowで確認する。

## 結論

機能中核をすべて実装し、focused test 47件と**Windows native self-windowの実OS入力実測**で確認した。実測はオーナーがNIKKEをプレイ開始する前に取得できた1本（`probe-output/demonstration-recorder-smoke-20260829-112607-825.json`）で閉じている。以後はオーナー指示（room #1049 / #1051）によりNIKKE・foreground・mouse・keyboard・Nanoへ一切触れていない。

## 何を作ったか

### 1. 共通contract（`Contracts/Playbooks/DemonstrationRecorderContracts.cs`・新規）

共通側は**操作eventとlifecycleだけ**を持つ。`DemonstrationInputEdge`（意味付け前の生入力edge）、`DemonstrationScreenPoint`（desktop絶対座標。正規化までの運搬用で原本には入らない）、`IDemonstrationInputSink`（非blocking・例外を投げない受け口。hook procedureとfast path workerから直接呼ばれる）、`IDemonstrationInputCollector`（Start／Stopだけ）、`IDemonstrationObservationRuntime`（**入力dispatchのmethodを持たない**観測面。記録器は構造上入力を出せない）。

`IGameInteractionRuntime` は `WaitStableAsync` / `Compare` をこの面へ移して継承する形にした。**既存実装のsignatureは不変**で、`ProductGameExplorerRuntime` 等はadapterなしでそのまま満たす。

### 2. 記録器（`Playbooks/DemonstrationRecorder.cs`・新規）

- **beforeの束縛**: 記録開始時に一度観測し、以後 `RefreshObservationAsync` で更新される「current observation」へ押下時点で束縛する。押下直前だけ観測してbeforeを作る経路は存在しない。
- **有限化**: downは保留し、**対になるupが来た押下だけ**が操作になる。押しっぱなしのまま停止／focus喪失した押下は `DiscardedHeldPresses` に数える。
- **click と drag**: 押下点と解放点がscreen座標で完全一致ならclick、違えばdrag（解放点を `DragDestinationNormalized` へ）。閾値を置かないので任意の許容幅を発明していない。
- **wheel**: 段数付きのscroll操作として1件で書く。
- **focus**: `ObserveForegroundAsync` が対象app以外（またはidentity取得不能＝null）を見たらFocusLostを書いてPausedへ入り、保留中の押下を捨てる。復帰は対象appのときだけで、**新しいObservationを取り直してから**FocusRegainedを書く。pause中の入力は原本へ入らない。
- **排他**: `StartAsync` は `DemonstrationRecordingGate` を取ってからでないと開始しない。捨てた入力も隠さず `DemonstrationRecorderCounters`（pause中無視／frame外／対にならない解放／押しっぱなし破棄）で公開する。

### 3. 排他とfan-out（`Playbooks/DemonstrationRecordingGate.cs`・新規）

`DemonstrationRecordingGate` が記録と再生を相互排他する。**low-level hookのinjected flagでNano出力と物理入力を区別しない**（旗は送信元を確定できない）ため、自己記録はこの排他で防ぐ。`DemonstrationDeviceEdgeQueue`（`IPhysicalInputObserver` 実装）はfast path workerからのenqueueを待たず、超過分は捨てて `DroppedCount` に数える。

### 4. fast path非blocking fan-out

`IPhysicalInputObserver`（`Contracts/Devices/Shared`）を追加し、`FastPathPump` に**任意**の `inputObserver` を足した。`RunOnce` はmapping解決の後に `OnInput` を1回呼ぶだけで、送出順序も所要も変えない。observerを渡さない既存構成の挙動は不変。

### 5. Windows環境別adapter

- `Host/WindowsDemonstrationInputCollector.cs`（新規）— WH_MOUSE_LL／WH_KEYBOARD_LL を専用threadのmessage loopへ張る。設置の成否と呼び出し回数（`KeyboardHookCalls` / `MouseHookCalls`）を観測点として公開する。
- `Host/WindowsDemonstrationInputEdgeFactory.cs`（新規）— **Windows message → 生入力edge の翻訳を純関数へ分離**した。翻訳規則が実foregroundなしで検証でき、実機を要するのは「OSが本当にhookを呼ぶか」だけになった。
- `WindowsGameInteractionCoordinateMapper.TryMapScreenToNormalized`（screen→client frame正規化。frame外はnull）。
- `OutputTokens.TryGetKeyName`（既存KeyNames表の逆引きだけで対応表を二重に持たない。表に無いkeyは `Vk:0xNN` のまま丸めない）。

## 試験内容と試験結果

すべてWindows native（net10.0-windows / .NET SDK 10.0.400）。

### focused test（新規47件・全green）

| suite | 件数 | 確認した内容 |
| --- | --- | --- |
| `Playbooks.Tests/DemonstrationRecorderTests` | 10 | 押下時点の観測とframeへの束縛（画面が動いてもbeforeは押下時点・正規化[0.3,0.2]）／upで初めてKeyTap／押しっぱなしのまま停止は操作0件・破棄1件・gateはFreeへ／解放点が違えばdrag／wheelはscroll（縦-3）／focus喪失で保留破棄・pause中4件は計数のみ・復帰は新Observationへ束縛／identity不能はpath null／frame外は計数のみ／記録中は再生を開始できず停止後は取れる／開始失敗でgateを残さない |
| `Playbooks.Tests/DemonstrationDeviceEdgeQueueTests` | 3 | FIFO順／容量超過時のdrop計数と空き復帰／並行4 producer×500件で消失0 |
| `Input.Tests/FastPathDemonstrationObserverTests` | 3 | 全入力がobserverへ順どおり届きemit内容は不変／observer無しの挙動不変／未知deviceはfaultし未処理入力はobserverへ渡らない |
| `Host.Tests/WindowsDemonstrationInputEdgeFactoryTests` | 21 | WM_KEYDOWN/SYSKEYDOWN/KEYUP/SYSKEYUP の翻訳と `Key:Esc`／key edgeに座標を付けない／表に無いkeyは `Vk:0xF0` のまま／拡張flagが名前引きに参加／mouse 3ボタン×down/upがscreen座標を運ぶ／X button番号はmouseDataの上位語／wheel 120・240・-360→1・2・-3段／1段未満のdeltaと移動だけは記録しない |
| `Probe.Tests/DemonstrationRecorderSmokeJudgementTests` | 4 | probeの判定を取得済みlive実測JSONへ再適用（下記） |

### 関連test（module単位・最終確認）

`IGameInteractionRuntime` の継承構成を変えたため横断で確認した。

```
Playbooks 194 / Input 160 / Host 274 / Exploration 63 / Desktop 98
Architecture 8 / Conformance 61 / Persistence 55 / Domain 83 / AI 43
Perception 32 / Probe 65        合計 1,136件 全green
solution build 警告0・エラー0
```

Architecture testがgreenなので依存行列は壊れていない。full regressionはこの工程では実行していない（t09の範囲）。

### Windows native self-window: 実測で成立

probe `demonstration-recorder-smoke` を追加した。self-windowのclient frameへclick／drag／Escape／wheelを起こし、hookが拾ったedgeと正規化を突合する。**送出側のSendInputはこのprobeの測定器であって製品経路ではない**——製品の出力はNano（Serial HID）だけで、記録器は入力を送らない。

採用実測: `probe-output/demonstration-recorder-smoke-20260829-112607-825.json`

- self-window client frame（screen座標）: Left=128, Top=151, Width=624, Height=441
- **hook呼び出し: keyboard=8 / mouse=8**（設置できても呼ばれない状態と区別する観測点）
- 観測edge 13件
- 送出との突合:

| 送出したもの | 観測されたedge | 正規化 |
| --- | --- | --- |
| 中央click（押下=解放） | PointerDown/Up `Mouse:Left` (440,371) / (440,371) | [0.5000, 0.4989] |
| drag（右下→中央） | PointerDown (596,481) → PointerUp (440,371) | [0.7500, 0.7483] → [0.5000, 0.4989] |
| Escape | KeyDown/KeyUp `Key:Esc`（ScreenPointなし） | — |
| wheel 1段 | Wheel `Mouse:Wheel` v=1 h=0 | [0.5000, 0.4989] |

- 送出していない `Key:Space` / `Key:R` / `Key:A` / `Key:U` も観測列に含まれる。low-level hookがdesktop全体の実入力を拾っている証拠であり、**合成した観測ではない**ことを示す。
- client frame外の点は `null`。観測したpointer down 2点はいずれもframe内で正規化できた。

**この実行でNGが1件出たが、原因はprobe側の判定バグで製品側ではない。** low-level hookはdesktop全体を拾うため送出したEscapeが観測列の先頭とは限らないのに `keyDowns[0]` を見ていた（実際の先頭は他appの `Key:Space`）。判定を「観測列の中に含む」へ直した。

**修正後の判定が保存済み実測に対して成立することは、手作業の突合ではなく機械で確認してある。** 判定を `DemonstrationRecorderSmokeJudgement.Evaluate`（観測列とclient frameだけから決まる純関数。live実行側も同じ関数を呼ぶ）へ出し、`DemonstrationRecorderSmokeJudgementTests` 4件が上記JSONへ同じ判定を再適用する。hookもSendInputも前面windowも使わない。

1. 修正後の判定12件すべてが保存済み実測に対して `Passed`。
2. 観測列に `Key:Esc` を含むが**先頭ではない**（元の判定バグの実体）。click対（押下点＝解放点）とdrag対（押下点≠解放点）が両方入っている。
3. 実測のkey edgeにScreenPointが無い。
4. client frameをずらすと同じ観測でも正規化checkが落ちる（判定が観測を無条件に通していない）。

**判定修正後のprobeを緑で1回通すlive再実行だけが未実施**で、これはオーナーのNIKKEプレイ中に前面・入力へ触れない指示（room #1049 / #1051）に従っている。製品側の成立はこの実測で足りており、live再実行が確かめるのは「同じ判定をその場でもう一度走らせても同じ」ことだけである。

#### 途中で当たった環境条件（記録）

最初の実行はhook呼び出し0で失敗した（`probe-output/demonstration-recorder-smoke-20260829-111326-093.json`）。設置は成功（非0 handle・`GetLastError`=0）なのに呼ばれず、本製品に依存しない40行の最小再現でも `GetMessage`／`PeekMessage` の両方で0だった（実装側ではない）。既存 `sendinput-accept` も同条件で前面化できず（`probe-output/sendinput-accept-t02-env-check-20260829-021527-143.json`）、前面はNIKKE（pid 34512）が保持していた。

**前面が普通のapp（ChatGPT）へ移った瞬間に走らせたらkeyboard=8 / mouse=8で全項目が取れた**ので、原因は「保護appが前面を保持している間、low-level hookがその入力を観測できないこと」で確定。probeはこの条件を別経路へ逃げず「未確認」としてJSONと標準エラーへ書いて止まる。

## 自己監査で直したもの

1. **hook procedureの中身を純関数へ分離**した。直書きのままでは実foregroundが取れないと翻訳規則を1行も検証できなかった。
2. **key名の思い込みを実表と突合**した。`Key:Escape` と書いていたが既存 `OutputTokens` の表は `Esc`。逆引きは新表を作らず既存表を引く。
3. **probeの失敗を握りつぶさない**ようにした。前面化できない条件を「未確認」として書き、別経路へ落とさない。
4. **probeの判定を観測の実態に合わせた**（`keyDowns[0]` が自分の送出だと決めつけていた。製品側の変更なし）。
5. **その判定を純関数へ出した**。live実行の中に埋まっていると正しさを手で読むしかないので、再適用testで機械の確認にした。
6. **`DemonstrationFocusChange.ForegroundApplicationPath` を `string?` へ変更**した（t01の契約）。identityを取得できない区間が実在する（本repoで実測済みのUnknown Application）のに非nullable必須だと別の値で埋めるしかない。憲章16どおりt01をreopenせず現工程の修正として直し、validatorのFocusLost分岐も「nullは可・空白文字は不可・対象app自身への喪失は不可」へ更新。sora（t03担当）へは#1027で通知済み。

## 4値（この工程の範囲）

- 対象game foregroundだけを記録する: **確認済み**（focused test。`ObserveForegroundAsync` の対象app一致で判定）
- focus喪失pause／復帰時の新Observation: **確認済み**（focused test）
- client座標正規化: **確認済み**（記録器のfocused test＋`TryMapScreenToNormalized` の境界。原本に絶対座標のfieldが無い）
- key down／upの有限化: **確認済み**（対になった押下だけが操作。押しっぱなしは破棄・計数）
- 記録・再生排他: **確認済み**（gateのfocused test。**再生側Host intentsへの結線はt04の範囲**で未結線）
- fast path非blocking fan-out: **確認済み**（全入力がobserverへ届き、emit内容とfault挙動は不変）
- Windows message→edge翻訳規則: **確認済み**（純関数21ケース）
- OSが実際にhookを呼び実入力edgeを拾うこと: **確認済み**（self-window実測。keyboard=8 / mouse=8、click・drag・Escape・wheelを全て観測）
- self-windowのclient frameでの実座標正規化: **確認済み**（(440,371)→[0.5000,0.4989]、(596,481)→[0.7500,0.7483]、frame外はnull）
- 修正後の判定が保存済み実測に対して成立すること: **確認済み**（再適用focused test 4件。hook・SendInput・前面windowを使わない）
- 判定修正後のprobeを緑で1回通すlive再実行: **未実施**（オーナーのNIKKEプレイ中につき前面・入力へ触れない指示 room #1049 / #1051 に従った）
- 保護appが前面を保持している間の実OS入力観測: **非対応**（low-level hookの構造的制約。probeは未確認として止まる）

## 変更file（commit d44f96c）

新規（`src/OpenLogicool.` 省略）: `Contracts/Playbooks/DemonstrationRecorderContracts.cs`／`Playbooks/DemonstrationRecorder.cs`／`Playbooks/DemonstrationRecordingGate.cs`（gateとfan-out queue）／`Host/WindowsDemonstrationInputCollector.cs`／`Host/WindowsDemonstrationInputEdgeFactory.cs`／`Probe/DemonstrationRecorderSmoke.cs`／`Probe/DemonstrationRecorderSmokeJudgement.cs`（probe判定の純関数）

既存への追加・変更: `Contracts/Playbooks/DemonstrationSessionContracts.cs`（focus pathをnullableへ・validator更新）／`Contracts/Exploration/GameInteractionContracts.cs`（WaitStable・Compareを `IDemonstrationObservationRuntime` へ移設）／`Contracts/Devices/Shared/DeviceContracts.cs`（`IPhysicalInputObserver`）／`Input/FastPathPump.cs`（任意のobserver fan-out）／`Input/OutputTokens.cs`（`TryGetKeyName`）／`Host/WindowsGameInteractionCoordinateMapper.cs`（screen→normalized）／`Probe/Program.cs`（command登録）

test（すべて新規・`tests/OpenLogicool.` 省略）: `Playbooks.Tests/DemonstrationRecorderTests.cs`／`Playbooks.Tests/DemonstrationDeviceEdgeQueueTests.cs`／`Input.Tests/FastPathDemonstrationObserverTests.cs`／`Host.Tests/WindowsDemonstrationInputEdgeFactoryTests.cs`／`Probe.Tests/DemonstrationRecorderSmokeJudgementTests.cs`

証跡: `evidence/phase14-product-completion/t02-windows-input-recorder.md`（本書）
