# Macro Product Flow campaign

## 結論

既存Game OperatorとInput Studioへ、AIによるマクロ作成、G13／G600割当、AI監視あり／なし再生、失敗stepのAI修復、複数マクロ合成を一つの製品journeyとして追加する。

マクロの正本は既存Learning Route revisionとVisual Macroだけとし、新しいマクロ形式、別アプリ、別DB、Probe呼出しは作らない。既存G13／G600設定UIのヘッダ、3ペイン、device模式図、layer、保存／undo、通常bindingの操作と見た目を維持する。

工程正本はLattice plan `macro-product-flow`。本書は目的、設計判断、F/A/H、非目標、受入、既知の罠を所有する。

## 利用者journey

1. Game Operatorの「マクロ」tabで、対象ゲームと達成したいことを入力する。
2. AIが既存10基盤を通って不足stepだけを発見し、Learning Routeのappend-only revisionとしてマクロを作る。
3. 保存済みマクロをAI監視なし、またはAI監視ありで再生する。
4. AI監視なしは保存actionだけをAI 0で実行し、非遷移なら停止してマクロを変更しない。
5. AI監視ありは保存actionの非遷移stepだけAIで修復し、成功後に旧版を残して新版へ更新する。
6. 複数の保存済みmacro versionを順番に連結し、一つの合成Learning Route revisionとして保存する。
7. Input Studioの既存「操作」に保存済みマクロを選び、既存G13／G600模式図からbutton／layerへ割り当てる。
8. resident host中の物理button downがfast pathを待たせずmacro invocation queueへ一回だけ入り、automation workerが対象macro versionを実行する。

## 設計判断

- **一つのマクロ正本**: Learning Route revisionがgoal、step順、版、修復履歴、合成結果を所有する。Visual Macroは同revisionから得る実行projectionである。
- **一つのrunner**: AI監視あり／なしで別runnerを作らない。保存action実行と10秒Compareは共通で、非遷移時のrepair policyだけを変える。
- **append-only修復**: 監視ありの修復成功は失敗stepのedgeだけを差し替えた新版をappendする。正常step、旧route revision、旧evidenceを保持する。
- **合成はedge参照の連結**: source macroをコピー・破壊せず、選択順のedge ID列を持つ新しいrouteを作る。scope不一致、retired edge、空macroは保存前に拒否する。
- **既存bindingの拡張**: `WorkspaceActionEntry.Outputs`へmacro invocation tokenを一件だけ置く。通常key／mouse／finite sequence文法と混在させない。
- **fast path非blocking**: Device Mappingはmacro tokenを一回のdown eventへ解決する。FastPathPumpは物理outputから分離し、有界in-memory queueへ`TryEnqueue`するだけで、AI、capture、SQLite、UI、macro完了を待たない。
- **出力session共有**: resident routeがSerial HIDなら既存protocol／emitterを借用し、別COM sessionを開かない。SendInput residentまたは非resident UIではSerial HID discoveryからmacro専用sessionを開く。
- **既存UI不変**: Input Studioへ新しい画面や新3ペインを作らない。右Inspectorの録画rowへ「マクロを選ぶ」を追加し、選んだactionを既存device図へ割り当てる。Game Operatorは既存TabControlへ「マクロ」tabを追加する。

## F/A/H

- **F**: Learning Routeを唯一正本とする判断、2再生modeの意味、append-only修復、合成整合、macro token文法、fast path非blocking、既存UI不変、Nano-only game input、commit／push／Exit判定。
- **A**: pure contract／test、Host intent、Windows／Foundry Local／Nano composition、Desktop panel、workspace editorの最小追加、resident trigger worker、fake／SQLite scenario。
- **H**: なし。実gameと実deviceは既存の自動化可能なWindows／Nano経路で観測し、人の機械操作待ちを計画へ組み込まない。

## 受入条件

1. 既存Input Studioの通常actionを保存・適用した時、WorkspaceDocument、MappingProfile、emitted edges、主要UI Automation名が変更前と一致する。
2. Game Operator UIからgoalを入力し、実gameまたは同じpublic fake経路でLearning Routeを作成できる。DesktopはSQLite、capture、AI、Nano実装を直接参照しない。
3. 保存macro versionをAI監視なしで再生し、AI call 0、route revision不変、非遷移時は停止、正常step再commit 0となる。
4. AI監視ありで一つの失敗stepだけを修復し、旧revisionと正常stepを保持した新版へ自動更新する。
5. 2件以上のmacro versionをUI順に合成し、source不変、合成route restart復元、全step順序一致を確認する。
6. 既存Input Studioの操作へmacroを選び、G13／G600の既存button／layer UIで割当・保存・再openできる。
7. 物理button downはmacro invocationを一件だけenqueueする。fast pathはAI／capture／SQLite／UIを待たず、通常key／mouse出力のlatency・ownership・releaseを維持する。
8. UI手動開始と物理button開始が同じHost macro execution coordinatorを通り、同時実行を黙って重ねない。
9. Windows実UIで作成→割当→監視なし再生→監視あり修復→合成→再起動後再生を確認する。game入力はNano、Computer Use／SendInput game dispatch／fallback／blind retry 0。
10. focused、関連test、UI目視、独立監査、最後のfull regression一回、対象限定commit、origin/main pushを完了する。

## 非目標

- Input Studioの再設計、device図の描き直し、別Macro Editorアプリ
- Learning Route／Visual Macroと競合する第三のmacro schema
- fast path threadでのAI、capture、SQLite、UI待機
- macro発火をkeyboard／mouse outputへ偽装すること
- source macro revisionの上書き、正常stepの再学習、全route再作成
- 一般game無人完走、課金／消費／戦闘操作のclaim
- G13／G600 firmware、onboard profile、既存通常binding contractの変更
- 別作業所有のProbe 3差分と既存未追跡probe出力の変更、削除、commit

## 既知の罠

- resident Serial HIDとmacro用に同じCOMを二重openすると片方が失敗する。既存sessionを借用し、ownershipを二重化しない。
- macro tokenを`OutputTokens.Parse`へ流すと通常emitterがfaultする。FastPathPumpで物理outputと分離し、emitterへ渡さない。
- 物理button upでmacroを再発火しない。macro tokenはdown一回だけの有限triggerとしPressOwnershipを作らない。
- 監視なしで非遷移後にAIへ落ちるとmode契約違反になる。repair policyをHost intentから一手runtimeへ明示する。
- 合成routeのenvironment scopeが混在すると保存action座標を別windowへ適用し得る。同一game／environmentだけを合成する。
- 既存LearningRoutePanelは手編集・教師付き実行面として残す。新しい「マクロ」tabへ機能を移して空洞化しない。
- UIの追加を理由にInput Studio全体を新ViewModel／DI frameworkへ移行しない。

## 並列化裁定

Contracts、Profiles、Input、Host、Desktopが同じmacro tokenとroute意味を依存順に変更し、Input Studioの単一Windowへ最終統合する。共有dirty treeには別作業所有のProbe差分もある。複数writerの衝突と受入費用が利益を上回るため、親が直列実装する。独立read-only反証だけを終端で使う。

