# Phase 2 Exit 判定材料（2026-08-16 時点）

計画 §8 Phase 2「Core Input Replacement」の Exit 5条件に対する現在地。証拠はすべて Windows native 実行の実測。**本書の時点で Exit は成立していない**（条件2が未実施、条件1・4の表示系が Desktop UI 待ち）。成立済み・未達を隠さず仕分け、残作業を明示する。

## 実施した deliverable（計画 §8 Phase 2「実施」8項目）

| 実施項目 | 状態 | 証拠 |
|---|---|---|
| G0-Device-W（apply・restore 実証、EXP-G600-03、write 拡張実測、route 最終決定） | **完了（オーナー裁定 2026-08-15 通過）** | [g600-route-assessment-2026-08-15.md](g600-route-assessment-2026-08-15.md) §5、B変種主経路・A補完・C不採用 |
| G13 adapter、G600 route、Mapping Runtime、Input Emitter | **完了・実機実証済み** | 両 adapter live smoke（drop 0）、fastpath live smoke（9押下対・drop 0・fault なし）、emitter smoke（watchdog 35ms release） |
| press generation、layer、profile 切替 | **完了**（down 時固定・up 再解決なし・変更は新規 down から。MAP-003/005/006・DEV-007/008） | `PressOwnershipState`＋`DeviceMappingRuntime` focused test 54件 green |
| finite macro（有限 sequence 出力） | **未実装**。単一 key・chord までは実装済み。DEV-006 の有限 sequence は Mapping Runtime の binding 表現拡張が必要 | — |
| foreground app identity | **未実装**（Phase 3 の app-first UX と一体で設計するのが自然な依存関係） | — |
| read-only onboarding と device capability 表示 | **未実装**（Desktop UI 未着手のため） | — |
| input acceptance（Notepad、通常 app、管理者 app、対象 game を分類） | **standard＝Delivered／elevated＝Blocked を受信側観測で確定**。Notepad・通常 app は standard 分類に包含。対象 game は Phase 7 pilot で個別実測（計画 §16 に記録済み） | `sendinput-accept` 証跡2件 |
| hotplug、sleep、profile 切替、key 保持、queue overflow | **部分**: queue overflow は fault 停止＋全 release を実装・test 済み。profile 切替・key 保持は focused test 済み。**hotplug は切断検出＋fake suite まで実装・green**（抜線実測のみ実機手番）。**sleep の実測 suite は未実施** | `FastPathPump`／`HotplugTests` focused test |
| driver decision record | **不要が確定**（user-mode B変種 route が成立したため driver 分岐に入らない） | route assessment |

## Exit 条件の判定

### 条件1: G13/G600 の Supported control を欠落なく表示・変換

**変換は成立・表示は未達。** 変換: 実測台帳の確認済み／強い推定 control 全数（G13 31 control＋stick、G600 20 control＋wheel tick）を parser が扱い、recorded fixture replay と live smoke で実証済み。未確認 bit は contract に載せない（根拠4値の運用どおり）。**表示**（capability の Supported/Unverified 表示面）は Desktop UI 未着手のため未達。

### 条件2: 1,000,000 report replay、1,000 generation race、hotplug suite が通る

**replay・generation race は成立（2026-08-16 追記）・hotplug suite は未実施。**

- **1,000,000 report replay: G13/G600 とも green**（各 <1秒）。固定 LCG の生成器が各 report へ加えた変化（既知 bit 反転・wheel event・stick 変化・未確認 bit noise）を oracle として保持し、stream の出力 edge／wheel tick／stick sample が「加えた変化そのもの」と全 report で完全一致することを検証（生成器由来の独立 oracle であり、parser 出力の自己参照ではない）。終端 idle report で stuck control ゼロ、edge 総数下限も検証（G600: edge 70万規模＋tick 15万規模、G13: edge 60万規模＋sample 25万規模）。
- **1,000 generation race: green**。profile 差し替え・latch 層切替・hold 層出入りの generation 変化 1,000 回を押下・解放と交錯させ、revision 刻印付き output token で「up が down 時 outputs と完全一致（再解決なし）」「二重 down・幽霊 up ゼロ」「終端 StopAndReleaseAll が保持中 output と完全一致」を検証。**wrong release 0 成立**。
- **hotplug suite: fake suite 成立（2026-08-16 追記）・抜線実測のみ実機手番**。切断検出（WM_INPUT_DEVICE_CHANGE・RIDEV_DEVNOTIFY）を G13/G600 両 live source に実装し、`FastPathPump` が Removal で所有 output 全 release＋新規 down 停止（DEV-008）、Arrival で受理再開（default layer へ復帰・切断前状態は持ち越さない）を行う。fake suite（`HotplugTests` 6件）で「保持中切断の自動 release」「幽霊 up 無送出」「hold layer 復帰」「1,000 回抜挿 cycle で stuck 0・wrong release 0」を検証し green。実機の抜線実測は probe `hotplug-smoke`（3段階対話・JSON 証跡）を用意済みで、実機手番のみ残る。

### 条件3: LGS virtual keyboard／bus に依存しない Supported path が明示される

**成立。** B変種（side 12ボタン→中間 usage F13〜F24 書換え＋raw route 0x80 読取り→SendInput 出力）が正典に明示され、LGS 常駐なし・driver なしで fast path が end-to-end 実機実証済み。LGS 併存時の巻き戻しなしも実測済み。

### 条件4: G600 制約を隠さず、3 slot または driver 等の実際の方式を UI に反映する

**方式の正典明示は成立・UI 反映は未達。** 制約（onboard 3 slot、B変種の中間 usage 書換え、F6 read 不能、elevated foreground 制約）はすべて文書化済み。UI への反映は Desktop UI 未着手のため未達。

### 条件5: hard crash の output 残留（watchdog 実装・受入）

**成立（オーナー裁定 2026-08-16 済み）。** EXP-IN-03 で「残留なし実証」ルートを棄却し watchdog 採用必須を確定 → 依存ゼロ watchdog を実装し hard kill 後 35ms release を実測（NFR-008 250ms 予算内）。elevated foreground 中の release 不能は残留 risk として Supported matrix の行条件に表示する（uiAccess・昇格実行とも不採用の裁定済み）。

## 判定

**Exit 5条件のうち成立2（条件3・5）、部分成立3（条件1・4——表示系が未達、条件2——fake suite まで成立・抜線実測のみ実機手番）。**

残作業の性質で分けると:

1. **自律で閉じられる**: finite macro（hotplug の切断検出＋fake suite は 2026-08-16 に完了）
2. **Desktop UI（Phase 3 並行レーン）で閉じる**: 条件1・4の表示系、read-only onboarding、foreground app identity。計画上 Phase 2 と Phase 3 は並行であり、表示系条件は Phase 3 の UI 骨格で満たすのが自然
3. **Phase 7 へ送付済み**: 対象 game の acceptance 分類（計画 §16 記録済み）

推奨: 1 を先に閉じて条件2を完全成立させ、その後 Phase 3 レーン（Desktop UI 骨格）へ進んで表示系条件を満たす。sleep 実測は実機手番（スリープ操作）を要するため、suite 整備後にオーナー手番で1回実測する。

## 追記（2026-08-16 同日）

条件2の 1M replay（G13/G600）と 1,000 generation race を実装し green を確認（`G600MillionReportReplayTests`／`G13MillionReportReplayTests`／`GenerationRaceTests`）。同日さらに hotplug の切断検出＋fake suite（`HotplugTests`）も実装し green。条件2の残りは抜線実測（probe `hotplug-smoke`・実機手番）と sleep 実測だけ。
