# t13 learned structure→Supervised Playbook 受入記録

日付: 2026-08-24
判定: **成立**

## 結論

hidden-oracle GameLabで、game固有seed 0から発見したnode／edgeを独立Exploration sessionで`Candidate → Replayed → Verified`へ一段ずつ昇格した。そのstructure revisionをfreezeした後に初めてtask用Playbookを合成し、元のExploration Runとは別のSupervised Runで同じ遷移を再現した。

Supervised Runは保存locatorを直接押さない。別game instanceの新鮮なObservationでsource stateを再同定し、同じprimitive／locator type／IoUで一意なcandidateへ再束縛した。その後にPlaybook version、structure revision、source state、Observation、frame sequence、transform revision、policy revision、consent revisionへ束縛した利用者の一手承認を通し、Durable Attemptの`Proposed → Authorized → DispatchArmed → Reported → Observing → Confirmed`で一回だけclickした。

## 不変条件とfail-closed

- Playbookがpinしたstructure revision／environment／edge列と不一致なら合成／解決不可。
- `Verified` modeはnode／edgeのいずれかがCandidate／Replayedなら不可。Supervisedだけがこれらをcandidateとして扱える。
- 保存Observationから復元したlocatorは、fresh Observationで対象window sourceがpolicyと完全一致し、freshnessがstop policy以内の場合だけ再束縛できる。
- 別window、stale frame、ambiguous target、retired node／edge、ポリシー外primitive／prohibited risk、承認束縛不一致はdispatch 0で拒否。
- verification authorityは、nodeではbefore／after Observationのstate hypothesis、edgeではcandidate／primitive／outcome／source／destinationを独立sessionのTransition Evidenceと照合する。無関係なevidenceによる昇格は拒否する。Factは独立再抽冺evidence contract未定義のため、node／edge用evidenceでは昇格不可。

## 再起動後の証拠

Supervised RunのRun Journalに次の7 eventの実payloadをappendした。

1. fresh before `ObservedScene`
2. frame再束縛済みaction proposal
3. actor=`User`の`StructurePlaybookStepApproval`
4. authorized actionの`DispatchArmed`
5. dispatch result
6. after `ObservedScene`
7. destination state／state hypothesis／`Confirmed`

SQLite接続を一度閉じ、新しい接続で同じRunを読み戻して、structure／policy／consent／frame／前後Observation／destinationの束縛が保持されることをassertした。最終Attemptは`Confirmed`、accepted click 1、Run event 7である。

## NIKKE追加実測と区別

t13中にNIKKEでも学習済みlocatorのopen、遷移先観測、Escape、帰還観測をNano Serial HIDだけでもう1列再現した。`SendInput=0`、`ComputerUse=0`、外部AI送信=0、課金／消費／戦闘／account変更=0である。

ただし、この追加列は出発・帰還観測が2 anchor規則を満たさず、Structure verification authorityも通していない。したがって追加のphysical replayとしてのみ採用し、NIKKEのstructureは`Replayed`のままとする。GameLabのVerified構造からのSupervised Run成立を、NIKKEの`Verified Game Structure`へ読み替えない。

## 検証

- `OpenLogicool.GameLab.Discovery.Tests`: 11件 green。
- `OpenLogicool.Playbooks.Tests`: 148件 green。
- read-only反証: P0 0。verification authority、revision／policy／window／freshness pin、一手承認、Run Journal、別Supervised Run、SQLite再openのコード契約に重大欠落なし。
- 最終full regressionとcommit／pushは[Phase 9 Exit Assessment](../../docs/phase9-exit-assessment.md)に記録する。
