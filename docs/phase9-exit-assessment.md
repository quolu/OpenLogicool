# Phase 9 Exit Assessment — AI Game Structure Discovery

判定日: 2026-08-24
判定者: ベル（統括）
工程正本: Lattice plan `phase9-game-structure-discovery`

## 結論

**Phase 9 Exitは成立した。** Exit 10条件は確認済み9、強い推定1、未確認0、非対応0である。

この判定が許可する公開範囲は`Game Structure Explorer Preview`である。hidden-oracle GameLabではVerified structureから別Supervised Runを成立させた。NIKKEは独立2 sessionのopen→observe→backをNano Serial HIDで再現したが、verificationは`Replayed`のままである。したがってNIKKEの`Verified Game Structure`、Fact依存task planning、`Verified Autonomous Playbook`、日課完遂、一般game対応はclaimしない。製品全体の公開claimは`Game Operator Preview`のまま維持する。

## Exit 10条件

| # | 判定 | 根拠 |
|---|---|---|
| 1 | 確認済み | STEP 0はFullTextAllowed／SummaryOnly／LinkOnly／Blockedをdeterministicに分類し、GameWithの全文残置0、出典欠落0、Web由来verified昇格0。[t01](../evidence/phase9-game-structure-discovery/t01-step0-policy-contract.md)～[t04](../evidence/phase9-game-structure-discovery/t04-step0-ui.md) |
| 2 | 確認済み | Web Reference 0、game固有seed 0のpixel-only hidden oracleを空DBから起動。[t10](../evidence/phase9-game-structure-discovery/t10-hidden-oracle-gamelab.md) |
| 3 | 確認済み | node 3、edge 4。Observation→proposal／policy／approval→Attempt→after Observation→Structure Eventをcorrelation。[t08](../evidence/phase9-game-structure-discovery/t08-exploration-coordinator.md)、[t10](../evidence/phase9-game-structure-discovery/t10-hidden-oracle-gamelab.md) |
| 4 | 確認済み | oracle名がruntime payloadへ0、存在しないedge commit 0、high-impact／scope外dispatch 0。[t10](../evidence/phase9-game-structure-discovery/t10-hidden-oracle-gamelab.md) |
| 5 | 確認済み | Ambiguous／Unavailable／Stale／transform不一致／未解決DispatchArmedで次dispatch 0、blind retry 0。[t08](../evidence/phase9-game-structure-discovery/t08-exploration-coordinator.md)、[t10](../evidence/phase9-game-structure-discovery/t10-hidden-oracle-gamelab.md) |
| 6 | 確認済み | SQLite再open後のprojection一致、異なる3 sessionでCandidate→Replayed→Verified。無関係evidenceは昇格拒否。[t07](../evidence/phase9-game-structure-discovery/t07-structure-store.md)、[t13](../evidence/phase9-game-structure-discovery/t13-supervised-playbook.md) |
| 7 | 確認済み | merge／split／edge再帰属／contradiction／retireはappend-only新revision。反証は依存edgeをCandidateへ降格し、pinned Playbookを無人実行不可にする。[t07](../evidence/phase9-game-structure-discovery/t07-structure-store.md)、[t13](../evidence/phase9-game-structure-discovery/t13-supervised-playbook.md) |
| 8 | 確認済み | NIKKE lobbyの非課金／非消費／非戦闘scopeでNanoのみのopen→observe→backを発見し、別process sessionで再同定／再遷移。[t12](../evidence/phase9-game-structure-discovery/t12-nikke-safe-slice.md) |
| 9 | 確認済み | Verified graphからPlaybook合成、fresh frame再束縛、User一手承認、別RunのDurable Attemptで一回だけdispatch、SQLite再open後も証拠維持。[t13](../evidence/phase9-game-structure-discovery/t13-supervised-playbook.md) |
| 10 | 強い推定 | Windows native Host／Desktop testで全表示値、pause／step／abandon／訂正をpublic intentから受入。WGC実フレームでInput Studio本体とGame Operator入口を目視。NIKKE前面保持のためGame Operator内部面の外観目視だけ未確認。[t11](../evidence/phase9-game-structure-discovery/t11-explorer-ui.md) |

