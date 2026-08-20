# Phase 8A campaign — Input Studio Parity／Distribution

- status: **active**
- 起票: 2026-08-21（工程表どおり。Phase 7 Exit 後。判断をオーナーへ戻さない）
- 統括: ベル（Grok 4.6）。実装 Terra×high（Codex）／監査 Grok 4.6×medium
- 実行 TODO の正本: **Lattice plan `phase8a-input-studio-dist`**
- 上位正本: [development-plan.md](development-plan.md) §Phase 8A、§14.1 Shared Distribution Gate、§14.2 Input Studio Public Gate、§14.4 LGS Parity Claim Gate
- 先行: Phase 3〜7 Exit 成立。Wave 7A は Playbook／Capture／AI／実 game を待たない

## 目的

実測成立した Input Studio を、常用可能な配布物へする。AI／capture／実 game を待たない。LGS Parity は名乗らない——inventory に未確認行があるので **Partial LGS Replacement** が公開 claim。

## 統括レーン判定と F/A/H

① Authenticode 証明書と clean VM は人待ちとして組込む。②多段受入。④証跡。Exit オーナー待ちは組まない。

- **F**: 公開 claim、gate 判定、commit・push、t10 Exit
- **A**: support matrix、LGS import dry-run、timed macro、restore、diagnostic bundle、packaging、SBOM、install 口
- **H**: Authenticode 署名。席は取らない。証明書が無ければ未確認のまま残す。署名を偽造しない

## 円卓

入口は peertable room `OpenLogicool`。setup.sh／parent-join はしない。pull run は本 plan 用に新規。席数は実装 2＋監査 1 のまま増やさない。

| 役割 | 配置 |
|---|---|
| 統括 | Grok 4.6（bell） |
| 実装 | Terra×high Codex |
| 監査 | Grok 4.6×medium |

待機中は `[次の行動]` 自己DMを出さない。preflight 失敗は席を立てず、別 model へ落とさない。

## 非目標

- LGS Parity を名乗る
- AI／capture／実 game を 8A の門にする
- Phase 8B
- provider 選定
- 装飾 UI
- 署名の偽造・自己署名を Supported と表示

## 受入条件（§Phase 8A Exit）

1. Input Studio Public Gate と Shared Distribution Gate を、確認済みの行だけで判定する。未確認は未確認のまま残す
2. LGS 環境と G600 state を元へ戻せる口がある
3. unsupported を UI と release note へ表示する
4. canonical inventory 全行が Supported でないので LGS Parity を名乗らない。claim は Partial LGS Replacement
5. 各 ToDo focused green＋証跡＋着地。H は未確認のまま残してよい。通し試験は Exit だけ

## 運用

H の t09 は席が取らない。t10 は親。remaining A は最初の witness に含めて compile する。

## Lattice task 仕様（正本は store）

### t01-support-matrix-claim

Input Studio の support matrix と公開 claim を製品面へ置く。確認済みだけ Supported。未確認を Supported にしない。LGS Parity を名乗らず Partial LGS Replacement と書く。G600 route と 3-slot 制約を matrix と release note へ出す。focused。

### t02-lgs-import-dry-run

LGS 9.04.49 profile XML の import dry-run。変換可能行と未対応行を分けて表示する。元設定・device を変更しない。`script` と path を信頼された命令として扱わない。`original="true"` の既定割当は取り込まない。APP-009。focused。fixture XML。LGS 稼働に依存しない。

### t03-timed-macro

delay、repeat while held、toggle、有限回 repeat を明示状態として扱う。停止境界（NFR-008）を破らない。MAP-007。既存の有限 Tap sequence を再実装しない。混在不正は profile 適用時に拒否。focused。

### t04-lgs-restore-rollback

migration の cancel と device restore。dry-run 後に apply しない経路と、G600 leftover restore で baseline へ戻す経路を製品口にする。元 profile を破壊しない。既存 leftover／write 作法を再実装しない。t02 の後。focused。

### t05-diagnostic-bundle

既定 diagnostic bundle。screen、secret、personal data を入れない。preview と削除がある。Host の既存診断口を再実装しない。focused。

### t06-packaging-identity

package identity、autostart、update manifest、unpackaged 配布レイアウト。EXP-DIST-01 の identity を実測して MSIX／Sparse／MSI の採否を記録する。未実測を Supported にしない。install／update 中に device write を開始しない。focused。

### t07-sbom-notices

SBOM、Third-Party Notices、artifact hash。署名はしない。t06 の成果物へ同梱する口。focused。

### t08-install-lifecycle

install、update、rollback、repair、uninstall の口と focused 試験。clean VM 実測は H ではなく、この口が clean 環境で通せる契約をテストで固定する。LGS 復帰は leftover restore を使う。t06 の後。focused。

### t09-authenticode

Authenticode 署名と timestamp。席は取らない。証明書が無ければ未確認のまま残す。自己署名を Supported と表示しない。t06 の後。

### t10-phase8a-exit

full regression 1回、Grok read-only 監査、`docs/phase8a-exit-assessment.md`。親が宣言。席は取らない。H 未確認は未確認のまま書く。Public Gate を未確認行で成立扱いにしない。
