# Phase 9 campaign — AI Game Structure Discovery

- status: **complete（Exit成立 2026-08-24）**
- 起票: 2026-08-24
- 統括: ベル（親）。実装・反証・相談はpeertable room `OpenLogicool`
- 実行ToDoの正本: Lattice plan `phase9-game-structure-discovery`
- 上位正本: [development-plan.md](development-plan.md) §3.6.1、§6.13、§Phase 9
- 先行: Phase 0〜8B Exit、Serial HID Output Exit。Phase 9は[Exit Assessment](phase9-exit-assessment.md)で成立判定済み

> Historical plan: 一手承認、復帰経路、risk推定、反復停止を含む本campaignの旧gateは現行runtime authorityではない。2026-08-25以降は[development-plan.md](development-plan.md) §0.3／§6.13.4と[Game Interaction Foundation Contract](game-interaction-foundation-contract.md)を正とする。

## 目的

AI学習前のSTEP 0で、公式情報と攻略情報からゲームの仕組み、ルール、日課候補を出典付きMarkdown Referenceとして集める。その仮説を権限へ変換せず、端末内のローカルAIが画面を観測、探索、再観測してGame Structureを構築する。Web支援がないzero-seed pathも独立して成立させる。製品runtimeは外部AI APIへ依存せず、AI推論目的の外部送信と外部AI API費用を0に固定する。

## 統括レーン判定とF／A／H

①NIKKE実機手番、②STEP 0→構造探索→別session再現→task再生の多段受入、④裁定証跡が必要なため統括レーンとする。

- **F**: 計画、契約裁定、Control、Lattice、commit／push、phase受入、Exit
- **A**: source policy、Markdown Reference、Web取得、contract migration、Structure Store、Coordinator、GameLab、UI、focused test
- **H**: NIKKEの起動、対象window確認、未知probeの最初の一手承認、別session再現。席はHを代行しない

## 円卓

入口は更新済みpeertable room `OpenLogicool`だけ。Lattice migrationと計画commit後に新campaign missionを配る。工場更新前に送った依頼は中止済みであり、成果へ採用しない。

- 実装席はLatticeでreadyかつwrite boundaryが独立確認済みのToDoだけを取る。
- 反証席はread-onlyで、仕様、実diff、focused evidenceを反証する。
- 相談席はSTEP 0、学習UX、構造modelの設計上の異論を返す。票数で裁定しない。
- 親はToDo完了、phase受入、最終claimを実測で裁定する。

## STEP 0 source境界

- FullTextAllowed: 明示license、公式API、利用者所有資料等。正規化Markdown本文を保存できる。
- SummaryOnly: title、URL、取得時刻、短い根拠、構造化要約、候補factだけをMarkdown参照カードとして保存する。
- LinkOnly: URLと取得判定だけを保存する。
- Blocked: robots、規約、認証、network、parse等の理由を残して停止する。
- GameWithはSummaryOnly。全文HTML、画像、全文Markdownを永続化しない。
- Web内容は非信頼入力で、risk、allowed primitive、承認、budget、provider、policyを変更できない。

## 非目標

- GameWithその他の第三者ページ全文をミラー／再配布する
- Web情報だけでstate、edge、日課、Playbookをverifiedへ昇格する
- zero-seed acceptanceへWeb Referenceを混ぜる
- providerを実測前に仮決めする
- OpenAI APIを含む従量課金型の外部AI API、cloud AI、外部AI API keyを製品runtimeへ組み込む
- AI推論目的でframe、crop、OCR、embedding、prompt、responseを外部送信する
- AIからInput、SQLite、risk判定、verification昇格へ直接到達させる
- NIKKEで課金、購入、資源消費、戦闘、account変更を探索する
- 既存Capture、Durable Attempt、Run Journal、Game Policy、watchdogを再実装する

## 受入条件

1. WR-001〜012とGS-001〜021をfocused evidenceへ追跡できる。
2. STEP 0は許可sourceをMarkdown Referenceへ保存し、GameWith全文残置0、出典欠落0、Web由来verified昇格0。
3. Web Reference 0件のhidden-oracle GameLabでseed 0からnode 3件以上、edge 2件以上を発見する。
4. crash／stale／capture loss／OutcomeUnknown／budget／recovery lossでblind retry 0、scope外dispatch 0。
5. restart replayと別session再同定を成立させ、candidate／replayed／verifiedを混同しない。
6. NIKKE lobbyの非課金・非消費・非戦闘scopeで可逆edgeを発見し、別sessionで再現する。
7. learned structureからcandidate Playbookを合成し、Supervised Runで再現する。
8. Input Studio fast pathはAI／network／Web障害から独立してgreen。
9. 最終full regression、read-only反証、Exit assessment、commit／pushまで完了する。