## campaign受入9条件

| # | 判定 | 根拠 |
|---|---|---|
| 1 | 成立 | 本文書の33件traceability matrix。 |
| 2 | 成立 | STEP 0の出典／保存／非信頼入力／verified分離。 |
| 3 | 成立 | hidden oracle: Web 0、seed 0、node 3、edge 4。 |
| 4 | 成立 | crash／stale／capture loss／OutcomeUnknown／budget／recovery lossでblind retry 0、scope外dispatch 0。 |
| 5 | 成立 | restart replay、独立session昇格、対象evidence authority照合。 |
| 6 | 成立 | NIKKE可逆edgeの発見と別session replay。verificationはReplayedのまま。 |
| 7 | 成立 | learned Verified graphから別Supervised Run。 |
| 8 | 成立 | `OpenLogicool.Input.Tests` 151件、`OpenLogicool.Architecture.Tests` 8件 green。AI／Webにfast path依存なし。 |
| 9 | 成立 | solution build成功、最終full regression 1014件green、read-only反証P0 0、本Exit判定、対象限定commit／push。 |

## WR-001〜012 traceability

| ID | 4値 | 実装／focused evidence |
|---|---|---|
| WR-001 | 確認済み | goal／source選択→preview／start／Markdown表示のSTEP 0。[t01](../evidence/phase9-game-structure-discovery/t01-step0-policy-contract.md)、[t04](../evidence/phase9-game-structure-discovery/t04-step0-ui.md) |
| WR-002 | 確認済み | URL、canonical、title、publisher、時刻、locale、kind、policy、digest、取得方式、引用範囲をcontract／store test。[t01](../evidence/phase9-game-structure-discovery/t01-step0-policy-contract.md)、[t02](../evidence/phase9-game-structure-discovery/t02-step0-store.md) |
| WR-003 | 確認済み | FullText MarkdownとSummaryOnly／LinkOnly cardを別wire形で保存。[t02](../evidence/phase9-game-structure-discovery/t02-step0-store.md) |
| WR-004 | 確認済み | source policyのdeterministic判定、不明はLinkOnly／Blocked。[t01](../evidence/phase9-game-structure-discovery/t01-step0-policy-contract.md)、[t03](../evidence/phase9-game-structure-discovery/t03-step0-acquisition.md) |
| WR-005 | 確認済み | GameWith SummaryOnly、raw HTML／画像／全文Markdown残置0。[t03](../evidence/phase9-game-structure-discovery/t03-step0-acquisition.md) |
| WR-006 | 確認済み | Web／snippet／Markdown／OCRをuntrustedとし、policy／risk／budget変更を拒否。[t01](../evidence/phase9-game-structure-discovery/t01-step0-policy-contract.md) |
| WR-007 | 確認済み | Reference Factのkind／claim／sources／confidence／scope／contradictionとWeb→verified不可。[t01](../evidence/phase9-game-structure-discovery/t01-step0-policy-contract.md) |
| WR-008 | 確認済み | Web hypothesisからstate／座標／allowed action／riskへの変換経路なし。[t01](../evidence/phase9-game-structure-discovery/t01-step0-policy-contract.md)、[t08](../evidence/phase9-game-structure-discovery/t08-exploration-coordinator.md) |
| WR-009 | 確認済み | claim source／contradiction／staleのappend-only revision、取得failureのfallbackなし。[t02](../evidence/phase9-game-structure-discovery/t02-step0-store.md)、[t03](../evidence/phase9-game-structure-discovery/t03-step0-acquisition.md) |
| WR-010 | 確認済み | 取得／保存／引用／ローカルAI／外部送信0／費用0／期限／除外／再取得／削除をUI intentで一巡。[t04](../evidence/phase9-game-structure-discovery/t04-step0-ui.md) |
| WR-011 | 確認済み | hidden-oracle acceptanceはWeb Reference 0固定。[t10](../evidence/phase9-game-structure-discovery/t10-hidden-oracle-gamelab.md) |
| WR-012 | 確認済み | provider未選定／network／policy／robots／HTTP／parse／cancel／timeoutを個別failureとし、cache成功へ丸めない。[t03](../evidence/phase9-game-structure-discovery/t03-step0-acquisition.md) |

