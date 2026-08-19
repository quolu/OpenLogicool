# Phase 4 campaign — Durable Automation Lab

- status: **active**（2026-08-19 起票）
- 起票: 2026-08-19（オーナー指示「Phase 4 を統括レーンで起票。実画面はまだ使わない」）
- 統括: ベル（本セッション親は Grok 4.6）
- 実行 TODO の正本: **Lattice plan `phase4-durable-lab`**（typed discovery 判定済み。本書は目的・思想・非目標・受入条件だけを所有し、ToDo を二重化しない）
- 上位正本: [development-plan.md](development-plan.md) §Phase 4 および §6.7〜6.12（要件 ID・Attempt 契約・Data Flow はそちらが正）

## 目的

AI なしで、停止・修正・再開の正しさを GameLab 上で完成する。この Phase の「現在 state」は GameLab oracle と fake Observation に限る。実画面からの resume claim は使わない。

## 統括レーン判定と F/A/H

統括レーン成立根拠: ①計画に中断が組込済み（GameLab 目視・Exit 裁定）②受入が多段連鎖（kernel→制御／観測→Lab／Exit）④裁定証跡が必要（crash matrix・journal replay）。

- **F（統括直轄）**: Data Flow Contract、監査担当クローズの観測、commit・push、計画正本の更新、Phase gate、Contracts 配下の wire type。各 ToDo のクローズは監査担当。親は代行しない
- **A（委譲可の実装物量）**: 仕様固定後の Playbook／journal／Attempt／GameLab 実装＋focused test
- **H（オーナー手番）**: GameLab の停止／再開面の目視、Phase 4 Exit 裁定。G600 残置の実機確認は本 campaign 外（先行実装済み・[g600-leftover-operation.md](g600-leftover-operation.md)）

## 円卓（知能の配置。役割→ティアは dotagents/docs/02_models.md が正）

円卓の入口は **peertable room** だけである。手順は peertable の `skill/SKILL.md`（setup → launch-seat → parent-join）。Windows native は 2026-08-16 の席対応修理後に利用可。Grok 親は room MCP を後付けできないので HTTP API で着卓する。物量は円卓へ流す。

Grok の `spawn_subagent` は円卓ではない。host 内子への実装委譲を円卓の代替にしない。

| 役割 | 配置 | 入口 |
|---|---|---|
| 統括・裁定・受入・commit | Grok 4.6（親） | 本人。peertable に parent-join。裁定と受入だけ |
| 実装物量（A） | `sonnet`×medium | peertable の Claude 席（`launch-seat.sh`）。本端末の Codex 席は使わない |
| 監査 finder | `sonnet`×low | 同じ円卓の別席。監査専用席は増やさない |
| 反証（契約クリティカル） | Grok 4.6×high | 親直轄。円卓外の read-only 確認に限って `spawn_subagent` refuter を使ってよい |
| Phase 4 exit 監査 | Grok 4.6 read-only | 親直轄。Codex は本環境 sandbox 破損のため使わない |

## 非目標（やらないこと）

- 実画面 capture／OCR／live resume claim（Phase 5）
- AI Teach／NextActionProposal の製品接続（Phase 6）
- G13 LCD／G600 RGB・DPI・F4/F5 残置／slot 切替の製品組込
- UI の装飾・実画像（磨きフェーズ）
- UI 保存と関連付けの導線統合（Phase 3 持ち越し・本 campaign 外）
- LGS profile import（APP-009 は R5・Phase 8A）

## 既知の罠

