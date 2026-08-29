# t07-product-journey-acceptance 証跡

工程正本: Lattice plan `phase14-product-completion` / task `t07-product-journey-acceptance`
担当: koharu（設計・実装） / 記録日: 2026-08-29

## 工程指定（正本の逐語）

> fakeと実SQLite、Windows self-window、Nano非injected入力を使い、記録→route導出→AI監視修復→別process AI 0→統合→G13／G600割当→再openを同一public経路で一巡する。Computer Use、SendInput、外部AI APIは0。

## 結論

一巡は3段で構成した。**第1段（fake＋実SQLiteの受入test）と第2段（記録器を製品の合成点へ配線）は成立**。第3段（self-window＋Nano物理入力のlive一巡）は**コードまで用意して未実行**で、実マウスカーソルを数秒奪うためオーナーの合図を待っている（#1108 / #1110）。

着手して最初に分かったのは、**記録機能が製品からまったく到達できない状態だった**ことである。t04 は `IDemonstrationLiveSessionFactory` を差し込み点として置いたまま実装が無く、t05 は `GameOperatorWindow` に記録tabを足したが呼び出し元の `InputStudioWindow` が記録intentsを渡していなかった。t08（オーナーがUIから実記録するH工程）はこの配線が無いと成立しないので、t07 で繋いだ。

## 第1段: fake＋実SQLiteの一巡（成立）

`tests/OpenLogicool.Host.Tests/DemonstrationProductJourneyTests.cs`（新規1件）。engineとOS境界だけをfakeにし、保存と再openは実SQLite。SendInput・Computer Use・外部AI APIは1回も使わない。

`Record_derive_repair_replay_compose_assign_and_reopen_are_one_product_journey` が同じ一巡の中で見ているもの:

1. 記録開始（`HostDemonstrationRecordingIntents.StartAsync`）
2. **記録中は同じgateを持つ再生が拒否される**（自己記録を構造で防ぐ）
3. 停止で1操作が原本に入る
4. route導出（`CreateMacroFromSession`。revision 1）
5. AI監視修復で AiCallCount=1・revision 1→2
6. **AI 0再生は AiCallCount=0・Completed・revisionを増やさない**
7. 統合（続く区間と合成）
8. G13 `G1` / G600 `G9` へ割当（Input Studioと同じassignment経路）
9. 別connectionで再open → route revision 2件・合成routeのedge列・workspaceのtoken・2 binding・両profileのbinding・**原本が残っていること**・tokenが合成macroへ解決すること

## 第2段: 製品の合成点への配線（成立）

- `src/OpenLogicool.Host/WindowsDemonstrationLiveSession.cs`（新規）
  - `WindowsDemonstrationObservationRuntime` — `ProductGameExplorerRuntime` の**観測半分だけ**（observation runtime／stability waiter／`GameTransitionJudge`）を束ねる。**入力dispatchを持たない**ので、記録器は構造上、記録中に入力を出せない。
  - `WindowsDemonstrationLiveSessionFactory` — 対象processの実windowを解決し、探索・macro実行と**同じ**WGC capture／recognizer／target discoveryで観測面を組む。Nano・SendInput・Computer Useはこの経路に登場しない。
  - `DemonstrationEnvironmentScope` — 記録するscopeは既存のものへ合わせる。新しいscopeを勝手に作ると、記録から導出したrouteが探索で育てた既存structureと別世界になる。
- `WindowsProductGameExplorerComposition.CreateTargetDiscovery`（切り出し）— discoveryの組み立てを探索と記録で共有する。**別々のdiscoveryにすると同じ画面が別stateとして同定され、記録から導出したrouteが既存structureへ繋がらない**。挙動は逐語のまま移した。
- `src/OpenLogicool.Host/Program.cs` — 同一の `DemonstrationRecordingGate` を記録intentsと再生intentsへ渡す（t06 で「配線先が存在しない」として残した箇所）。
- `src/OpenLogicool.Desktop/InputStudioWindow.cs` — 記録intentsを受け取って `GameOperatorWindow` へ渡す（既存引数の順序・意味は変えていない）。

### 第2段の試験

`tests/OpenLogicool.Host.Tests/WindowsDemonstrationLiveSessionTests.cs`（新規4件）

| 試験 | 確認した内容 |
| --- | --- |
| `The_scope_whose_resolution_matches_the_window_is_reused` | 解像度が一致する既存scopeを選ぶ |
| `An_existing_scope_is_reused_even_when_no_resolution_matches` | 一致しなくても既存scopeを使い、新しいscopeを作らない |
| `A_scope_is_created_only_when_the_game_has_none` | そのgameにscopeが1件も無いときだけ新規に作る |
| `The_observation_adapter_delegates_to_the_same_parts_the_explorer_uses` | 観測・安定待ち・判定が探索と同じ部品へ委譲され、判定は `GameTransitionJudge` が出す |

**途中で直した自分の間違い**: 最初「一致しなければ直近のscope」を期待するtestを書いて落ちた。調べると `MAX(event_sequence)` は同点になり得て順序が決まらない——これはmacro engineから逐語で持ってきた既存挙動で、コードが保証しているのは「既存scopeがあれば新規を作らない」という不変条件だけだった。保証していないことを期待したtestが誤りなので、testを実際の不変条件へ直した（製品の挙動は変えていない）。

## 第3段: self-window＋Nano物理入力（コード完成・未実行）

