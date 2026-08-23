# t01 STEP 0 policy contract 受入記録

日付: 2026-08-24
Lattice plan: `phase9-game-structure-discovery`
Task: `t01-step0-policy-contract`

## 結論

受入。WR-001〜012のSTEP 0境界をpure wire contract、deterministic source policy、machine testへ落とした。HTTP、SQLite、UI、ExplorationContextは後続taskの責務として実装していない。

## 実装した契約

- original URLとcanonical URLの双方をtrusted policy evaluatorへ渡し、どちらかが`gamewith.jp`またはそのsubdomainなら、取得許可があっても`SummaryOnly`へ固定する。末尾dotも正規化する。
- terms／robotsがUnknownまたはUnavailableなら`LinkOnly`、Rejectedなら`Blocked`。AIがdecisionや引用上限を緩和してもvalidatorが決定表との差を拒否する。
- `SummaryReferenceBody`はraw HTML、画像、変換全文を保持するfieldを持たず、根拠断片を200文字×3件に固定する。
- 本文取得済み`AcquiredWebReferenceSource`と、metadataを偽造せず停止を記録する`RestrictedWebReferenceSource`を別wire形にした。
- Web Fact専用validityはHypothesis／Stale／Contradictedだけで、Verifiedを表現できない。
- 新規取得、既存cache再利用、policy制限、provider未選定、network不可、terms拒否、robots拒否、HTTP失敗、parse失敗、取消、timeoutを区別する。空文書を新規成功として扱わない。
- 取得前preview、source除外、再取得要求、削除previewをpure contract化した。削除はpayloadを物理削除し、削除対象IDだけを持つtombstoneをappendする。
- Web payloadはstate ID、target座標、allowed action／primitive、expected transition、risk、approval、budget、Game Policyを持たない。

## 変更ファイル

- `src/OpenLogicool.Contracts/Research/WebReferenceContracts.cs`
- `src/OpenLogicool.Contracts/Research/WebReferenceContractSchema.cs`
- `tests/OpenLogicool.Conformance.Tests/WebReferenceContractTests.cs`

## 検証

- `dotnet test tests/OpenLogicool.Conformance.Tests/OpenLogicool.Conformance.Tests.csproj --no-restore`
  - 52件成功、失敗0、skip 0
- `dotnet test tests/OpenLogicool.Architecture.Tests/OpenLogicool.Architecture.Tests.csproj --no-restore`
  - 7件成功、失敗0、skip 0

## 反証と修正

Fableの初回read-only監査はP0 1件、P1 2件を検出した。

1. GameWithのWeb本文が外部canonical URLを申告するとSummaryOnlyを迂回できた。
2. LinkOnly／Blocked documentがResearchRunのattemptから孤児になった。
3. 未取得sourceにもtitle／publisher／locale／digestを必須化し、偽値を強制していた。

original／canonical両host判定、取得済み／停止sourceの型分離、policy制限attemptのsource／document参照によって根治した。修正後のFable再監査は`PASS（旧P0/P1閉塞、新規P0/P1なし）`。Fableはread-onlyで、source変更・commit・pushを行っていない。

Peertable room `OpenLogicool`には設計監査`[921]`／`[922]`、実装後監査`[923]`、修正後監査`[924]`を依頼した。このtask受入時点で席からの返答は未着であり、返答済みとは扱っていない。後続taskとterminal auditでも同じroomを継続使用する。

## 残さないもの

- Source取得、redirect、robots、HTML→Markdown変換は`t03-step0-acquisition`。
- SQLite append-only revision、復元、削除実行、exportは`t02-step0-store`。
- UI previewと操作journeyは`t04-step0-ui`。
- Web hypothesisからExplorationContextへの受渡しと観測証拠の関連付けは`t06`以降。