- 本端末は Codex sidecar／CLI とも Windows sandbox 破損。監査は Grok に限る
- 円卓は peertable room。`spawn_subagent` implementer を円卓と読まない（2026-08-19 実被弾）
- lattice.kitepon.dev が 503 のときはこの PC の dashboard daemon 死亡。`lattice bridge reconfigure` のあと todo 系書込み 1 発で復旧
- fast path 純潔: Playbook executor は Device Input→Mapping Runtime→Emitter を待たせない。dispatch は所有 model の上で別経路
- Input API 成功をゲーム内成功と扱わない（PB-005）。Confirmed には Observation が必須
- 未解決 DispatchArmed から次 dispatch を自動生成しない。失敗を別経路へ fallback しない
- GameLab Prototype の oracle は実験資産。製品 GameLab は Contracts の Observation／Playbook を正とし、Prototype を黙って製品扱いにしない
- **peertable 0.4.3（2026-08-19 npm）**: Windows 着席（`-L`・MCP 同意・seat identity・lattice.cmd・prepublish）に加え、parent-watch の `shell:true` / DEP0190 を外した
- **Claude Windows hook の `\` 消失**: `apply-claude-config` が path を引用するようにした（2026-08-19 適用済み）。次の Claude 席から効く

## 受入条件（campaign 単位）

1. development-plan §Phase 4 Exit 条件の成立材料が揃い、`docs/phase4-exit-assessment.md` に4値表記でまとまっている
2. 全 fault point で未解決 DispatchArmed から次 dispatch を自動生成しない
3. Confirmed に Observation が必ず存在する
4. journal replay と projection が一致する
5. active Run の version が crash や edit で勝手に変わらない
6. manual intervention 後は再観察なしに進まない
7. 現在 state の根拠は GameLab oracle／fake Observation だけであり、実画面 resume を主張していない
8. 各 ToDo は focused test green＋証跡で閉じ、対象限定 commit・push 済み
9. 未成立項目は未成立と明記され、成功扱いされていない

## 検証方法

- kernel／制御: focused test と fault fixture（実画面なし）
- GameLab: oracle と fake Observation の scenario。通し試験は Exit の最終確認だけ
- Phase 最終: full regression 1回＋Grok read-only 監査

## 運用

- 各 ToDo は「仕様固定（F）→実装委譲（A）→監査担当クローズ→intake accept→着地（F）」で閉じ、次 ToDo へは監査担当の「次の工程に着手してください」で継続。止まるのは H と blocker だけ
- 発見した別問題は Lattice の maintenance note へ記録し、無断で完了条件へ追加しない
- Data Flow Contract（§6.12）は journal 永続化より先に閉じる

## Lattice task 仕様（正本は store。以下は起票時の作業指定）

### t01-data-flow-contract

Phase 4 前必須の Data Flow Contract を `docs/` へ置く。対象: frame、crop、OCR text、window title、process path、prompt／response、journal、device ID、crash dump、diagnostic bundle。各 data に生成元・保存先・送信先・retention・削除経路。初期既定は計画 §6.12（full-screen 永続 OFF、cloud OFF 等）。実装は書かない。

### t02-playbook-graph

PB-001／002／008。Playbook を前提・状態・Semantic Action・期待結果・分岐の graph として保存し、Run 開始時に immutable version へ pin する。訂正は新 version。確定済み event は変更しない。Contracts／Domain／Playbooks の focused test。

### t03-journal

PB-006、OPS-008／009。観測・提案・承認・dispatch・結果・確定・訂正・手動介入を append-only event として保存する。journal と engineering log を分離し correlation ID で一遷移を追跡する。t01 の retention／削除経路に従う。再起動復元は OPS-008。

### t04-attempt-sm

PB-003／004／005 と §6.7。操作前に Attempt と DispatchArmed を commit してから外部入力を呼ぶ。DispatchArmed 以降の未解決は OutcomeUnknown。Windows 入力・ゲーム内効果・SQLite を一つの transaction にしない。Input API 成功を Confirmed にしない。

### t05-run-controls

PB-007。pause、一手実行、skip、abandon、手動介入、未来手順の編集、version switch。物理入力が同じ Semantic Action に届いたら manual intervention として executor を止める（PB-013）。Run 進行へ自動合流しない。

### t06-fake-observation

Unique／Ambiguous／Unknown／Unavailable の fake Observation。Confirmed には同じ Attempt を参照する commit 済み Observation が必須。Perception は Attempt を知らない。実画面は使わない。

### t07-fault-matrix

全 fault point（crash、handled stop、window 喪失、partial SendInput）で未解決 DispatchArmed から次 dispatch を自動生成しない。保証できる中止だけ Disarmed。保証できない場合は OutcomeUnknown。NFR-012。

### t08-gamelab-oracle

APP-010、UX-003〜005。GameLab で Playbook と実行履歴を編集・閲覧する。提案待ち／承認待ち／入力中／結果確認中／停止／対象不一致／認識不能／完了／失敗を常時表示。pause／emergency stop は AI・capture・対象 device に依存しない。現在 state は oracle／fake Observation だけ。

### t09-recorder-replay

session recorder／replayer。journal replay と projection が一致する。active Run の version が replay や crash 復元で勝手に変わらない。

### t10-resume-ux

PB-009、UX-005。再開前に対象 app・version・現在 Observation を照合し、UniqueMatch 以外では自動再開しない。manual intervention 後は再観察なしに進まない。実画面 UniqueMatch は対象外。

### t11-phase4-exit

full regression 1回、Grok read-only 監査、`docs/phase4-exit-assessment.md` を Exit 条件×4値で作成。最終 Exit 宣言はオーナー裁定（H）。
