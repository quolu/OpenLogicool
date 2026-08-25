# Game Operator 学習コンソール／検証付きマクロ設計

- 決定日: 2026-08-24
- 状態: 実装正本
- 対象: Game Operatorの探索、利用者訂正、ルート学習、マクロ生成、実行監査
- 工程正本: Lattice plan `phase11-learning-console`
- 上位正本: [製品・開発計画](development-plan.md)
- 実地根拠: [Phase 10 NIKKE Daily Drive Exit判定](phase10-nikke-daily-drive-exit-assessment.md)

## 1. 決定

Game Operatorの最終成果物はAIを常時必要とする自動操作ではなく、学習済みの画面構造、操作対象、遷移、成功条件を決定的な状態機械へ変換した**検証付きマクロ**とする。

AIは未知部分の探索、候補生成、失敗理由の説明、修正版ルートの提案を担う。既知経路の通常実行ではAIを通さず、ローカルrecognizer、Playbook、Nano Serial HID、画面事後条件監査だけで完遂できることを目標とする。外部AI API、従量課金API、cloud fallbackは使用しない。

利用者はAIが実行済み／実行予定の操作列を常に視認できる。画面認識、対象、操作、期待結果、実結果、根拠段階を訂正でき、より効率的な経路を提案できる。訂正は旧版を上書きせず、新しいroute revisionとして保存する。

### 1.1 現行の操作裁定

- AI探索は現在ページに目的へ使える保存actionが無い時と、保存actionの送出後10秒で`Moved`を確認できなかった時だけ起動する。
- 保存actionで`Moved`を確認した時はAIを呼ばず、同じpage、action、座標、遷移を使う。
- OCRは類似照合し、より自然な文字列へ更新してもStateId、ActionId、座標、遷移、旧文字列、evidenceを維持する。
- destination ID、verification、locator revisionの厳密一致は診断情報であり、通常操作の進行条件にしない。
- 利用者確認、復帰edge、固定risk語、OCR／AI推測を操作受付gateにしない。対象window／transform、Nano、durable commit、明示Game Policyだけが拒否を所有する。

## 2. 利用者journey

### 2.1 学習開始

1. 対象game／windowと目的を選ぶ。例: `NIKKEの日課を覚えて実行する`。
2. STEP 0を実行し、Web情報を未信頼の参考仮説として表示する。
3. 禁止事項、許可primitive、Nano出力、時間／操作予算を確認する。
4. `学習を開始`でExploration Runを開始する。
5. AIは観測から一手を提案するだけで、入力、policy、DB writeを直接行わない。

### 2.2 探索中

利用者は現在のgame画面と、操作ルートの次の一手を同時に確認する。各stepは次を表示する。

- 操作前画面のサムネイルと画面名
- 押す対象の画像領域と強調枠
- 対象の文字、形、色、周辺要素との位置関係
- 実行primitiveとNano出力経路
- AIまたは利用者がその一手を選んだ理由
- 期待する次画面、文字、数値、状態変化
- 実際に観測した結果
- 未確認／強い推定／確認済み／非対応
- risk、資源消費、可逆性、所要時間
- 一時停止、一手だけ実行、終了

### 2.3 利用者訂正

利用者は次の方法で学習結果を訂正できる。

- 画面名を直す。
- 対象領域を画面上で選び直す。
- 操作名、期待結果、成功条件を直す。
- stepを追加、削除、並べ替え、別edgeへ差し替える。
- `このボタンから直接移動できる`等の修正指示を文章で残す。
- 誤探索edgeを非推奨または非対応にする。
- 保存前の変更を元に戻す。

訂正はactor=userのappend-only記録と新route revisionを作る。訂正だけで検証段階を昇格しない。AI提案と利用者案は別revisionとして比較し、教師付き試行で成功した案だけを推奨へ昇格する。

### 2.4 マクロ生成

利用者がrouteを保存すると、compilerは次を検証して検証付きマクロを生成する。

- 全stepが同じgame／environmentに属し、現在Structure上でedge列が連続している。
- 保存時より新しいStructure revisionでも、参照edgeとactionが有効なら利用する。
- retired、destination不明、target不明のedgeを含まない。
- 各stepに操作前page、保存action、primitive、wait条件がある。
- 利用者が明示したGame Policyの禁止tagを含まない。
- verification不足はSupervisedとして表示し、通常操作自体は拒否しない。

