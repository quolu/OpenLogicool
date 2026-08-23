# STEP 0 Web Reference policy contract

## Status

Accepted — 2026-08-24

## Context

AI Game Structure Discoveryの前段でWeb情報を参考にするが、取得物は非信頼であり、Game Policy、risk、allowed primitive、承認、budget、provider、Data Flowを変更できない。GameWithは全文を保存・再配布せず、出典付きの短い参照カードだけを保持する。取得不能や利用条件不明を空の成功または古いcache成功へ丸めない必要がある。

## Decision

1. source policyはoriginal URL、canonical URL、terms、robotsだけを入力とするpure決定表で決め、保存前に再計算する。originalまたはcanonicalのどちらかが`gamewith.jp`系なら、取得許可があっても`SummaryOnly`を上限とする。
2. `SummaryOnly`の根拠断片は200文字×3件に固定する。この数値をAI出力や取得物から変更する口を作らない。
3. FullText／Summary取得済みsourceと、LinkOnly／Blockedで本文取得前に止まったsourceを別wire型にする。後者のmetadataへ偽値を埋めない。
4. Web Reference FactのvalidityにはVerifiedを置かない。Web payloadには実行権限を与えるstate ID、target、allowed action／primitive、expected transition、risk、approval、budgetを置かない。
5. 新規取得、cache再利用、policy制限、失敗8種を別状態にする。LinkOnly／Blockedもsource／documentへ相関し、空の成功文書にはしない。
6. revision系列はappend-onlyとし、利用者削除はpayloadの物理削除後に、削除対象IDだけを持つtombstoneを追記する。
7. 取得前plan、source除外、再取得、削除previewをpure contractとして公開し、HTTP／SQLite／UIは後続taskへ分離する。

## Consequences

- Web情報は探索hintの候補にはなるが、Game Structure、Verified Step、verified Game State Factへ直接昇格できない。
- t02はこのwire形を変えずにappend-only SQLite storeと削除を実装する。
- t03はoriginal／redirect後canonicalの双方をpolicy evaluatorへ渡し、別source／cacheへ黙ってfallbackしない。
- t04はacquisition planと削除previewをそのまま表示し、保存成功と取得成功を混同しない。
- 受入証拠は`evidence/phase9-game-structure-discovery/t01-step0-policy-contract.md`を参照する。
