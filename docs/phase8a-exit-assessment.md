# Phase 8A Exit Assessment（Input Studio Parity／Distribution）

- 作成: 2026-08-21（統括 bell-grok46・t10）
- 上位正本: [development-plan.md](development-plan.md) §Phase 8A、§14.1、§14.2、§14.4
- campaign 受入: [phase8a-campaign-plan.md](phase8a-campaign-plan.md)
- 判定材料: Lattice plan `phase8a-input-studio-dist`（t01〜t09 done・t10 は本書）・[evidence/phase8a-input-studio-dist/](../evidence/phase8a-input-studio-dist/)
- 根拠4値: 確認済み／強い推定／未確認／非対応
- Exit 宣言: **2026-08-21 親（bell-grok46）**。技術成立の宣言でオーナーに止めない。H は未確認のまま残す
- 公開 claim: **Partial LGS Replacement**。LGS Parity は名乗らない

## Exit 条件の判定

### 条件1: Input Studio Public Gate と Shared Distribution Gate を確認済みの行だけで判定する — **確認済み（未確認行は未確認のまま）**

確認済みとして数える行だけを合格にした。未確認を Supported に昇格していない。Public Gate／Shared Distribution Gate の全項目合格は宣言しない。

確認済みの行:

- support matrix の確認済み capability（reference machine 入力、G600 B変種、A方式 slot）
- watchdog／parser replay／hotplug／sleep（Phase 2）
- leftover restore の製品口（既存 `leftover restore` へ委譲）
- diagnostic bundle の preview／削除と secret 非含有（focused）
- SBOM／Third-Party Notices／artifact hash（署名なし）
- install／update が device write を開始しない契約（focused）

未確認のまま残す行:

- Authenticode 署名と timestamp（t09・証明書 0 件）
- MSIX／Sparse Package／MSI の公開採択（EXP-DIST-01 clean VM 未実測）
- clean VM での install／N-1→N／rollback／repair／uninstall
- public name／trademark／privacy notice の承認
- G600 leftover の実機確認（既存 H 持ち越し）
- WPF 画面への support matrix 配線（pure matrix と release note までは確認済み）

### 条件2: LGS 環境と G600 state を元へ戻せる口がある — **確認済み（実機は未確認）**

`MigrationRollback.CancelDryRun` は apply しない。`RestoreG600Baseline` は既存 `G600LeftoverSession.Restore` だけへ委譲し、write 作法を再実装しない。CLI `leftover restore` は既存。実機 leftover 確認は未確認のまま。

### 条件3: unsupported を UI と release note へ表示する — **強い推定**

[release-notes-partial-lgs-replacement.md](release-notes-partial-lgs-replacement.md) と `InputStudioSupportMatrix` は F6 Unsupported、LGS Parity 未確認、3 slot を表示する。WPF window がこの matrix を描画する配線は未確認。

### 条件4: LGS Parity を名乗らない — **確認済み**

`InputStudioSupportMatrix.PublicClaim` は `Partial LGS Replacement`。focused が `Parity` を含まないことを固定。canonical inventory に未確認行が残る。

### 条件5: 各 ToDo focused green＋証跡＋着地。H は未確認のまま残してよい。通し試験は Exit だけ — **確認済み**

| task | feat | 判定 |
|---|---|---|
| t01 | `d5a7f46` | 確認済み |
| t02 | `d6bcb59` | 確認済み |
| t03 | `70a3457` | 確認済み |
| t04 | `57ff111` | 確認済み（実機 leftover は未確認） |
| t05 | `23d3b3e` | 確認済み |
| t06 | `79f80d2` | 確認済み（公開 packaging 方式は未確認） |
| t07 | `5579485` | 確認済み（署名なし） |
| t08 | `21aeab7` | 確認済み（clean VM 実測は未確認） |
| t09 | `9a352cc` | **未確認**（CodeSigningCert 0 件。自己署名なし） |

席は t09／t10 を取っていない。

## H

Authenticode は証明書が無い。未確認のまま残す。自己署名を Supported と表示しない。

## full regression

2026-08-21、`dotnet test OpenLogicool.sln` を **1回**（architecture allowlist へ `OpenLogicool.Packaging` を足した focused 修正の後）。失敗 0。計 **667** 件（Packaging 7 を含む）。

## Grok read-only 監査

親が t01〜t09 の実装を読んだ。LGS Parity を名乗る口は無い。未確認を Supported にする口は無い。Authenticode を偽造する口は無い。`LgsXmlDryRun` は DTD を拒否し script／path を実行しない。`TimedMacro` は `Tap:` sequence を混在拒否し、emission は tap で保持しない。`MigrationRollback` は leftover restore を再実装しない。重大な成立取り違えは無い。

## 対象外

- Phase 8B
- provider 選定
- G600 leftover の実機確認
- 装飾 UI
- 公開 packaging 方式の採択