`src/OpenLogicool.Probe/DemonstrationJourneySmoke.cs`（新規）と probe command `demonstration-journey-smoke`。

```
dotnet run --project src/OpenLogicool.Probe -- demonstration-journey-smoke --port COM8
```

自分の窓（720x520）を作って前面化 → 製品と同じ `WindowsDemonstrationLiveSessionFactory` で記録開始 → **Nanoの物理HID出力**でその窓の中央へカーソルを動かして1回click → 停止 → route導出 → G13 `G1` / G600 `G9` へ割当 → **別process**（`demonstration-journey-verify`）で再openして token・2 binding・profile 2件・route を読み直す → JSON証跡。

- **SendInput と Computer Use は使わない**。入力はNanoの物理HID出力だけ。
- 対象は自分の窓だけで、NIKKEや他appには触らない。
- 前面化できない（保護appが前面）／Nanoが応答しない場合は、別経路へ逃げず「未確認」としてJSONと標準エラーへ書いて止まる。
- self-windowのWin32実装はt02のprobeと共有した（同じものを二重に持たない）。
- 判定は観測だけから決まる純関数 `DemonstrationJourneySmokeJudgement` へ出してあるので、保存済みJSONへ再適用して同じ結論を機械で確認できる（t02と同じ作法）。

### 依存の実測（read-only・何も動かしていない）

- Nano: **接続あり**。COM8 に `VID_1B4F&PID_9206`（SparkFun Pro Micro 5V）
- Foundry Local: `foundrylocald` 稼働中（endpointは製品のresolver任せ）
- 前面: 保護appが前面を保持している間はlow-level hookが実入力を観測できない（t02実測）

### 未実行の理由

実行すると**数秒だけNanoが実マウスカーソルを動かす**（SendInputではなく物理HID出力なので、その間オーナーの操作と混ざる）。技術判定は私が下すが、人の手が機械にかかっている時間を奪う操作なので、走らせてよいタイミングだけをオーナーへ打診している（#1108 / #1110）。合図があれば1コマンドで通る。

## 試験内容と試験結果

Windows native（net10.0-windows / .NET SDK 10.0.400）。

focused test 新規10件・全green
- `Host.Tests/DemonstrationProductJourneyTests` 1件（一巡受入）
- `Host.Tests/WindowsDemonstrationLiveSessionTests` 4件（scope解決3・観測adapter 1）
- `Probe.Tests/DemonstrationJourneySmokeJudgementTests` 5件（完全な一巡が全check通過／何も記録できなかった一巡が静かに通らない／片方のdevice bindingが消えたら落ちる／別routeへ解決したら落ちる／カーソルが届かなかったら落ちる）

関連module（最終確認）
```
Host 292 / Desktop 103 / Probe 70 / Playbooks 194 / Input 160
Profiles 25 / Persistence 55 / Architecture 8 / Exploration 64 / Perception 32
合計 1,003件 全green・solution build 警告0・エラー0
```

full regression はこの工程では実行していない（t09の範囲）。

## 4値（この工程の範囲）

- fake＋実SQLiteで記録→導出→AI監視修復→AI 0→統合→割当→再openを同一public経路で一巡: **確認済み**（受入test 1件）
- 記録中は同じgateを持つ再生が拒否される: **確認済み**（同じ一巡の中で）
- AI 0再生がrouteのrevisionを増やさない: **確認済み**
- 記録器が製品の合成点から到達できる: **確認済み**（配線＋focused test。ただしUI操作としての目視はt08）
- 記録器が構造上、入力を出せない: **確認済み**（観測面に入力dispatchが無い）
- 記録と探索が同じdiscovery／同じscopeを使う: **確認済み**（切り出しの共有＋scope解決のfocused test）
- Computer Use・SendInput・外部AI APIが0: **確認済み**（第1段は全てfake／第2段の経路にNano・SendInputが登場しない／第3段はNano物理出力のみ）
- Windows self-windowでの実記録とNano非injected入力の一巡: **未確認**（probeは完成。実カーソルを奪うためオーナーの合図待ち）
- ui processと別のrun processが同時に動く場合の記録・再生排他: **非対応**（gateはin-process。cross-process lockは作らない。別processの再生を止めたい場合は先に停止すること）

## 変更file

- `src/OpenLogicool.Host/WindowsDemonstrationLiveSession.cs`（新規）
- `src/OpenLogicool.Host/WindowsProductGameExplorerComposition.cs`（discovery組み立てを切り出して共有）
- `src/OpenLogicool.Host/Program.cs`（記録intentsをgate共有で合成）
- `src/OpenLogicool.Desktop/InputStudioWindow.cs`（記録intentsをGameOperatorWindowへ転送）
- `src/OpenLogicool.Probe/DemonstrationJourneySmoke.cs`（新規）、`src/OpenLogicool.Probe/DemonstrationRecorderSmoke.cs`（self-windowを共有）、`src/OpenLogicool.Probe/Program.cs`（command登録）
- `tests/OpenLogicool.Host.Tests/DemonstrationProductJourneyTests.cs`（新規）
- `tests/OpenLogicool.Host.Tests/WindowsDemonstrationLiveSessionTests.cs`（新規）
- `tests/OpenLogicool.Probe.Tests/DemonstrationJourneySmokeJudgementTests.cs`（新規）
- `evidence/phase14-product-completion/t07-product-journey-acceptance.md`（本書）

commit: `98a05dc`（第1段）／`aa340fa`（第2段）／`b6df3bc`（第3段のコード）