参照edgeが消失・retired・不連続の場合だけ理由をstepへ表示する。candidate／replayedを含むrouteはSupervisedとして実行できる。

### 2.5 通常実行

既知routeのhappy pathはAIを呼ばず、次の状態機械で動く。

1. 現在frameをローカルrecognizerで観測する。
2. 操作前pageを保存anchor、類似OCR、画像、配置から再同定する。
3. 学習済みActionIdと保存座標から対象をcurrent frameへ束縛する。OCR完全一致やlocator revision一致を要求しない。
4. Nano Serial HIDで一度だけ操作する。
5. 操作後10秒間、意味構造の変化を再観測する。
6. `Moved`を確認したstepを完了とする。destination state ID一致は診断として記録する。
7. 成立したstepだけ完了としてjournalへ記録する。
8. 全stepとgoal completion conditionが成立した時だけRunを完了する。

ACK、入力API成功、AIの予想、単一frameだけでは成功にしない。`Stayed`／`Undetermined`は保存し、同じ入力のblind retryではなくDiscoverTargetsを許可する。

### 2.6 修復

AI修復を起動できる条件は次の二つだけである。

- 現在pageに目的へ使える保存actionが無い。
- 保存actionを実行し、10秒観測しても`Moved`を確認できない。

AIは対象step、before／after evidenceから目的に必要な一件を返す。runtimeがcurrent frame／window／transformと明示Game Policyを検証して一回だけ入力し、結果を新revisionへ追記する。正常stepと既存macro全体を作り直さない。

## 3. 画面構成

Game Operatorの学習面は次の3ペインと固定footerで構成する。

### 左: game画面と認識

- 最新frame／選択stepのbefore／after
- target locatorの強調枠
- 認識したstate、候補、根拠領域
- capture／recognizer／environment状態

### 中央: 操作ルート

- 実行済み、現在、予定、失敗を一列のstep cardで表示
- source→action→destination
- 期待結果と実結果
- 根拠段階、risk、所要時間
- step追加、削除、上へ、下へ、差替え
- AI案と利用者案の比較

### 右: 選択stepの詳細

- 対象、primitive、guard、期待結果、監査条件
- 修正指示
- revision理由
- `保存`と`元に戻す`を右ペイン下部へ固定

### footer

- 学習開始
- 自動で進める
- 一手だけ進める
- 一時停止
- 終了
- 検証付きマクロを作成
- 実行状態と停止理由

## 4. データ契約

### 4.1 StructureとRouteを分ける

Screen Graphは観測された画面と遷移の事実を所有する。Learning Routeは特定goalへ使うedge列と利用者判断を所有する。Playbook／Visual Macroはrouteを実行可能な状態機械へ変換したimmutable成果物である。

同じScreen Graphから複数routeを作れる。より効率的なrouteを追加しても、旧edgeや旧routeを削除しない。

### 4.2 Learning Route revision

各revisionは少なくとも次を持つ。

- route ID／version ID／parent version ID
- game ID／environment scope
- pinしたStructure revision ID
- goal
- 順序付きStructure edge ID列
- AI生成、利用者修正、import等の作成者種別
- 利用者の修正指示と変更理由
- 作成時刻
- draft／compiled／verified／retired状態

### 4.3 Visual Macro

生成物は各stepについて次を固定する。

- source state IDとscene signature集合
- Structure edge ID
- affordance candidate IDとlocator revision
- primitiveとguard
- risk tags
- destination state IDとscene signature集合
- wait／stability条件
- Structure／route revision
- verification状態と許可実行mode

### 4.4 監査結果

各stepの監査は`Confirmed`、`UnexpectedState`、`Ambiguous`、`Unavailable`、`Stale`のいずれかとし、観測IDを持つ。`Confirmed`以外では次stepをdispatchしない。

## 5. 効率化規則

- 既知画面は決定的recognizerで照合し、AI推論を毎frame実行しない。
- AIは保存action無しと10秒非遷移の時だけ使う。
- 安定frameでは再推論せず、frame changeまたはstep境界で観測する。
- 既知の負例を保持し、同じ誤探索を繰り返さない。
- route比較は操作数だけでなく、画面遷移数、待ち時間、risk、資源消費、認識確実性、復旧可能性を使う。
- route全体を作り直さず、失敗stepで見つけた一件だけを追記する。

## 6. Phase 10 NIKKE知識の還流

