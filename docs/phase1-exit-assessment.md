# Phase 1 Exit 判定（2026-08-15）

計画 §8 Phase 1「Contract／GameLab Foundation」の Exit 4条件に対する判定。証拠はすべて Windows native 実行の実測。

## 実施した deliverable（計画 §8 Phase 1「実施」9項目）

| deliverable | 状態 | commit |
|---|---|---|
| solution／module骨格 | 完了（13 classlib＋Host配線点） | 5a4fec0 |
| Contracts revision 0.1 | 完了（§6.4 baseline 13型・§7.2 subtree分割） | 5a4fec0 |
| pure model（SemanticAction／Profile generation／Frame／Observation／Proposal／RunEvent） | 完了 | 836e649, c48baf3 |
| GameLab v1 | 完了（virtual clock・固定LCG） | ce996b6, 1e62723 |
| scenario-v1 | 完了（schema＋実 scenario 2本） | ce996b6, 1e62723 |
| fake Device／Capture／Perception／AI | 完了（tests/OpenLogicool.Fakes） | b8999e5 |
| contract conformance suite | 完了（adapter注入型＋fixture厳格検証） | b8999e5, 1e62723 |
| gate manifest format | 完了（schema・意味規則・G0-Automation実データ sample） | 43d1627 |
| dependency direction test | 完了（許可行列・禁止参照・PackageReference・UseWPF） | 5a4fec0, 43d1627 |
| initial SQLite migration runner | 完了（001は schema_migrations のみ） | 43d1627 |

## Exit 条件の判定

### 条件1: GameLab が daily reset、irreversible claim、delay、popup、unknown state、manual intervention を seed 付きで再現

**成立（確認済み）。** 6種すべてが seed 固定 fixture とテストでロックされている。

| 挙動 | ロック手段 |
|---|---|
| daily reset | virtual clock 日境界越えで claim 状態が初期化される focused test |
| irreversible claim | 遷移表に ClaimDone を From とする entry が存在しない検証＋同日再 claim 拒否 test |
| delay | scenario-basic-claim の expected sequence が 100ms 刻みの monotonicMs を固定 |
| popup | 同 fixture が `auto:popup` を expected sequence で固定 |
| unknown state | scenario-unknown-glitch（seed 7133）が `auto:unknown-glitch` を固定 |
| manual intervention | runner 経由（invokeAfterActions）と API 直呼びの両経路を test 化 |

**変異テストによる検証**: `Roll(_scenario.Unknown)` を無効化すると unknown scenario の test が失敗し、復元すると green に戻ることを実測した（2026-08-15）。テストが実際に挙動をロックしていることの証拠であり、「コードがある」だけの成立と区別している。

### 条件2: 全 Lane が自 module と fake だけで build／focused test できる

**成立（確認済み）。** 13 module が NuGet 依存ゼロ（Persistence の Microsoft.Data.Sqlite のみ例外）で build でき、fake 4種と conformance suite が実 hardware・実 capture・実 AI なしで green。`dotnet test OpenLogicool.sln -c Release` 全35件 green。

### 条件3: contract 意味 owner、subtree owner、fixture revision が一意

**成立。** §7.2 の subtree 分割どおり Contracts を Shared／Domain／Devices（Shared・G13・G600）／Profiles／Playbooks／Capture／Perception／AI へ分割し、非交差。全 contract 型が `SchemaVersion`（0.1.0）を持つ。

### 条件4: contract subtree を含む Lane scope が非交差で、Shared／cross-contract 変更だけが integration slot を使う

**成立（構造として）。** dependency direction test が許可行列（§6.3）と禁止参照を機械検査し、Fakes も行列対象。実運用の integration slot 判定は Phase 2 以降の並行作業で検証する。

## 品質保証の経緯

- **cross-provider 監査を2回実施**（Claude 親 → Grok 4.6×high 検証、正典 `docs/02_models.md` の配置）。Slice 2 で2件、Phase 完了時に10件の指摘。**全12件を親が実読で裏取りして採用**し、maintenance wave で修正した。
- 監査で見つかった契約クリティカルな欠陥: KnowledgePackManifest が pack 自身に `Verified` を宣言させる型になっていた（draft 違反。import した pack が自称 verified で自動実行に乗る経路）。`Untrusted` のみの閉じた enum へ修正。
- 実装は Codex（gpt-5.6-terra×high）へ委譲し、親が全 slice で build／test を再実行して受入。委譲先の報告を採用宣言に替えていない。
- Slice 1 の初回報告はスコープを縮小した未完成状態だったため受入棄却し、差し戻して完遂させた。

## 未決定・持ち越し

- RunEvent の `attemptId`／`commandId`／`nodeOrTransitionId` の nullability と §6.7 必須表記の整合（[docs/contracts/run-event.md](contracts/run-event.md) に明記、Phase 4 決定）
- ObservationResult の `lastChangeMs` を使った Unavailable 判定ロジック（型は追加済み、判定は Phase 5）
- crash point の fault injection（scenario-v1 では `const "none"`、Phase 4 で拡張）
- 実機手番: 0A の UI 照合（LGS 画面と XML 台帳12項目の突合）

## 判定

**Phase 1 の Exit 4条件はすべて成立している。** Phase 2（Core Input Replacement）へ進める。ただし Phase 2 は **G0-Device-W（Migration Safety Gate 実証・EXP-G600-03・route 最終決定）を他実装より先に単独実施**する順序であり、これは実機 write を伴うためオーナー裁定と実機手番を要する。device write 禁止は G0-Device-W 通過まで継続。