## GS-001〜021 traceability

| ID | 4値 | 実装／focused evidence |
|---|---|---|
| GS-001 | 確認済み | game固有seed 0でpixel／window／environment／generic primitive／goal／policyだけから起動。[t10](../evidence/phase9-game-structure-discovery/t10-hidden-oracle-gamelab.md) |
| GS-002 | 確認済み | Available×NovelをObservedScene／state hypothesis／affordance／evidenceとして保持。[t06](../evidence/phase9-game-structure-discovery/t06-scene-contract-migration.md)、[t09](../evidence/phase9-game-structure-discovery/t09-vision-provider.md) |
| GS-003 | 確認済み | candidateはObservation／frame／transform／window／locator／evidence／confidence／primitiveへ束縛。stale／別window／任意座標をdispatch前拒否。[t06](../evidence/phase9-game-structure-discovery/t06-scene-contract-migration.md)、[t13](../evidence/phase9-game-structure-discovery/t13-supervised-playbook.md) |
| GS-004 | 確認済み | Exploration Context／Proposalをtask plannerから分離し、source／revision／target／primitive／hypothesis／outcome／wait／stopを必須化。[t06](../evidence/phase9-game-structure-discovery/t06-scene-contract-migration.md) |
| GS-005 | 確認済み | AIはStructureDeltaProposalだけ。Knowledge Controllerだけがschema／identity／policy／evidenceを検証しappend。[t08](../evidence/phase9-game-structure-discovery/t08-exploration-coordinator.md) |
| GS-006 | 確認済み | Frame／Observation、hypothesis、candidate、replayed／verified、Playbookを別layerで保持。[t07](../evidence/phase9-game-structure-discovery/t07-structure-store.md)、[t13](../evidence/phase9-game-structure-discovery/t13-supervised-playbook.md) |
| GS-007 | 確認済み | nodeのstable ID／environment／signature／variant／evidence／label／verification／revisionをprojection test。[t07](../evidence/phase9-game-structure-discovery/t07-structure-store.md) |
| GS-008 | 確認済み | edgeのsource／frame-bound target／primitive／guard／risk／reversible／before／after／wait／outcome分布／evidenceをprojection test。[t07](../evidence/phase9-game-structure-discovery/t07-structure-store.md) |
| GS-009 | 確認済み | merge／split／再帰属／label／contradiction／retireが旧ID／evidenceを保持し、反証時は降格。[t07](../evidence/phase9-game-structure-discovery/t07-structure-store.md) |
| GS-010 | 確認済み | append-only Structure Event／correlation／projection replay／OutcomeUnknownをSQLite再openで確認。[t07](../evidence/phase9-game-structure-discovery/t07-structure-store.md)、[t10](../evidence/phase9-game-structure-discovery/t10-hidden-oracle-gamelab.md) |
| GS-011 | 確認済み | Run policyにapp／window／environment／primitive／budget／外部AI 0／risk／recovery／stopをimmutable固定。[t05](../evidence/phase9-game-structure-discovery/t05-discovery-admission.md)、[t08](../evidence/phase9-game-structure-discovery/t08-exploration-coordinator.md) |
| GS-012 | 確認済み | 初回一手承認、deterministic low-risk／reversible／recovery gate、purchase／delete／account／希少資源／free text禁止。[t05](../evidence/phase9-game-structure-discovery/t05-discovery-admission.md)、[t08](../evidence/phase9-game-structure-discovery/t08-exploration-coordinator.md) |
| GS-013 | 確認済み | no-progress／repeat／oscillation／capture／stale／budget／recovery lossを個別停止、blind retry 0。[t10](../evidence/phase9-game-structure-discovery/t10-hidden-oracle-gamelab.md) |
| GS-014 | 確認済み | Screen GraphとGame State Factを別projectionにし、extractor／evidence／confidence／environment／validity／resetを保持。[t07](../evidence/phase9-game-structure-discovery/t07-structure-store.md) |
| GS-015 | 確認済み | Exploration Runとtask Supervised Runを別instance／別journalにし、taskはpin済みrevisionを維持。[t13](../evidence/phase9-game-structure-discovery/t13-supervised-playbook.md) |
| GS-016 | 未確認 | graph route合成とCandidate／ReplayedのVerified拒否は確認済み。しかしGame State Factのverification／environment／validity／resetを使うFact-aware planningは未実装。Phase 9 Exit 9のFact非依存routeには使わず、claim対象外とする。[t13](../evidence/phase9-game-structure-discovery/t13-supervised-playbook.md) |
| GS-017 | 確認済み | local provider／model／prompt／vision範囲／response／resourceをversioned記録。non-loopback、API key、cloud fallback、外部AI API費用は0。[t05](../evidence/phase9-game-structure-discovery/t05-discovery-admission.md)、[t09](../evidence/phase9-game-structure-discovery/t09-vision-provider.md) |
| GS-018 | 確認済み | hidden oracleは別test processに隔離、runtime payloadにoracle state／transition／expected列0。Architecture testで依存拒否。[t10](../evidence/phase9-game-structure-discovery/t10-hidden-oracle-gamelab.md) |
| GS-019 | 確認済み | GameLab SendInputとNIKKE Nanoを混ぜず、pointer／frame-bound click／Escape／scroll／generic F13を実観測。[t05](../evidence/phase9-game-structure-discovery/t05-discovery-admission.md)、[t12](../evidence/phase9-game-structure-discovery/t12-nikke-safe-slice.md) |
| GS-020 | 強い推定 | native testでrevision／Known／Novel／frontier／probe／risk／approval／budget／recovery／stop／verificationとpause／step／abandonを確認。Game Operator内部面の外観目視のみ未確認。[t11](../evidence/phase9-game-structure-discovery/t11-explorer-ui.md) |
| GS-021 | 確認済み | user actorのappend-only correctionでlabel／identity／merge／split／edge再帰属／fact mutationを新revisionへ反映し、旧evidence保持／自動昇格なし。[t07](../evidence/phase9-game-structure-discovery/t07-structure-store.md)、[t11](../evidence/phase9-game-structure-discovery/t11-explorer-ui.md) |

## 検証と反証

- solution build: 警告0、エラー0。
- 変更直結focused: `GameLab.Discovery` 11、`Playbooks` 148、`Exploration` 16、`Input` 151、`Architecture` 8、すべてgreen。
- 最終full regression: **1014件green、失敗0、skip 0**（2026-08-24 Windows native、実行は最終ゲートの1回）。
- read-only反証: P0 0。初期指摘のverification authority、revision／policy／window／freshness pin、fresh target再束縛、User一手承認、Run Journal実payload、SQLite再open後の束縛は修正後に閉塞判定。
- NIKKE追加列はphysical replayとしてのみ採用。2 anchorとverification authorityを満たさないためVerified根拠には数えない。

## 次の未確認

これらはPhase 9 ExitとPreview claimの対象外であり、成立扱いしない。

- Game State Factの独立再抽出／verification authority、validity／resetを用いるtask planning。
- NIKKEのVerified Game Structure昇格。
- Verified Autonomous Playbook、日課完遂、一般game対応。
- NIKKE前面保持中のGame Operator内部面外観目視。
