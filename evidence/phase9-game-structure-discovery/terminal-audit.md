# Phase 9 終端監査

日付: 2026-08-24
方式: native sub-agentによるread-only反証、統括が実ファイル／focused evidence／最終回帰を受入

## 最終判定

- P0: 0。
- コードP1: 0。
- 証拠P1: 0。
- P2: Game Operator内部面の外観目視だけ未確認。Windows native Host／Desktop testとInput Studio実フレームは成立。Exit条件10／GS-020を強い推定とし、Preview以上をclaimしない。

## 指摘と閉塞

1. verification authorityがTransition Evidenceの所属sessionだけを見ていた。nodeのstate signatureとedgeのcandidate／primitive／outcome／source／destination照合を追加し、無関係evidence拒否testで11／11 green。
2. Playbook resolverがrevision／environment／edge列をpinしていなかった。`StructurePlaybookCandidate`との完全一致を必須化。
3. 保存locatorをfresh frameに再groundしていなかった。source state再同定、IoU一意照合、policy target window、freshness上限をfail-closed化。
4. Supervised承認とRun Journalの束縛が永続証拠として薄かった。Playbook／structure／source／Observation／frame／transform／policy／consentへ承認を束縛し、前後scene／authorized action／confirmationをSQLiteへ存続。再open後に全束縛をassert。
5. NIKKEの第三列をVerified根拠に使う過大主張を拒否。2 anchor／verification authorityを満たさないため、追加physical replayとしてのみ記録しNIKKEはReplayedを維持。
6. WR-001〜012／GS-001〜021の個別追跡表を[Exit Assessment](../../docs/phase9-exit-assessment.md)に作成。GS-016のFact-aware planningは未確認とし、claim対象外に固定。

## 最終受入

- build: 警告0、エラー0。
- full regression: 1014件green、失敗0、skip 0。
- Input fast path: 151件green。
- Architecture: 8件green。
- 公開範囲: `Game Structure Explorer Preview`。NIKKE Verified／Fact-aware task planning／Verified Autonomous Playbook／日課完遂／一般gameは未確認。
