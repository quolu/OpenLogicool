# 目的指向の逐次探索 campaign

## 結論

利用者のgoalを、一手ごとの既存`ProductGameExplorerRuntime`の上で最後まで進める製品runtimeを追加する。保存済みLearning Routeがあれば各edgeの保存actionをAIなしで実行し、操作後10秒の`Compare`が`Moved`なら次stepへ進む。保存actionが無い時と、保存actionの10秒非遷移後だけ、同じgoalに必要な一件を`DiscoverTargets`で探す。

初回探索では`Moved`になったnode／edgeをLearning Routeのappend-only revisionへ逐次追記する。既存routeのstepが非遷移になった時は、そのstepのAI再探索で得たedgeだけを差し替え、正常step、前後step、旧route revision、旧evidenceを保持する。再起動後は同じgame、environment、goalから同じroute IDを解決し、AI call 0でroute全体を再現する。

工程の正本はLattice plan `purpose-directed-exploration`とする。本書は目的、設計判断、受入条件、非目標、既知の罠だけを所有する。

## 設計判断

- 上位runtimeは`Observe`、`DiscoverTargets`、`Hover`、`Click`、`KeyTap`、`Scroll`、`Drag`、`WaitStable`、`Compare`、`LearnTransition`を合成済みの`ProductGameExplorerRuntime`だけを一手実行器として使う。
- goal文字列からgame固有state、target、座標、正解routeをcodeへ埋め込まない。初回の未知stepは既存Foundry Local target discoveryへ同じgoalを渡し、一件だけ選ぶ。
- Learning Route IDはgame、environment、goalから決定的に作る。再起動時にcallerが前回の一時IDを保持しなくても同じrouteを読めるようにする。
- 保存routeの再生では、現在stepのStructure edgeが持つtarget semantic keyとprimitiveをknown-first discoveryへhintとして渡す。OCR完全一致、destination ID一致、locator revision一致、verification状態はhintの利用gateにしない。
- 操作後の10秒観測中は次step用のAI target discoveryを始めない。`Compare`完了後、現在stepが`Moved`でない時だけAI再探索を許可する。
- `Stayed`と`Undetermined`は一手の学習結果として保存し、goal Run自体の失敗へ丸めない。次の試行は同じ入力のblind retryではなく、許可されたAI再探索による別Attemptとする。
- routeの逐次保存はdurable commitであり、保存失敗時は次の入力を出さない。
- goal完了判定は操作拒否ではない。初回探索では利用者goalに対応する明示completion evaluatorが判定し、保存route再生では全edgeの`Moved`完了をgoal完了とする。

## 受入条件

1. 保存route無しのgoal Runが、未知stepだけAIを使い、各`Moved` edgeを順番どおりLearning Routeへ逐次保存する。
2. route途中の`Stayed`／`Undetermined`を学習済みoutcomeとして保持し、同じstepだけAI再探索へ移る。
3. AI再探索で`Moved`を得た時、最新route revisionは失敗stepだけを新edgeへ差し替え、正常stepと旧revisionを保持する。
4. route保存失敗、current window／frame／transform不成立、Nano不達、明示Game Policy拒否では次の入力を送らない。
5. 同じSQLite DBを閉じてWindows compositionを作り直した後、同じgoalの全stepをAI call 0で再現する。
6. 一つの実game上の実目的を初回探索から完了まで実行し、再起動後のAI 0再現を同じevidenceへ記録する。
7. SendInput dispatch 0、Computer Use input dispatch 0、fallback 0、blind retry 0を証拠化する。
8. focused test、関連test、Windows実走、最後のfull regression一回、独立監査、対象限定commit／pushまで閉じる。

## 非目標

- 一般gameの無人自律成功claim
- NIKKE全日課の完遂
- cloud AI、外部AI API、Computer Use、SendInputのゲーム入力
- destination ID、verification、ReviewStatus、一手承認、復帰edge、OCR禁止語、反復回数を通常操作gateへ戻すこと
- 既存の正常routeや全buttonを事前に再探索すること
- 別作業所有のProbe差分3ファイルと既存未追跡probe出力の変更、削除、commit

## 既知の罠

- 一手runtimeの`WaitStable`中に通常のtarget discoveryを呼ぶと、遷移確定前に次step用AIが走り得る。比較観測と次step探索のphaseを分離する。
- Structure edgeの`AffordanceCandidateId`とknown-screen indexの保存ActionIdは同一とは限らない。再生hintは保存済み`TargetSemanticKey`、primitive、位置、類似OCRを使う。
- Learning Routeはappend-onlyであり、逐次保存を同一revisionの上書きで実装しない。
- 非遷移後に古い保存actionへ交互に戻ると修復が進まない。修復中のstepは`Moved`までAI再探索側へ保持する。
- 既存route完走だけで初回探索のgoal完了を推測しない。初回は明示completion evaluator、再生は保存route終端を使い分ける。

## 並列化裁定

契約、known-first、目的run、Windows composition、実SQLite scenarioが同じHost／Contracts／tests面を依存順に変更し、共有dirty treeには別作業所有のProbe差分がある。scope分離した複数writerの利益より受入と衝突回避の費用が大きいため、同一親が直列に実装する。read-onlyの独立監査だけを終端で利用する。

## 工程参照

実行ToDo、依存、状態、完了証拠はLattice plan `purpose-directed-exploration`だけを正本とする。