## Lattice task仕様

### t01-step0-policy-contract

WR-001〜012をpure contractへ落とす。SourcePolicy、ReferenceDocument、ReferenceFact、ResearchRun、provenance、取得失敗、contradiction、deletionを定義する。GameWith SummaryOnlyとWeb非信頼境界をmachine testにする。

### t02-step0-store

append-only Reference revisionとSQLite persistence、再起動復元、削除preview／execute、exportを実装する。FullTextAllowed本文とSummaryOnly参照カードを別wire形にし、禁止sourceのraw本文を保存しない。

### t03-step0-acquisition

HttpClient境界、redirect／canonical URL、timeout／cancel、content digest、robots／source policy評価、HTML→Markdown正規化を実装する。取得失敗をcache成功へ丸めず、provider／source fallbackを行わない。GameWith adapterはSummaryOnly参照カードだけを返す。

### t04-step0-ui

Game Operator画面に「STEP 0 Web調査」を置く。source、保存内容、利用条件、引用、ローカルAI処理、外部AI送信なし、外部AI API費用0、期限、削除をpreviewし、調査開始、除外、再取得、削除、Markdown表示を一巡させる。cloudを有効化する設定は作らない。

### t05-discovery-admission

EXP-GS-01／04とData Flowを実測する。zero-seed visual groundingのローカルprovider／recognizerを比較し、外部AI送信0／外部AI API費用0を確認して一方式だけ選ぶ。primitiveをGameLabとNIKKEでroute別に分類し、不成立routeへfallbackしない。

### t06-scene-contract-migration

ObservedScene、AffordanceCandidate、ExplorationPolicy／Context／Proposal、StructureDeltaProposal、TransitionEvidence、GameStructureRevision、GameStateFactを追加する。ObservationResultをcapture availabilityとstate identityの2軸へmigrationし、既存consumer／fixtureを同じrevisionへ移す。

### t07-structure-store

append-only Structure Event Store、immutable Screen Graph／Game State Fact projection、schema migration、crash replay、contradiction、merge／split、retire、Knowledge Pack exportを実装する。

### t08-exploration-coordinator

観測commit→AI proposal→policy／承認→Playbook probe→再観測→Transition Evidence→StructureDelta検証を既存Durable Attempt境界へ配線する。AI／Perception／ExplorationからInput／SQLite実装への直接依存を拒否する。

### t09-vision-provider

EXP-GS-01で採用したローカル方式を実装し、Novel、同一画面候補、frame-bound affordance、構造化proposalを返す。provider／model／prompt／ローカル入力crop／resource使用量を記録し、外部AI送信経路、外部AI API key、cloud fallbackを実装しない。

### t10-hidden-oracle-gamelab

Web Reference 0件、game固有seed 0のhidden-oracle GameLabを作る。node 3件、edge 2件、no-change／loop、crash、OutcomeUnknown、capture loss、stale、budget、recovery loss、restart、別session昇格を一つずつfocused scenarioで成立させる。

### t11-explorer-ui

structure revision、Known／Novel、frontier、probe、risk／承認理由、budget、復帰経路、停止理由、candidate／replayed／verifiedを表示し、pause／step／abandon／訂正を操作できるようにする。

### t12-nikke-safe-slice

H手番。NIKKE lobbyの非課金・非消費・非戦闘scopeで、人のstate／target命名なしにopen→observe→backの可逆edgeを発見し、別sessionで再同定・再遷移する。画面before／afterだけで成功判定する。

### t13-phase9-exit

learned structureからcandidate Playbookを合成してSupervised再現、Input Studio非回帰、全focused、最終full regression一回、read-only反証、`docs/phase9-exit-assessment.md`、公開claim裁定、commit／pushを行う。親が技術判定して閉じる。

## Phase

- p0-step0-admission: t01〜t05
- p1-discovery-core: t06〜t09
- p2-verification-delivery: t10〜t13

Phaseごとに対象ToDoのfocused evidenceを揃えてreview／acceptする。H以外の完了判定をオーナーへ返さない。
