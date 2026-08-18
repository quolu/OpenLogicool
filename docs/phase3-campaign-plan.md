# Phase 3 campaign — App-first Unified UX 完遂（統括レーン）

- status: **completed**（2026-08-18 オーナー Exit 宣言・全 11 task done・全 3 phase accepted・終端監査 accepted。証跡は [evidence/phase3-app-first/terminal-audit.md](../evidence/phase3-app-first/terminal-audit.md)・Exit 判定は [phase3-exit-assessment.md](phase3-exit-assessment.md) が正）
- 起票: 2026-08-16（オーナー指示「Phase 3 を Lattice に書き起こして円卓で進める」）
- 統括: ベル（セッション主モデル Fable 5）
- 実行 TODO の正本: **Lattice plan `phase3-app-first`**（typed discovery 判定済み。本書は目的・思想・非目標・受入条件だけを所有し、ToDo を二重化しない）
- 上位正本: [development-plan.md](development-plan.md) §Phase 3（要件 ID・Exit 条件はそちらが正）

## 目的

Phase 3（App-first Unified UX）の Exit 5条件を成立させる。オーナー裁定（2026-08-16）により **UI は最後・機能中核を計画順に先行**。UI 着手時に Grok 4.6 へ設計相談を挟む。

## 統括レーン判定と F/A/H

統括レーン成立根拠: ①計画に中断が組込済み（実機手番・UI 確認・Exit 裁定）②受入が多段連鎖（機能中核→UI→fake/real contract→Exit assessment）。

- **F（統括直轄）**: 各 ToDo の受入裁定、commit・push、計画正本の更新、Phase gate、契約変更（Contracts/ 配下の wire type・port）
- **A（委譲可の実装物量）**: 仕様固定後の機能中核実装＋focused test、一括置換、fixture 作成
- **H（オーナー手番）**: 実機での launcher／同名 EXE 実測補助（NIKKE 等の起動）、UI の見た目確認、Phase 3 Exit 裁定

## 円卓（知能の配置。役割→ティアは dotagents/docs/02_models.md が正）

| 役割 | 配置 | 入口 |
|---|---|---|
| 統括・裁定・受入・commit | Fable（親） | 本人 |
| 実装物量（A） | `sonnet`×medium | Agent `implementer`（Codex 中位×medium は対等候補） |
| 監査 finder | `sonnet`×low | Agent／Workflow |
| 反証（契約クリティカルのみ） | Grok 4.6×high（Fable 親の第一候補） | aiterm `grok_agent` |
| UI 設計相談 | Grok 4.6 | aiterm `grok_agent`（オーナー指名） |
| Phase 3 exit 監査 | cross-provider（Codex または Grok） | codex-sidecar `codex_review` 等 |

親（Fable）は高額のため、**裁定・受入・契約クリティカルだけに使い、物量は円卓へ流す**（オーナー指示）。

## 非目標（やらないこと）

- Phase 4 以降（Automation Lab・Capture・AI Teach）の先取り
- G600 side remap の残置運用の製品組込（Mapping Runtime 設計は Phase 3 scope 外。probe 実証まで済み）
- device write の新規経路追加（MAP-010 維持）
- LGS profile import（APP-009 は R5・Phase 8A 側）

## 既知の罠

- Windows 11 の Store app redirect: 手打ち EXE path は一致しない。関連付けは実行中 process の path 取得だけを正とする（実測済み・AGENTS.md 記録）
- Control Record CLI は Windows で `PLATFORM_UNVERIFIED`＝fail closed（control-record.md v1）。writer 委譲の Packet／Report は**文書ベース**（会話内 Packet 8点＋Worker Report）で代替し、受入は統括の diff・focused test 再実行で行う
- 並列 writer を2つ以上同時に走らせる場合だけ Lattice run 経由を既定とする。使えない場合は直列へ落とす（自前交差判定で並列強行しない）
- fast path 純潔（AI・network・capture・SQLite・UI を待たない）を UI 実装時も維持する
- 未決定を仮実装で埋めない（根拠4値の表示を Unverified→Supported へ格上げしない）

## 受入条件（campaign 単位）

1. development-plan §Phase 3 Exit 5条件の成立材料が揃い、`docs/phase3-exit-assessment.md` に4値表記でまとまっている
2. 各 ToDo は focused test green＋実走（または実機）証跡で閉じ、対象限定 commit・push 済み
3. UI test scenario が fake と real contract で同じ結果になる（Exit 条件5）
4. 未成立項目は未成立と明記され、成功扱いされていない

## 検証方法

- 機能中核: focused test＋temp DB 実走（既存パターン踏襲）
- app identity 完全形: 実機実測（launcher 遷移・同名 EXE・window 消失）を probe または Host log で証跡化
- UI: fake contract test＋real 実走
- Phase 最終: full regression 1回＋cross-provider 監査

## 運用

- 各 ToDo は「仕様固定（F）→実装委譲（A）→受入（F）→commit/push（F）」で閉じ、**次 ToDo へ自動継続**。止まるのは H と blocker だけで、止まる時は現在地と必要条件を明示する。
- 発見した別問題は本 plan の maintenance queue（Lattice 側 ToDo 追記）へ記録し、無断で完了条件へ追加しない。