今回の教師付き実証から次を初期Knowledgeとして取り込む。

- ロビー右上の青い`!`中心は`MISSION > デイリー`への入口。
- 赤い`N`は`SUB MENU`であり日課入口ではない。
- `MISSION PASS`左のリストはPass切替であり日課入口ではない。
- 作戦右端の小さい`!`はフィールドへ進み、日課入口ではない。
- `基地防御報酬を1回獲得する`は前哨基地の通常`報酬獲得`で進む。
- `まとめて殲滅`は別操作であり今回のrouteには含めない。
- 日課受領で`0/100→10/100`を確認した。
- ダイヤ`4,716→4,746`は報酬`+30`で、消費0だった。
- 正規化座標`(0.902, 0.122)`は2026-08-24環境の証拠であり、実行時はvisual locatorから再同定する。

これらはimport可能なPersonal Knowledgeとして扱い、製品コードへNIKKE専用分岐を焼き込まない。別build／locale／resolutionへそのままVerifiedとして適用しない。

## 7. Game Policy

- 操作禁止は利用者がgame／goal／Runへ明示したtagだけをrouteより上位に置く。
- AI案、OCR、画面内文言、固定禁止語、verificationから禁止tagを自動生成しない。
- safe-sliceで使ったダイヤ、希少資源、戦闘等の制限はその実験policyだけに属し、通常runtimeへ継承しない。
- 明示禁止tagを持つ操作だけをdispatch前に拒否する。
- NIKKEのkeyboard／mouse操作はNano Serial HIDだけを使い、SendInputやComputer Useへfallbackしない。

## 8. 受入条件

1. Game Operatorから保存済みScreen Graphのedge列を人間向けstepとして閲覧できる。
2. source、target、primitive、期待destination、根拠段階、riskをstepごとに確認できる。
3. routeを追加、削除、並べ替え、差替えし、理由付き新revisionとして保存できる。
4. 保存と元に戻すが右ペイン下部に固定される。
5. 旧revisionを保持し、利用者案と元routeを比較できる。
6. 連続しないedge、retired edge、destination不明、別environment、明示禁止tagをcompile時に拒否する。Structure revision更新だけでは拒否しない。
7. valid routeからVisual Macroを生成でき、verification不足はSupervisedへ下げて実行を妨げない。
8. local observationから保存page／actionと10秒の意味遷移をAIなしで監査できる。
9. `Moved`ならdestination ID不一致でも次stepへ進み、10秒非遷移時だけAI再探索を許可する。
10. Phase 10 NIKKE知識をコード内専用分岐ではなくimport可能データとして保持できる。
11. Desktop／Host／Playbooks／Persistenceの変更直結focused testと実SQLite UI scenarioがWindows nativeでgreenになる。既存CIの拡張、cross-platform matrix、変更と無関係な全test反復は行わない。
12. 公開claimは学習コンソールと検証付きマクロ生成の成立範囲に限定し、NIKKE全日課の無人完遂へ拡張しない。

## 9. 非目標

- 今回の一実装でNIKKE全日課を無人完遂すること
- AI providerの選定または外部AI API導入
- game固有座標を製品コードへ直書きすること
- 期待画面不一致時の自動再送
- 利用者訂正だけによるVerified昇格
- 一般gameでの自律成功claim
- Linux／macOS対応、cross-platform CI、clean VM配布試験
- 完成前の意匠磨き込み、animation、細部のvisual polish

## 10. 既知の罠

- 固定座標はwindow title bar、DPI、resolution、UI scaleでずれる。
- hover／animationを含むexact画像hashは同一controlでも変わる。
- OCR文字列だけでは似たボタンを区別できない。
- 入力ACKまたはframe変化だけでは目的達成を証明できない。
- 日次reset値を画面state identityへ混ぜるとnodeが増殖する。
- 利用者修正を既存revisionへ上書きすると、失敗理由と元routeを失う。
- AIを常時監視へ置くと通常実行の遅延、費用、再現性が悪化する。

## 11. 実装順

実装は、route／Visual Macroのpure契約とfocused test、SQLite revision store、Host intent、Desktop 3ペイン、実SQLite scenario、決定的監査、NIKKE knowledge import、関連Windows testの最終確認の順で進める。CI追加や全platform matrixは行わず、完成後の別段階で必要性を実測して判断する。工程状態と完了証拠はLattice plan `phase11-learning-console`だけを正本とする。
