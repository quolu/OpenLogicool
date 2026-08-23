# t04 STEP 0 Web調査 UI evidence

- 日付: 2026-08-24
- Lattice: `phase9-game-structure-discovery / t04-step0-ui`
- 判定: 確認済み

## 成立した利用経路

Input Studio下部の `Game Operator` からSTEP 0画面を開く。URLと確認済みの利用条件／取得許可を入力すると、取得前に次を表示する。

- 取得方針、保存内容、引用上限、保存期限、削除対象
- AI処理はこのPC内
- 外部AI送信なし
- 外部AI API費用0円

同じ `IWebResearchIntent` 経路で、取得開始、source除外、再取得、削除preview→削除、保存済みReferenceのMarkdown表示を行う。DesktopはHTTPとSQLiteを直接呼ばず、Host adapterが既存`WebReferenceAcquisitionService`と`SqliteWebReferenceStore`へ接続する。

cloud有効化、外部AI API key、外部AI provider入力のUI／intentは存在しない。GameWithのSummaryOnly取得は、ローカルmodelが未選定の間は`ProviderUnselected`を明示し、外部APIへfallbackしない。

## focused evidence

- `WebResearchWorkspaceTests`: fake intentでpreview→start→exclude→reacquire→list→Markdown→deleteを同じpublic経路から一巡、2件green。
- `HostWebResearchIntentTests`: fake local summary provider＋実SQLite migration/storeでGameWith SummaryOnlyの保存、Markdown、再取得、削除墓標を一巡、1件green。
- 関連test: AI／Conformance／Playbooks／Desktop／Host／Persistenceの6 projectを実行し、失敗0・skip 0。
- 製品起動: `OpenLogicool.Host ui --duration-ms 1500`を実DB migration込みで実行しexit 0。

## 次工程との境界

t04はローカルAI方式を仮決めしない。実runtimeで使うvision／summary modelの選定、model取得量、memory、latency、unknown棄却、cancelはt05 Discovery Admissionで実測する。
