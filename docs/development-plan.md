# OpenLogicool 製品・開発計画

- 版: 0.3（2026-08-15監査 [reviews/plan-audit-2026-08-15.md](reviews/plan-audit-2026-08-15.md) の指摘反映）
- 改訂日: 2026-08-15
- 状態: 改訂版・実装着手前
- 対象: Logicool G13 / G600を統合するWindowsネイティブアプリと、画面認識付き逐次学習プレイブック
- 比較基準: Logicool ゲームソフトウェア 9.04.49
- 成立性資料: [G13/G600 Windows成立性調査](../rag/openlogicool/feasibility-2026-08-14.md)
- 一次資料台帳: [Primary-source manifest](../rag/openlogicool/raw/source-manifest.md)

## 0. この計画の結論

OpenLogicoolは、次の二つの製品価値を同じアプリで提供する。

1. Input Studio: ゲーム／アプリを先に選び、G13とG600を一つのワークスペースで設定するLGS代替。
2. Game Operator: 画面を観察し、利用者の目的から操作を提案・実行・確認し、停止・修正・現在状態から再開できるプレイブック基盤。

両者は同じ意味操作とアプリプロファイルを共有するが、稼働条件とrelease gateを分ける。AI、network、captureが利用不能でもInput Studioは動作しなければならない。Input Studioを完成させるためにGame Operatorを待たず、Game Operatorを急ぐためにG13/G600の低遅延入力経路へAIを混ぜない。

公開先行実装から主要経路の成立可能性は確認できたが、本製品のWindows実機環境で致命的ブロッカーがないことは未実証である。G13入力、G600 Feature Report、G600 live input routeが成立するまで、ユーザーモード製品の成立性はUnverifiedとする。特に「ユーザーモードだけでG600の全ボタンを無制限のアプリプロファイルへ割り当て、元入力を重複させない」ことは未成立である。次の三方式から実測後に明示的に選ぶ必要がある。

- 方式A: 3個のオンボードプロファイルを直接利用する。
- 方式B: 中間usageをユーザーモードで変換する。
- 方式C: 物理入力抑止を署名済みdriverで実装する（出力はSendInputまたは仮想HID。2変種は§6.6で別capabilityとして評価する）。

本書で「方式A／B／C」はこの3方式だけを指す。route決定は2段で行う: read-onlyで判定できる範囲（G0-Device-RO、Phase 0内）と、Migration Safety Gate通過後の最小write実験を要する範囲（G0-Device-W、Phase 2入口）を分け、最終決定はG0-Device-Wで行う。

したがって、今すぐ許可するのはPhase 0のread-only調査、契約draft、UX prototype、GameLab prototypeまでとする。製品実装のGoは分野別admission gateで決める。

### 0.1 根拠の表記

本書では判断を次の4値で扱う。

- 確認済み: 実機、実ファイル、一次資料、focused experimentのいずれかで確認した。
- 強い推定: 複数の公開実装または仕様から成立可能性が高いが、本製品環境では未実測。
- 未確認: 実測または契約が不足しており、対応済みとは表示できない。
- 非対応: 技術、規約、製品方針のいずれかにより対応しないと裁定した。

UnverifiedをSupportedとして表示しない。実験失敗を別方式へ黙ってfallbackして成功扱いしない。

### 0.2 初期技術裁定

- 言語／runtime: C#、.NET 10 LTS、net10.0-windows。
- UI: WPF。Windows標準APIとの統合、tray常駐、Raw Input message loop、成熟した配布経路を優先する。
- 初期architecture: 一つの常駐process内をmodule分割する。UIを閉じてもtrayで入力runtimeを維持する。
- 対象architecture: x64を最初の基準とし、ARM64はsupport matrixで別判定する。
- 永続化: SQLiteとversioned JSON export。
- Windows API: Raw Input、HID、SendInput、Windows Graphics Captureを第一候補とする。
- 開発環境: WSL2は文書、domain、fixture等へ利用できるが、Windows UI、HID、capture、input、installerの受入はWindows native実行だけを証拠とする。
- kernel driver: Phase 0の実測で必要性が成立するまで実装しない。必要と判明した場合は独立した署名・配布計画へ分岐する。
- package: 開発中はunpackagedを許す。Dynamic Lightingのbackground制御または公開配布の前にMSIX／Sparse Package／MSIを実測して決定する。

技術裁定は実測で反証された場合に変更できる。変更時は対象requirement、影響module、migration、rollbackを記録する。

## 1. 製品語彙

| 用語 | 意味 |
|---|---|
| Application Workspace | 一つのゲーム／アプリに属するG13、G600、意味操作、automation、履歴をまとめた編集単位 |
| App Profile | 対象アプリの識別条件と、そのアプリで有効になる設定revision |
| Semantic Action | 「回避」「報酬を受け取る」など、物理キーや座標から独立した利用者の意図 |
| Binding | 物理control、layer、条件をSemantic Actionまたは低水準commandへ結び付ける設定 |
| Input Macro | key、mouse、chord、有限時間のsequenceを決定的に実行する低水準手順 |
| Playbook | 観測可能な前提、Semantic Action、期待結果、分岐を持つversioned状態graph |
| Run | 特定Playbook versionを使う一回の永続実行instance |
| Attempt | 一つの操作提案から結果確定または放棄までを表す単位 |
| Observation | capture frameを状態候補、確度、根拠領域、鮮度へ変換した結果 |
| Knowledge Pack | ゲーム固有のapp identity、state、recognizer、action参照、playbook、fixtureを収めるversioned data |
| Screen Graph | 画面（state）をnode、画面内のvisual targetと画面遷移をedgeとする、ゲームの構造地図。Playbookから独立した第一級成果物 |
| Learning | modelのfine-tuningではなく、観測、訂正、成功経路をKnowledge Pack／Playbookへversioned保存すること |
| Verified Step | GameLabではscenario oracle付き別session、実gameでは同一環境scopeの独立live sessionで再現済みのPlaybook step（凍結acceptance datasetは評価専用で昇格根拠にしない。§10.3） |

一般化するのは観察、計画、操作、確認、学習の仕組みである。任意ゲームを初見で完全自動化できるとは約束しない。ゲーム固有知識はKnowledge Packとして追加する。

## 2. 製品目標と境界

### 2.1 製品目標

- ゲームを一度選べば、同じ画面でG13とG600を設定できる。
- 物理キー中心ではなく、意味操作中心で両デバイスを編集できる。
- LGS 9.04.49のG13/G600向け常用機能を、実機受入付きで置き換える。
- 利用者が自然言語で目的を伝えると、AIが現在画面と既存知識から次の一手を提案できる。
- 操作を実行した事実と、ゲーム内で成功した事実を分離して記録する。
- 成功した一手を直ちにcandidateとして追記し、翌日等の別sessionで再現性を検証する。
- 途中停止、手動介入、手順訂正、process再起動後も、現在画面を照合して続きを再開できる。
- 失敗時に、何が失敗し、何を自動実行しておらず、次に何を選べるかを利用者へ示す。

### 2.2 製品境界

- ゲームprocessへのDLL注入、memory read／write、anti-cheat回避を行わない。
- 未確認のゲーム規約を自動運転許可として扱わない。
- Windows secure desktopへ入力しない。
- Input APIが成功したことをゲーム内成功と扱わない。
- 画面内文字列、OCR結果、importしたKnowledge Packを信頼された命令として扱わない。
- AI providerを黙って切り替えない。localからcloudへのfallbackも行わない。
- 課金、希少資源の消費、account変更、削除等のhigh-impact操作は、利用者が対象actionを明示許可しない限り自動確定しない。
- 未確認機能をLGS同等、一般ゲーム対応、完全自動化対応と表示しない。
- LGS/G HUB設定を既定で削除しない。復元を実証できないdevice writeを製品機能にしない。

### 2.3 Release claim

| Claim | 使用可能になる条件 |
|---|---|
| Device Preview | read-onlyでデバイスとcapabilityを正しく表示できる |
| Core LGS Replacement | 常用代替必須行が実機受入済みで、導入・復帰手順がある |
| App-first Unified Configuration | 一つのApplication Workspaceで両機の保存・適用状態を個別確認できる |
| Durable Automation | GameLabで全crash boundary、停止、修正、再開の不変条件を満たす |
| AI-assisted | 観察／提案／承認付き実行が凍結evalを通る |
| Verified Autonomous Playbook | 対象ゲーム、version、環境、規約の範囲でverified stepだけを無人実行できる |
| LGS Parity | canonical inventoryでLGS 9.04.49上の存在を確認した全capabilityが、対象support matrixでSupportedである |

NIKKEの可視desktop領域を2回取得できた実測は、探索的なcapture成立証拠に限る。継続capture、WGC window capture、遮蔽、最小化、認識pipeline、操作結果確認、規約許可は未確認であり、上記claimには算入しない。

## 3. 要件catalog

Release列は、R1 Core、R2 Unified UX、R3 Durable Lab、R4 AI Pilot、R5 Stable Distributionを示す。LGS Parityはrelease番号とは別のclaim gateであり、機能を対象外へ移して達成扱いにしない。

### 3.1 Application／Profile

| ID | Release | 要件 |
|---|---|---|
| APP-001 | R2 | アプリを実行中一覧、インストール済み一覧、EXE参照から追加できる |
| APP-002 | R2 | 編集対象と現在有効なアプリを別状態として常時表示する |
| APP-003 | R2 | 一つのWorkspaceでG13、G600、Semantic Action、Binding revisionを編集する |
| APP-004 | R2 | app identityはbasenameだけに依存せず、正規化path、package identity、process世代、必要時window条件を持つ |
| APP-005 | R2 | launcherからgame本体への遷移、同名EXE、再起動、Alt+Tab、window消失を診断可能にする |
| APP-006 | R2 | foreground変更はprofile generationを原子的に切り替える |
| APP-007 | R2 | 編集中、保存済み、runtime適用済み、device反映済みを区別し、部分成功を一括成功と表示しない |
| APP-008 | R2 | identity不明時はUnknown Applicationへ移り、直前profileを黙って継続しない |
| APP-009 | R5 | LGS profile importは変換可能行と未対応行をdry-runで表示し、元設定を変更しない |
| APP-010 | R3 | 同じWorkspaceでPlaybookとexecution historyを編集・閲覧する |
| APP-011 | R4 | 同じWorkspaceでAI automationと実行modeを設定する |

### 3.2 Device／Input

| ID | Release | 要件 |
|---|---|---|
| DEV-001 | R1 | G13 046D:C21CとG600 046D:C24Aをinstance単位で識別し、hotplug、sleep/resumeを扱う |
| DEV-002 | R1 | G13のG1〜G22、stick、取得可能な補助keyを押下／解放として読む |
| DEV-003 | R1 | G600のcurrent profileと3 profileをread-onlyで完全backupできる |
| DEV-004 | R1 Gate | G600通常／G-Shift側全controlのlive input routeと元入力重複を実測して方式を決定する |
| DEV-005 | R1 | device capabilityをSupported／Experimental／Unsupported／Unverifiedで表示する |
| DEV-006 | R1 | 単一key、mouse、chord（同時押し基本形）、有限sequenceをWindows通常入力経路で出力する（timed multi-key・repeat・toggleはMAP-007のR5） |
| DEV-007 | R1 | profile／layer変更前にdownした入力は、down時に固定したgenerationでreleaseする |
| DEV-008 | R1 | pause、device切断、通常終了時は新規downを止め、所有中outputをreleaseする |
| DEV-009 | R1 Gate | hard crash時のoutput残留を実測し、残留しない証拠または期限内にreleaseするwatchdogをSupported pathの条件にする |
| DEV-010 | R5 | G600 device write（154-byte profile書換等の永続write一般）は完全backup、byte diff、readback、power cycle、restoreが通ったcapabilityだけ有効化する |
| DEV-013 | R2 | 方式A採用時のF0 active slot切替writeは、DEV-010の一般write gateと分離し、EXP-G600-03（backup・readback・restore付き）の受入だけでR2から有効化できる。app-first切替（APP-006）のG600側実装はこの切替だけを使い、154-byte profile writeを使わない |
| DEV-011 | R5 | G13 RGB／M LED／LCDは標準入力を壊さない独立実験後だけ有効化する |
| DEV-012 | R1 Decision／R5 Delivery | device単位の元入力抑止が必要と実証された時点でfilter driver／virtual HID計画へ分岐し、driverなしのclaimを制限する |

### 3.3 Mapping／Macro

| ID | Release | 要件 |
|---|---|---|
| MAP-001 | R2 | Semantic Actionを複数device controlへ割り当てられる |
| MAP-002 | R2 | 同じSemantic ActionへG13とG600の両方から到達できる |
| MAP-003 | R1 | G13 M1／M2／M3相当とG600 G-Shift相当のlayerを扱う |
| MAP-004 | R2 | 未割当、重複、循環、到達不能layer、元入力重複の可能性を適用前に表示する |
| MAP-005 | R1 | key down時にdevice instance、control、press generation、profile、layer、mapping revision、output集合を固定する |
| MAP-006 | R1 | key upは現在profileを再解決せず、down時のoutput集合だけを解放する |
| MAP-007 | R5 | delay、repeat while held、toggle、有限回repeatを明示状態として扱う |
| MAP-008 | R1 | 無期限holdまたは取消不能macroを作らない。全macroに停止境界を持たせる（自動化が入力を送る最初のreleaseから適用。§6.5） |
| MAP-009 | R2 | 設定revisionの保存、undo、export、importを提供する |
| MAP-010 | R2 | foreground appに応じた切替でG600の154-byte永続profileを毎回writeしない |

### 3.4 Durable Playbook

| ID | Release | 要件 |
|---|---|---|
| PB-001 | R3 | Playbookを前提、状態、Semantic Action、期待結果、分岐を持つgraphとして保存する |
| PB-002 | R3 | Runは開始時のimmutable Playbook versionへpinする |
| PB-003 | R3 | 操作前にAttemptとDispatchArmedをcommitしてから外部入力を呼ぶ |
| PB-004 | R3 | DispatchArmed以降の未解決AttemptはOutcomeUnknownとして扱い、自動再送しない |
| PB-005 | R3 | Windows入力、ゲーム内効果、SQLiteを一つのtransactionとして扱わない |
| PB-006 | R3 | 観測、提案、承認、dispatch、結果、確定、訂正、手動介入をappend-only eventとして保存する |
| PB-007 | R3 | pause、一手実行、skip、abandon、手動介入、未来手順の編集を提供する |
| PB-008 | R3 | 訂正は新versionを作り、確定済みeventを変更しない |
| PB-009 | R3 | 再開前に対象app、version、現在Observationを照合し、UniqueMatch以外では自動再開しない |
| PB-010 | R3 | runごとに一つのactive executorだけを許し、stale executorからの進行を拒否する |
| PB-011 | R3 | checkpointをsource of truthにせず、journalから再構築できるprojectionとして扱う |
| PB-012 | R4 | stepを環境scope付きcandidate、replayed、verifiedへ昇格し、初回成功やfixture成功を実gameの決定的経路と表示しない |
| PB-013 | R3 | 手動介入中の操作をAIまたは直前Attemptの成功原因へ自動帰属しない |
| PB-014 | R3 | disk write不能時はDispatchArmedを作れないため、外部入力前に停止する |

### 3.5 Capture／Perception

| ID | Release | 要件 |
|---|---|---|
| CAP-001 | R4 | window／display、backend、sequence、monotonic time、size、pixel format、color space、DPI、rotation、crop変換をFrameへ付与する |
| CAP-002 | R4 | black、stale、drop、resize、device lost、backend change、遮蔽、最小化を別状態として診断する |
| CAP-003 | R4 | backend変更や座標系変更時は観測連続性を切り、再校正まで自動入力を止める |
| CAP-004 | R4 | backend選択と失敗理由を記録し、別backendへの切替を利用者へ明示する |
| CAP-005 | R4 | support matrixをwindowed／borderless／fullscreen、DPI、HDR、multi-monitor、遮蔽、最小化ごとに持つ |
| PER-001 | R4 | ObservationResultをKnown、Ambiguous、Unknown、Unavailableに分ける |
| PER-002 | R4 | Observationへframe ID、age、recognizer version、候補、校正済みconfidence、evidence regionを付ける |
| PER-003 | R4 | Known以外を既知Playbookの自動実行条件にしない |
| PER-004 | R4 | 成功条件を操作前後の時系列と安定観測窓で判定し、単一frameだけに依存しない |
| PER-005 | R4 | 対象window、capture source、input targetが一致しない場合はdispatch前に停止する |
| PER-006 | R4 | absolute座標だけのstepはfragileと表示し、anchorまたはvisual targetへ昇格できない限りverifiedにしない |

### 3.6 AI／Knowledge

| ID | Release | 要件 |
|---|---|---|
| AI-001 | R4 | 利用者の自然言語goalを小目標と次の構造化proposalへ変換する |
| AI-002 | R4 | AIはSendInput、HID write、DB writeを直接呼ばず、NextActionProposalだけを返す |
| AI-003 | R4 | verified runでは既存Semantic Action IDだけを提案できる |
| AI-004 | R4 | teach modeおよびSupervised Runの未知操作は、Perceptionが現在frameから列挙したvisual targetと事前許可primitiveだけを選べる（frameに紐付かない絶対座標の提案はmodeを問わず拒否する） |
| AI-005 | R4 | schema version、catalog version、state前提、risk class、承認要否が不一致のproposalをPlaybookが拒否する |
| AI-006 | R4 | prompt、model、parameter、dataset、Knowledge Pack versionをrunへ記録する |
| AI-007 | R4 | provider/modelを精度、未知棄却、p50/p95遅延、取消、費用、data policyで比較する |
| AI-008 | R4 | provider切替、local/cloud切替、送信範囲変更を暗黙に行わない |
| AI-009 | R4 | session／日単位の費用上限を利用者が設定でき、到達時は次のdispatch前に停止する |
| AI-010 | R4 | OCR、画面内指示、import dataは非信頼入力であり、system policyや権限を変更できない |
| AI-011 | R4 | model fine-tuning、provider側学習、Playbook学習を別概念としてUIと記録で区別する |
| KP-001 | R4 | Knowledge Packにgame/build、locale、UI scale、state、anchor、success condition、action参照、schema、出典、license、検証状態を持たせる |
| KP-002 | R4 | Knowledge Packは実行code、任意script、provider変更、秘密を含めない |
| KP-003 | R4 | import直後はUntrusted／Candidateとし、対象環境で検証するまで自動実行へ使わない |
| KP-004 | R4 | Screen Graph（state、visual target、遷移）をPlaybookの付属品ではなく独立成果物として保存・閲覧・versionできる |
| KP-005 | R4 | Observe Onlyで利用者の実操作を観察し、入力を行わずにScreen Graphのcandidate node／edgeを蓄積する。candidateは検証まで自動実行の根拠にしない |

### 3.7 UX／Operations

| ID | Release | 要件 |
|---|---|---|
| UX-001 | R1 | 初回起動で各device、LGS/G HUB、driver、利用可能capability、read-only／ownership状態を表示する |
| UX-002 | R2 | app選択から両device設定までをdevice別画面の往復なしで完了できる |
| UX-003 | R3 | 提案待ち、承認待ち、入力中、結果確認中、利用者停止、対象不一致、認識不能、完了、失敗を常時表示する |
| UX-004 | R3 | pause／emergency stopをAI、capture、対象deviceに依存しない操作で提供する |
| UX-005 | R3 | 再開時に最後のconfirmed state、現在state、差分、採用version、次の操作を表示する |
| UX-006 | R2 | device未接続、競合、app identity不一致、入力不達に利用者向け復旧選択を出す |
| UX-007 | R2 | 主要flowをkeyboardだけで完了でき、状態を色だけで表さず、high contrastと表示倍率へ対応する |
| UX-008 | R4 | capture不能、AI障害、Observation照合不能に利用者向け復旧選択を出す |
| OPS-001 | R1 | AI／networkが停止していてもdevice mappingとknown deterministic macroを利用できる |
| OPS-002 | R1 | 設定とdevice backupをapp再起動後に復元できる |
| OPS-003 | R4 | full-screen画像の永続保存とcloud送信を既定OFFにする |
| OPS-004 | R4 | secretをWindows Credential ManagerまたはCurrentUser保護領域へ保存し、log、journal、export、dumpへ含めない |
| OPS-005 | R2 | device／input向けengineering logと診断bundleを利用者preview付きで生成する |
| OPS-006 | R5 | install、update、rollback、repair、uninstall、LGS復帰をclean Windows環境で検証する |
| OPS-007 | R5 | 公開artifact、installer、update manifestを署名・timestampし、SBOMとThird-Party Noticesを同梱する |
| OPS-008 | R3 | journal、Playbook、active Runをapp再起動後に復元できる |
| OPS-009 | R3 | execution journalとengineering logを分離し、correlation IDで一遷移を追跡できる |

### 3.8 非機能予算

以下は初期engineering targetであり、Phase 0のreference machine計測後にbaseline化する。結果を見た後に都合よく緩和する場合は理由と利用者影響を記録する。

| ID | 指標 | 初期target |
|---|---|---|
| NFR-001 | Raw Input handler占有 | p99 1 ms以下。HID I/O、DB、UI、AIをhandler上で実行しない |
| NFR-002 | input dispatch遅延 | WM_INPUT取得からSendInput返却までp99 10 ms以下、最大値も記録 |
| NFR-003 | profile切替 | foreground eventから新generation確定までp99 100 ms以下 |
| NFR-004 | macro timing | 10 ms以上のintervalでjitter p99 5 ms以下を目標とする |
| NFR-005 | parser | 1,000,000 recorded report replayでedge欠落、重複、順序逆転0 |
| NFR-006 | generation競合 | 1,000回のkey保持＋profile/layer切替でwrong release 0 |
| NFR-007 | hotplug | 50回の抜差し、20回のsleep/resumeでstale handle 0、再取得p99 2秒以下 |
| NFR-008 | handled stop | 停止受付後に新規actionを発行せず、所有keyを250 ms以内にreleaseする |
| NFR-009 | soak | 8時間でqueue、handle、memoryが継続増加せず、core idle CPUのbaselineを超え続けない |
| NFR-010 | accessibility | app選択、binding、AI依頼、停止、訂正、再開、診断をkeyboardのみで完了 |
| NFR-011 | privacy | 既定の診断bundleとexportにframe、OCR本文、prompt、secretを含めない |
| NFR-012 | durability | 全fault pointでunknownの自動再送、false success、journal再生不一致0 |

### 3.9 Requirement traceability

| Requirement群 | 主Phase | Owner Lane | 主な受入証拠 |
|---|---|---|---|
| APP-001〜008、MAP-001〜006／009／010、UX-001／002／006／007 | Phase 2／3 | A、D | UI scenario、profile generation、input acceptance |
| APP-009、MAP-007、OPS-006／007 | Phase 8A | A、K | LGS import dry-run、clean VM、signed artifact |
| APP-010 | Phase 4 | E、A | Playbook UI scenario |
| MAP-008 | Phase 2 | D | 停止境界・250ms release test |
| APP-011 | Phase 6 | A、H | 実行mode設定scenario |
| OPS-001／002 | Phase 2 | D、K | AI/network遮断動作test、再起動復元test |
| DEV | Phase 0／2／8A | B、C、D | device report、golden vector、実機readback、hotplug |
| PB、OPS-008／009、UX-003〜005 | Phase 4 | E、I、K | state model、journal replay、crash matrix |
| CAP | Phase 0／5 | F | backend matrix、Frame conformance、live capture |
| PER、KP-001〜004 | Phase 5 | G、I | frozen frame corpus、Observation conformance |
| KP-005 | Phase 5／6 | G、H | Screen Graph candidate蓄積のObserve Only scenario |
| AI、UX-008、OPS-003／004 | Phase 6 | H、E | proposal conformance、provider eval、Data Flow |
| OPS distribution群 | Phase 3／8A／8B | A、J、K | diagnostic bundle、clean VM、signed artifact |
| NFR | 各該当Phase | 各owner＋I | gate manifest、benchmark、support matrix |

## 4. LGS同等性の定義

「LGS同等」は曖昧な宣伝文句にせず、LGS 9.04.49の実機挙動を行単位で比較する。Phase 0でinstalled LGSを操作し、画面、設定file、device state、入力挙動を記録したcanonical parity matrixを作る。

初期分類は次のとおり。LGS実機目録で存在しない機能は対象外へ修正する。

| Capability | G13 | G600 | 目標release | 現在 |
|---|---:|---:|---|---|
| persistent/default profile | 対象 | 対象 | R2 | 未確認 |
| app detection profile | 対象 | 対象 | R2 | G600方式未決定 |
| simple key／mouse assignment | 対象 | 対象 | R1 | 強い推定／未確認 |
| chord（同時押し基本形） | 対象 | 対象 | R1 | 未確認 |
| timed multi-key sequence | 対象 | 対象 | R5 | 未確認 |
| repeat while held／toggle | 対象 | 対象 | R5 | 未確認 |
| profile export／import | 対象 | 対象 | R5 | 未確認 |
| G13 G1〜G22 | 対象 | — | R1 | 強い推定 |
| G13 M1／M2／M3／MR | 対象 | — | R1/R5 | firmware依存、未確認 |
| G13 stick／dead zone／diagonal | 対象 | — | R1 | 未確認 |
| G13 RGB／M LED | 対象 | — | R5 | protocolあり、Windows write未確認 |
| G13 LCD framebuffer | 対象 | — | R5 | protocolあり、Windows endpoint未確認 |
| G13 LCD applet相当 | 対象 | — | R5裁定 | 目録未作成 |
| G600通常button | — | 対象 | R1 | live route未確認 |
| G600 G-Shift | — | 対象 | R1/R5 | profile構造確認、live route未確認 |
| G600 3 onboard profile | — | 対象 | R5 | read/write未確認 |
| G600 DPI／report rate | — | 対象 | R5 | protocol確認、write未確認 |
| G600 RGB | — | 対象 | R5 | LampArray列挙確認（Logitechソフトウェア導入環境下。提供元device stack未特定＝LGS/G HUB除去後の存続は未確認）、制御未確認 |
| command／program launch | 対象なら実装 | 対象なら実装 | R5 | LGS目録待ち |
| script機能 | 対象なら裁定 | 対象なら裁定 | R5裁定 | LGS目録待ち |

parity matrix各行には、LGS手順、OpenLogicool手順、入力fixture、期待結果、許容差、Windows build、firmware、driver、LGS状態、証拠artifactを持たせる。

LGS Parity claimは、canonical inventoryでLGS 9.04.49上の存在を確認した全capabilityが、対象support matrixでSupportedになった場合だけ使用できる。Unsupported、Deferred、Out of Scopeが一行でもあれば、owner裁定の有無にかかわらずPartial LGS ReplacementまたはCore LGS Replacementと表示する。ownerは実装scopeを裁定できるが、parity成立条件を縮小できない。

## 5. UX設計

### 5.1 情報architecture

最上位navigationはhardwareではなくapplicationとする。

~~~text
Applications
  └─ Application Workspace
       ├─ Overview            active app、device、capability、問題
       ├─ Actions             意味操作catalogと両device binding
       ├─ Layout              G13とG600の統合配置
       ├─ Automation          goal、Playbook、実行mode
       ├─ Timeline            proposal／dispatch／observation／confirmed
       └─ Diagnostics         入力、capture、AI、device状態

Hardware Maintenance
  ├─ G13 diagnostics／lighting／LCD
  └─ G600 backup／onboard profile／lighting／DPI
~~~

Hardware Maintenanceは保守面であり、通常のゲーム設定flowへ出さない。

### 5.2 Application Workspace

- headerに「編集中のapp」「現在有効なapp」「対象window」「適用revision」「実行mode」を並べる。
- Actions面は意味操作を行にし、同じ行へG13 binding、G600 binding、layer、automation利用箇所を表示する。
- device図からcontrolを選んでも、意味操作一覧から選んでも同じbinding editorを開く。
- 保存はlocal revisionを作る。runtime適用とdevice writeは別結果として表示する。
- G13成功／G600失敗のような部分成功を、統合保存成功と表示しない。
- advanced HID、raw report、provider parameterはDiagnosticsまたはExpert viewへ分離する。

### 5.3 利用者journey

#### Journey A: 初回導入

1. G13/G600を個別検出する。
2. LGS、G HUB、Logitech driver、Dynamic Lightingとの関係を表示する。
3. 共存read-onlyとOpenLogicool ownership移行を分ける。
4. device write前に復元backupを作り、readbackできることを確認する。
5. 片方だけでも利用開始でき、未接続側を明示する。
6. test fieldで一つの入力を確認してからapp profileを有効化する。

#### Journey B: 一つのゲームを設定

1. 実行中一覧、installed一覧、EXE参照からappを選ぶ。
2. Actionを作る。
3. 同じ画面でG13とG600へbindingする。
4. layer、競合、未対応capabilityを確認する。
5. revisionを保存し、runtime適用結果をdevice別に確認する。
6. gameへ戻り、active profile表示とtest inputで確認する。

#### Journey C: AIへ教える

1. 対象window、capture範囲、外部送信、保存期間、費用上限を確認する。
2. Observe OnlyでAIの状態認識と次操作proposalを見る。
3. Teach modeで一手を承認する。
4. runtimeがdispatchし、Perceptionが期待結果を確認する。
5. 成功stepをcandidateとして即時保存する。
6. 翌sessionでreplayし、verifiedへ昇格する。

#### Journey D: 停止・訂正・再開

1. 停止受付後は新しいactionを発行しない。
2. 所有中keyをreleaseし、最後のAttemptを表示する。
3. DispatchArmed以降で結果未確認ならOutcomeUnknownと表示する。
4. 訂正は新Playbook versionを作り、確定済み履歴を残す。
5. 再開時に現在Observationと候補stateを示す。
6. UniqueMatchだけ自動再開できる。曖昧なら利用者が状態指定、再観察、手順修正、終了を選ぶ。

#### Journey E: 障害診断

次を別categoryとして表示する。

- device未接続、driver競合、shared open失敗、write不可
- app identity不一致、target window非foreground、UIPI、不達
- capture black、stale、resize、遮蔽、最小化
- Observation unknown／ambiguous
- AI authentication、timeout、rate limit、予算到達
- journal／disk write失敗、active executor競合

各障害に「最後に成功した地点」「自動で行っていないこと」「次の選択肢」「コピー可能なredacted診断」を付ける。

### 5.4 実行mode

| Mode | AI | 入力 | 知識更新 |
|---|---|---|---|
| Observe Only | 状態とproposalを表示 | なし | observationとScreen Graph candidate |
| Teach | 一手を提案 | stepごとに承認 | 成功後candidate |
| Supervised Run | known low-riskを実行、未知は確認 | 条件付き | candidate／replayed／verified昇格（§6.8） |
| Verified Run | verified pathだけ実行 | ambiguityで停止 | 実行証拠を追加 |

承認はapp、window、Observation、proposal、Playbook version、risk classへ結び付ける。いずれかが変わった承認を再利用しない。

## 6. Architecture契約

### 6.1 二つの実行経路

~~~mermaid
flowchart LR
    G13["G13 Raw Input"] --> DI["Device Input"]
    G600["G600 Input Route"] --> DI
    DI --> MR["Mapping Runtime"]
    MR --> IE["Input Emitter"]
    IE --> APP["Foreground App"]

    CAP["Capture"] --> PER["Perception"]
    PER --> PB["Durable Playbook"]
    AI["AI Planner"] -->|proposal only| PB
    PB --> AC["Semantic Action Catalog"]
    AC --> MR
    PB --> J["Event Journal"]
    UI["Desktop UI"] --> PB
    UI --> MR
~~~

Device Input → Mapping Runtime → Input Emitterはfast pathである。このpathではAI、network、capture、SQLite、UI renderingを待たない。

Capture → Perception → Playbook → Semantic Actionはdurable pathである。遅延、pause、取消、journal、結果確認を扱う。AIはこのpathへproposalを返すだけで、fast pathを直接操作しない。

### 6.2 初期process model

最初は一つのWindows resident processにする。不要なIPC、service、driverを先に作らない。ただしmodule境界は将来process分離できる契約にする。

- UI windowを閉じてもtray hostは継続する。
- fast pathは専用queueとworkerを持ち、AIやcaptureのbackpressureを受けない。
- queue overflowはdropして継続せず、runtimeをfault停止して所有outputをreleaseする。
- hard crash後のkey releaseは単一processだけでは保証不能である。Supported pathでは、対象OS上でoutputが残留しないことを実測するか、host process死亡後に所有outputを期限内にreleaseする最小watchdog processを採用する。
- release注入もUIPIに従う: foregroundが自ILより高いwindowの間、SendInputによるkey-upは届かない。期限内release保証（NFR-008・§14.3）はforeground IL≦自ILの条件付きとし、elevated foreground中の残留riskはSupported matrixの行条件として明示する。watchdogの昇格実行またはuiAccess署名の採否はPhase 2のwatchdog decisionに含める。
- 二重起動を防ぎ、既存instanceへUI activationを渡す。
- 一つのRunを進められるexecutorは一つだけとする。

### 6.3 Module構成

~~~text
src/
  OpenLogicool.Contracts/
  OpenLogicool.Domain/
  OpenLogicool.Devices.G13/
  OpenLogicool.Devices.G600/
  OpenLogicool.Input/
  OpenLogicool.Profiles/
  OpenLogicool.Playbooks/
  OpenLogicool.Persistence/
  OpenLogicool.Capture/
  OpenLogicool.Perception/
  OpenLogicool.AI/
  OpenLogicool.Desktop/
  OpenLogicool.Host/

tools/
  OpenLogicool.DeviceProbe/
  OpenLogicool.CaptureProbe/
  OpenLogicool.GameLab/
  OpenLogicool.SessionRecorder/

tests/
  Contract/
  Domain/
  Device/
  Playbook/
  Capture/
  Perception/
  AI/
  UI/
  Packaging/

test-assets/
  device-reports/
  frames/
  scenarios/
  acceptance/
~~~

依存規則:

- Contractsはwire type、ID、enum、portだけを持ち、具体SDKを参照しない。
- DomainはSemantic Action、Profile、Playbookのpure modelを持つ。
- DesktopはHID、SendInput、capture API、AI SDK、SQLiteを直接呼ばない。
- AIは変更可能なPlaybook controllerを参照しない。immutable PlannerContextを受け、NextActionProposalだけを返す。
- Playbooksだけがproposalの承認、dispatch依頼、結果確認、journal、version確定を統括する。
- DevicesはUI、AI、Perception、SQLiteを参照しない。
- Perceptionは入力を実行しない。
- Hostだけが具体implementationを配線する。
- Persistence portの意味ownerは利用domain、SQLite implementationとmigration runnerのownerはK（Persistence／Release）とする（§7.2と同一。Lane J「Platform／Integration」はshared primitivesと統合を持ち、SQLite実装を持たない）。

### 6.4 共有contract baseline

| Contract | 必須field／意味 | Semantic owner |
|---|---|---|
| ApplicationIdentity | full path、package、process generation、window matcher | Profile |
| DeviceInstance | VID/PID、path、container、generation、capability | Device |
| PhysicalInput | instance、control、edge、timestamp、report sequence | Device |
| SemanticAction | stable ID、name、risk class、parameter schema | Domain |
| BindingRevision | app、device、layer、mapping revision、outputs | Input/Profile |
| CapturedFrame | source、backend、sequence、time、DPI、format、transform、freshness | Capture |
| ObservationResult | Known／Ambiguous／Unknown／Unavailable、evidence、confidence、version | Perception |
| PlannerContext | goal、state、allowed action、history summary、budget | Playbook |
| NextActionProposal | action IDまたは許可target、precondition、expected outcome、stop condition | AI |
| PlaybookVersion | immutable ID、parent、nodes、edges、change reason | Playbook |
| RunEvent | schema、run sequence、attempt、causation、actor、payload | Playbook |
| KnowledgePackManifest | game/build、locale、schema、provenance、verification | Perception/Knowledge |
| ScreenGraph | node（state ID）、edge（visual target参照・遷移・帰属操作）、node/edgeごとのcandidate／verified状態、immutable version、環境scope | Perception/Knowledge |

全contractにschema version、stable ID、wall-clockと必要なmonotonic time、取消時の意味を定義する。enum追加、nullability変更、順序変更、confidence意味変更は、source互換でもsemantic breakingになり得る。

### 6.5 Input ownership

physical down時にPressOwnershipを作る。

- key: device instance + control + press generation
- value: profile revision、layer、mapping revision、実際に送ったoutput down集合
- profile／layer変更は新規downから有効
- physical upはPressOwnershipを参照し、現在mappingを再評価しない
- device disconnect、pause、handled shutdownは新規downを止めてから所有outputをrelease
- SendInputの部分成功はfaultであり、同じsequenceを自動再送しない
- indefinite holdは許可せず、有限leaseまたはphysical releaseを必要とする

Playbookによる合成入力（automation dispatch）も同じ所有modelに従う。

- Semantic Actionから送信outputへの解決は決定的とする: dispatch時にAttemptが単一の出力経路（Workspaceが定義するSemantic ActionごとのprimaryのInput Macroまたはbinding出力）を固定し、複数device bindingを同時発火しない。解決結果（実際のoutput集合）はAttemptへ記録する。
- 合成down時もPressOwnershipを作る（key: actor=automation + Attempt ID + output、value: 送信済みoutput down集合）。停止・crash時のrelease対象は物理・合成の全所有outputであり、watchdog（§6.2）の責務にも合成outputを含める。
- 取消不能な合成入力を作らない。全合成macroは停止境界（NFR-008の250 ms release）に従う。この停止境界は自動化が入力を送る最初のrelease（R3）から要件であり、R5のMAP-007（repeat／toggle等の完全な状態管理）を待たない。
- 物理入力とRunの仲裁: Supervised／Verified Run中に物理入力が同じSemantic Actionへ到達した場合、runtimeはそれをmanual interventionとして扱いexecutorを停止する（PB-013）。Run中に物理binding発火を自動でRun進行へ合流させない。詳細の仲裁方式（マスクか停止か）はPhase 4で、この所有modelの上に決定する。

### 6.6 G600 architecture gate

| Route | App profile数 | 元入力 | Driver | 判定 |
|---|---:|---|---|---|
| 3 onboard slot直接利用 | 最大3 | 原則重複なし | 不要 | read/write未確認 |
| intermediate usage → user-mode変換 | 無制限候補 | usage自体が届く可能性 | 不要候補 | live route未確認 |
| physical suppression → SendInput | 無制限 | 抑止可能 | filter必要 | gate後候補 |
| physical suppression → virtual HID | 無制限 | 抑止可能 | filter＋VHF | parity分岐 |
| foregroundごとの154-byte write | 見かけ上無制限 | — | 不要 | 採用禁止 |

仮想HIDは元入力を消さない。suppressionとvirtual outputは別capabilityとして評価する。

### 6.7 Durable Attempt state machine

~~~text
Proposed
  → Authorized
  → Prepared
  → DispatchArmed
  → DispatchReported
  → Observing
  → Confirmed | Rejected | OutcomeUnknown

Proposed | Authorized | Prepared
  → Cancelled

DispatchArmed
  → Disarmed | OutcomeUnknown

OutcomeUnknown
  → Reconciling
  → Confirmed | Rejected | NeedsUserDecision | Abandoned

NeedsUserDecision
  → UserResolvedSuccess | UserResolvedFailure | Abandoned
~~~

- Cancelled: dispatch前の中止（利用者abandon、前提不成立、handled shutdown）。外部効果ゼロで閉じた終端。PB-007のabandonは、dispatch前AttemptについてはCancelledへ写像する。
- Disarmed: DispatchArmed後、外部入力APIを一度も呼んでいないことをruntime自身が保証できる場合だけの中止終端（PER-005停止、対象window喪失等）。保証できない場合はOutcomeUnknownへ倒す。
- UserResolvedSuccess／UserResolvedFailure: 利用者判断による解決終端。Confirmed／Rejectedとは別状態であり、Observationを持たないため学習昇格（candidate化・verified昇格）の根拠にしない。判断はUserDecisionイベントとして記録する。

契約:

1. DispatchArmedを外部入力呼出前にcommitする。
2. DispatchArmed以降にprocessが止まった場合、実際に未送信でもOutcomeUnknownとして再開する。crash境界がPrepared以前（外部入力呼出前が確定）の場合、再開時はCancelledへ倒す。
3. Input API戻り値は、game受理または期待結果の証拠ではない。
4. Confirmedには同じAttemptを参照するcommit済みObservationが必要。
5. 未解決Attemptがある間、次のdispatchを自動生成しない。終端（Confirmed／Rejected／Cancelled／Disarmed／UserResolved*／Abandoned）だけが解決である。
6. SQLite transactionが保証するのはevent、projection、Playbook version等のDB内整合性だけである。
7. Windows入力とgame stateはDB transactionへ参加せず、exactly-onceを保証しない。
8. 承認済みAttemptのObservation・前提が変わった場合は再利用せずCancelledへ倒し、新Attemptを作る。

RunEventの必須field:

- event ID、schema version
- run ID、run sequence
- Playbook ID、version ID、node／transition ID
- command ID、Attempt ID
- causation ID、correlation ID
- executor epoch、actor type
- occurred time、persisted time
- Observation ID（Observing以降のeventだけ必須。それ以前はnull。schema初版からのnull許容であり、§6.4のnullability変更禁止の対象外）
- typed payload

Attempt IDは相関用であり、Windowsやgameのidempotency keyではない。

契約4のObservation→Attempt参照は、PlaybookがcommitするRunEvent（Attempt IDとObservation IDを併記）だけで成立させる。ObservationResult自体はAttemptを知らず、PerceptionはAttempt相関を所有しない（§6.3の依存規則と整合）。どのObservationをどのAttemptへ束縛するかの意味ownerはE（Playbook）である。

### 6.8 Version／resume

- Playbook versionはimmutableでparent versionとchange reasonを持つ。
- Runは開始時versionへpinする。
- 編集は新versionを作り、実行済みeventを変更しない。
- 新versionへの切替はPausedかつ現在state再照合後だけ許可する。
- progress継承はstable node IDと前後conditionが互換なnodeだけにする。
- skip、manual completion、human correction、AI successを別eventにする。
- manual intervention開始時にexecutorを止め、終了後は必ず新Observationから照合する。
- checkpointはlast event sequence付きのcacheであり、journalから再生成する。
- active executorはmonotonic executor epochを取得し、stale epochからのappendを拒否する。

verification statusはenvironment scopeを持つ。GameLabでのVerifiedはGameLab内だけで有効であり、frozen acceptance datasetはrecognizer、planner、runtimeの評価に使っても、実game stepのVerified昇格には使わない（昇格の証拠はlive sessionだけ。§10.3）。

昇格の3段（PB-012）は次で定義する。

- candidate → replayed: 独立live sessionで再現したが、環境scopeの完全一致が未確認、または一致しない環境での再現。
- replayed → verified: 同一環境scopeの独立live sessionで再現した場合。candidateから直接verifiedになるのは、最初の独立再現が同一環境scopeだった場合である。
- 環境scopeは game/build、locale、UI scale、resolution、display mode（windowed／borderless／fullscreen）、DPI、HDR、capture backend、input route、Screen Graph version、recognizer version の同一性で判定する。

Screen Graphのnode／edgeも同じ2段で昇格する: Observe Only等で蓄積したcandidateは、別sessionの実観測で同一node（state ID）がUniqueMatch再現し、edgeは同一遷移が再観測された場合だけverifiedへ昇格する。PB-009の再開照合とVerified Runのstate根拠に使えるのはverified node／edgeだけであり、candidateは提案・表示・Teachの参考にだけ使う（KP-005）。Verified StepはScreen Graph versionへpinし、参照先node／edgeがcandidateへ降格または削除された場合はverified statusを失いreplayedへ戻る。

State matchは次を返す。

- UniqueMatch
- NoMatch
- AmbiguousMatch
- InsufficientEvidence
- StaleObservation

自動再開できるのは、app、target window、Playbook version、観測鮮度、安定窓、state predicateを満たすUniqueMatchだけである。

### 6.9 Capture／Perception

第一候補はWindows Graphics Captureでwindow単位captureを行う。Desktop Duplicationと可視desktop領域captureは別backendとしてprobeする。選択backend、利用者のcapture選択、yellow border等のOS表示、失敗条件をUIへ反映する。

Frame contractは座標を次の順で変換する。

1. source pixel coordinates
2. content coordinates
3. DPI／rotation／letterboxを反映したnormalized coordinates
4. current target window client coordinates
5. input coordinates

resize、display移動、DPI変更、HDR／format変更、backend変更でtransform revisionを更新し、古いlocatorを無効化する。

ObservationResultのconfidenceはrecognizerごとにcalibration datasetで定義する。複数候補の差が小さい、frameが古い、証拠領域が欠ける場合はKnownへ丸めない。成功条件は前frame、dispatch、後続複数frameの安定窓から判定する。

### 6.10 AI proposal境界

verified runのAI出力は既存Semantic Action ID、expected outcome、stop conditionだけを含む。

未知ゲームのTeach modeでは、AIが任意座標やkey codeを生成するのではなく、Perceptionが現在frameから列挙したvisual targetを選ぶ。Runtimeはtargetが同じFrame／transform revisionへ属し、対象window内にあり、許可primitiveであることを検証する。成功後に利用者が命名したSemantic Actionとlocator candidateへ保存する。

AIは次を変更できない。

- action catalog
- risk class
- execution mode
- provider／model
- cloud送信範囲
- daily cost cap
- game policy
- Playbook verified status
- Screen Graphのnode／edgeのcandidate／verified状態

provider error、timeout、rate limit、schema error、budget到達は明示停止する。別providerへfallbackしない。

### 6.11 Knowledge Pack

初期formatはdataのみとし、実行plugin systemを作らない。

~~~text
manifest
application-identities
supported-environments
semantic-actions
states
recognizers
visual-targets
screen-graph
playbooks

`states`はScreen Graphのnode台帳であり、Screen Graphのnode IDは`states`のstable state IDと同一とする。Playbookの前提・ObservationのKnown候補・visual targetの帰属・Screen Graphのnodeは、すべてこの単一のstate IDを参照し、同じ画面状態を別IDで二重定義しない。`screen-graph`はnode間のedge（遷移・帰属visual target・candidate/verified状態）を持つ。
fixtures
policy-record
provenance-and-license
migrations
~~~

stateとrecognizerはgame build、locale、resolution、UI scale、capture conditionへ紐付ける。pack import直後はUntrustedであり、code、script、prompt override、provider設定、secretを受理しない。

### 6.12 Data flow／secret

Phase 4前にData Flow Contractを作る。対象はframe、crop、OCR text、window title、process path、prompt／response、journal、device ID、crash dump、diagnostic bundleである。各dataに生成元、保存先、送信先、retention、削除経路を持たせる。

初期既定:

- full-screen frameの永続保存: OFF
- cloud送信: app単位で明示同意するまでOFF
- confirmed stepのevidence crop保存: 利用者がTeach sessionで選択
- engineering logへのOCR／prompt本文: OFF
- crash raw dump: OFF
- telemetry: OFF
- secret: Windows Credential ManagerまたはCurrentUser scope保護。export対象外
- deletion: SQLite、image、cache、temp、upload queue、bundle、backupの対象をpreviewする

## 7. 並行開発model

### 7.1 Workstream

| Lane | 所有scope | 必須成果物 | 独立開発手段 | 統合gate |
|---|---|---|---|---|
| A Product／UX | UX specification、Desktop | fake device、fake timeline、UI test | scenario fixture | core journeyをfakeとreal contractで再利用 |
| B G13 | Devices.G13、probe | descriptor／report fixture、adapter、conformance | recorded reports | 全edge、hotplug、stick実機受入 |
| C G600 | Devices.G600、probe | full backup、live route、adapter | recorded reports | route裁定、readback、重複判定 |
| D Input／Profile | Domain mapping、Input、Profiles | generation model、resolver、macro runtime | fake physical input | latency、wrong release 0 |
| E Playbook | Playbooks、event semantics | state machine、journal replay、fault results | GameLab oracle、fake Perception | crash invariant全通過 |
| F Capture | Capture、CaptureProbe | capability matrix、Frame fixture | recorded／live frame | frame conformance |
| G Perception／Knowledge | Perception、pack schema | annotation、Observation、unknown判定 | frozen frame corpus | calibration／acceptance gate |
| H AI | AI、provider eval | proposal schema、eval report | recorded PlannerContext | direct input不可能、frozen eval |
| I GameLab／Quality | GameLab、scenario、acceptance assets | deterministic app、ground truth、fault hooks | virtual clock／seed | fixture v1とscenario conformance |
| J Platform／Integration | shared primitives、contract baseline index、Host、共通build | baseline index、composition、dependency結果 | fake modules | cross-contract／Host integration gate |
| K Persistence／Release | Persistence実装、migration runner、packaging | DB migration、install／update／artifact | repository fake、clean VM | migration、rollback、package gate |

staffが少ない場合は複数Laneを一人が持てるが、ownershipとgateは統合しない。G13とG600、CaptureとPerception、AIとPlaybookを別scopeにすることで独立作業を可能にする。

### 7.2 Contract ownership

Contractsはsemantic ownerごとの非交差subtreeへ分割する。

~~~text
OpenLogicool.Contracts/
  Shared/          J
  Domain/          D    （SemanticAction等のpure model契約）
  Devices/Shared/  J    （DeviceInstance・PhysicalInput等のG13/G600横断契約）
  Devices/G13/     B
  Devices/G600/    C
  Profiles/        D
  Playbooks/       E
  Capture/         F
  Perception/      G    （ObservationResult・KnowledgePack・ScreenGraphを含む）
  AI/              H
~~~

- 各semantic ownerが自subtreeの物理file、意味、provider／consumer contract testを所有する。
- Jはshared primitives、baseline index、共通build、package compositionを所有し、個別contract変更の直列統合者にならない。
- Eventとtransaction semanticsはE、SQLite implementationとmigration runnerはKが所有する。
- consumerは自scopeのadapterを所有し、中央担当が他Lane実装を代行しない。
- AIとPlaybookの循環依存を禁止する。AIはproposal portだけを実装する。
- 複数contractを同時変更する作業とShared変更だけintegration slotを必要とする。

### 7.3 Definition of Ready

Taskを並行開始できるのは次が揃った場合だけである。

- semantic owner、read scope、write scope
- contract revision、fixture ID
- 前提gate
- focused test commandと期待結果
- 成果物path
- 実機／実ゲームsession予約の要否
- central file変更がある場合のintegration slot
- 未確認事項と停止条件

### 7.4 Definition of Done

- 所有scopeだけを変更している。
- focused testとprovider／consumer contract testがgreen。
- fakeとreal adapterが同じconformance suiteを通る。
- Supported／Experimental／Unsupported／Unverifiedを更新した。
- 実験環境、contract revision、fixture ID、結果、未検証範囲をgate manifestへ残した。
- 利用者向け失敗表示とdiagnostic categoryがある。
- Lane Doneとproduct integrated Doneを混同していない。
- 最終E2Eは関連focused testが全て通った後だけ行った。

### 7.5 Contract change protocol

変更要求に次を含める。

- semantic owner、対象revision、変更理由
- 影響consumer
- source互換とsemantic互換の分類
- fixture／acceptance差分
- SQLite、journal、active Run、Knowledge Packのmigration
- rollback
- consumer sign-off

追加だけの変更でも挙動互換を実証できなければbreakingとする。semantic ownerは自subtreeとcontract testを変更し、consumer変更は各consumer ownerが行う。複数subtreeまたはSharedを横断する変更だけJのintegration slotで調整する。write scopeが交差する作業は並行dispatchしない。

### 7.6 Fixture Gate

GameLab v1とscenario-v1を、Playbook、Perception、AI、UIの製品実装より先に提供する。

scenario manifest:

- scenario ID、schema、seed、virtual clock
- initial state
- allowed action
- expected frame／Observation
- expected event sequence
- irreversible effect count
- reset method
- viewport、DPI、frame time
- popup、delay、unknown、manual intervention、crash point

同じmanifestをfake、recorded frame、live GameLabのconformance testで使う。

device fixtureは別系列にし、descriptor、report ID、raw bytes、driver／LGS状態、Windows build、firmware、取得日時、端末識別情報のredactionを持つ。

### 7.7 Integration waveとcritical path

~~~mermaid
flowchart TD
    W0["Wave 0: feasibility・LGS inventory"] --> W1["Wave 1: contract draft・GameLab v1・fixture v1"]
    W1 --> W2A["Wave 2A: G13/G600 → Input → Notepad"]
    W1 --> W2B["Wave 2B: GameLab → Playbook → Journal → UI"]
    W1 --> W2C["Wave 2C: Capture → Perception corpus"]
    W2A --> W3["Wave 3: App-first Input Studio"]
    W2B --> W4["Wave 4: AIなしautomation closed loop"]
    W2C --> W4
    W4 --> W5["Wave 5: AIをGameLabへ接続"]
    W3 --> W7I["Wave 7A: Input Studio distribution／parity"]
    W3 --> W6["Wave 6: 実game observe／approve／verified run"]
    W5 --> W6
    W6 --> W7G["Wave 7B: Game Operator distribution"]
~~~

big-bangでHostへ集めない。各Waveは一つのvertical sliceとして受け入れる。

- Wave 2Aは実device入力をNotepad test targetまで通す。
- Wave 2Bはcaptureをfakeにしてdurabilityを通す。
- Wave 2Cはinputを行わずlive／recorded captureを同じObservationへ変換する。
- Wave 4で初めてAIなしの画面closed loopを通す。
- Wave 5のAIはGameLab以外へ入力しない。
- Wave 6はObserve Only、Teach、Supervised、Verifiedの順に解禁する。
- Wave 7AはPlaybook、Capture、Perception、AI、実game pilotを待たず、Input Studio Public GateとShared Distribution Gateの2つだけで公開できる（Game Operator系のgateを待たない）。
- Wave 7BはShared Distribution GateとGame Operator Public Gateを通す。

## 8. Phase計画

期間は、1人のownerがfocused taskを進める場合の初期幅であり、約束ではない。Phase 0後に実測で再見積りする。並行Laneがあるため期間を単純合計しない。

### Phase 0: Feasibility Admission（1〜3週）

目的: 製品契約を仮定で固定せず、最大分岐をread-only中心の実測で閉じる。

実施:

- LGS 9.04.49のcanonical parity inventory。
- G13 Raw Input全control、stick、hotplug、firmware差の記録。
- G600 Feature Report read-onlyと完全backup。
- G600 live input route、通常／G-Shift側全control、legacy重複の記録。
- LGS/G HUB/driver/Dynamic Lighting inventoryとMigration Safety Gateの手順定義（apply・restore testの実証はG0-Device-Wで行い、Phase 0ではwriteしない）。
- WGC／Desktop Duplication／可視desktopのcapture probe。
- ObservationResult、Proposal、Knowledge Packのdraft。
- AI provider候補のdata policy、画像対応、構造化出力、費用／遅延評価設計。
- Windows support matrixとreference machineの確定。
- WPF/.NET 10のWindows-native build、run、test最小確認。

Exit Gate G0-Device-RO（Phase 0内・read-onlyで判定できる範囲だけ）:

- G13主要入力経路が成立。
- G600 profile完全readが成立。
- G600 live input routeの観測記録が完成（現状firmware割当で届くusage、通常／G-Shift側の識別可否、legacy重複の分類。writeを伴う成立判定は含まない）。
- Migration Safety Gateの手順（backup・readback・restore）が定義済み（実証はG0-Device-Wで行う）。
- 方式A／B／Cのうちread-onlyで棄却できるものを棄却し、残候補と必要なwrite実験を列挙済み。

Exit Gate G0-Device-W（Phase 2入口・Migration Safety Gate実証後の最小write実験）:

- Migration Safety Gateをapply・readback・restore testまで実機で実証済み（EXP-MIG-01）。
- EXP-G600-03（F0 active slot切替）で方式Aの成立を判定済み。
- 方式Bが残候補の場合、中間usageのonboard書込みとWindows到達を実測済み（EXP-G600-02の write 拡張）。
- 方式A／B／Cのいずれかへ最終決定し、未知のままUI契約を固定していない。

Phase 0受入の「device writeを行っていない」は維持する。G0-Device-WはPhase 0の外にあり、Phase 2の他実装より先に単独で実施する。

Exit Gate G0-Automation:

- Frame、Observation、Proposalのdraftが実frameとGameLab prototypeのfixture案で表現可能（GameLab v1の実装受入はPhase 1 Exit。Phase 0はprototypeと仕様で判定する）。
- cloud送信前のData Flow Contract項目が決定。
- NIKKE実測は探索証拠として再記録し、製品受入と混同していない。

No-Go:

- 失敗時に別の方式を仮定して製品module実装へ進まない。
- 次のfocused experimentだけをReadyにする。
- driverが必要と判明した場合、user-mode releaseとdriver releaseを別scopeへ分ける。

### Phase 1: Contract／GameLab Foundation（2〜4週）

目的: 各Laneが同じfakeとfixtureを使って独立開発できる最小baselineを作る。

実施:

- solution／module骨格。
- Contracts revision 0.1。
- Semantic Action、Profile generation、Frame、Observation、Proposal、RunEventのpure model。
- GameLab v1とscenario-v1。
- fake Device、Capture、Perception、AI。
- contract conformance suite。
- gate manifest format。
- dependency direction test。
- initial SQLite migration runner。domain table実装は各feature gate後に追加する。

Exit:

- GameLabがdaily reset、irreversible claim、delay、popup、unknown state、manual interventionをseed付きで再現。
- 全Laneが自moduleとfakeだけでbuild／focused testできる。
- contract意味owner、subtree owner、fixture revisionが一意。
- contract subtreeを含むLane scopeが非交差で、Shared／cross-contract変更だけがintegration slotを使う。

### Phase 2: Core Input Replacement（4〜8週）

目的: LGSを終了しても日常入力を行える最小Input Studioを作る。

実施:

- G0-Device-W（他実装より先に単独実施）: EXP-MIG-01のapply・restore実証、EXP-G600-03、方式B残存時のwrite拡張実測、route最終決定。
- G13 adapter、G600で選択したroute、Mapping Runtime、Input Emitter。
- press generation、layer、profile切替、finite macro。
- foreground app identity。
- read-only onboardingとdevice capability。
- Notepad、通常app、管理者app、対象gameを分けたinput acceptance。
- hotplug、sleep、profile切替、key保持、queue overflow。
- user-mode routeが不十分な場合のdriver decision record。

Exit:

- G13/G600のSupported controlを欠落なく表示・変換。
- 1,000,000 report replay、1,000 generation race、hotplug suiteが通る。
- LGS virtual keyboard／busに依存しないSupported pathが明示される。
- G600制約を隠さず、3 slotまたはdriver等の実際の方式をUIに反映する。
- hard crashでoutputが残留しないことをSupported OSで実証するか、watchdogを実装・受入している。どちらも満たせないheld-output capabilityはUnsupportedとする。

### Phase 3: App-first Unified UX（4〜8週、Phase 2と並行）

目的: hardware-firstではなくapp-firstの設定体験を完成する。

実施:

- Application list／Workspace。
- Action-centric binding editor。
- G13/G600統合layout。
- editing／saved／runtime／device state。
- conflict、unknown capability、partial apply。
- onboarding、test field、diagnostics。
- keyboard navigation、high contrast、display scaling。
- revision、undo、export。

Exit:

- 利用者がappを一度選び、device別画面を往復せず両機を設定。
- OpenLogicoolへAlt+Tabしてもediting targetを失わない。
- launcher、同名EXE、window消失を誤profileで継続しない。
- G13だけ／G600だけでも完結する。
- UI test scenarioがfakeとreal contractで同じ結果になる。

### Phase 4: Durable Automation Lab（6〜10週）

目的: AIなしで停止・修正・再開の正しさをGameLab上で完成する。

実施:

- Playbook graph、immutable version、Run、Attempt state machine。
- append-only Event Journal、projection、executor epoch。
- pause、step、skip、abandon、manual intervention、version switch。
- DispatchArmed境界とfault injection。
- fake ObservationによるUnique／Ambiguous／Unknown／Unavailable。
- session recorder／replayer。
- user timelineとresume UX。

Exit:

- 全fault pointで未解決DispatchArmedから次dispatchを自動生成しない。
- ConfirmedにObservationが必ず存在。
- journal replayとprojectionが一致。
- active Runのversionがcrashやeditで勝手に変わらない。
- manual intervention後は再観察なしに進まない。
- このPhaseの「現在state」はGameLab oracle／fake Observationに限る。実画面resume claimはまだ使わない。

### Phase 5: Capture／Perception（6〜12週、Phase 4と並行）

目的: 実画面を契約済みObservationへ変換する。

実施:

- WGC first backendとcapability probe。
- Desktop Duplication／visible regionの明示選択。
- frame transform、resize、DPI、HDR、multi-monitor。
- OCR、anchor、state classifier、visual target。
- Knowledge Pack schema。
- development／calibration／acceptance corpus分離。
- capture／recognition failure UX。
- NIKKE等の探索frameを再現可能なexperiment artifactへする。

Exit:

- recorded／live frameが同じFrame／Observation conformanceを満たす。
- Known誤判定、Unknown棄却、success false-positiveを事前固定metricで評価。
- backend change、resize、stale frameで入力を止める。
- 一つの実game成功はpilot成立だけと表示し、一般game対応claimにしない。
- Phase 4との統合で、実画面からUniqueMatchした場合だけresumeできる。

### Phase 6: AI Teach／Learn（8〜16週）

目的: 利用者のgoalから未知stepを提案し、candidate Playbookを逐次育てる。

実施:

- provider benchmarkと選定。
- PlannerContext／Proposal schema。
- Observe Only、Teach、Supervised。
- target selectionとexpected outcome。
- candidate／replayed／verified昇格。
- correction、cost cap、timeout、cancel。
- prompt／model／parameter／dataset version記録。
- image crop／cloud consent。
- frozen GameLab AI eval。

Exit:

- AIがdirect input／DB／device APIへ到達できないことをdependency testで確認。
- schema外、catalog外、state不一致、risk不一致proposalをdispatch前に拒否。
- 初見GameLab scenarioを途中保存し、別sessionで既知部分をreplay、未知だけ追記。
- GameLabでのverified statusが実gameへ継承されない。
- acceptance datasetをprompt調整へ使っていない。
- provider停止時もInput Studioとverified deterministic Playbookが利用可能。

### Phase 7: Daily Mission Pilot（最低2 reset cycle＋4〜8週）

目的: 同日やり直せないdaily進行で逐次学習と翌日再現を実証する。

順序:

1. GameLab daily resetで複数日を高速検証。
2. 実game Observe Only。
3. 利用者の実操作とAI proposalを比較するshadow run。
4. Teach modeの一手承認。
5. verified部分だけSupervised／Verified Run。
6. 日替わり未知branchは停止して追記。

実game前gate:

- Game Policy Recordがobserve／assist／autoをmode別に許可。
- target game/build、capture、input、account環境がsupport matrixにある。
- recovery、diagnostic、cost、data consentが有効。
- high-impact action permissionが明示される。
- ownerが実game sessionを一つだけ所有し、他Laneはrecordingを使う。

Exit:

- 初日の成功をverifiedとしない。
- 翌日相当の別sessionでknown pathを再現。
- 途中停止、manual intervention、Alt+Tab、capture loss、OutcomeUnknownから復帰。
- 未知branchを既存verified pathを壊さず追加。
- 規約上許可されないmodeは技術的に可能でも無効。

### Phase 8A: Input Studio Parity／Distribution（8〜16週、Phase 3後に独立開始）

目的: AI／capture／実game pilotを待たず、実測成立したInput Studioを常用可能なsigned productへする。

実施:

- remaining input parity: G13 light／LCD、G600 RGB／DPI／report rate／onboard write、advanced macro。
- LGS migration dry-run／import／manual reconstruction／rollback。
- package identity、installer、autostart、update。
- clean install、N-1→N、rollback、repair、uninstall。
- Authenticode署名、timestamp、SBOM、Third-Party Notices。
- public name、publisher identity、trademark、non-affiliation。
- Input Studio support matrixとdiagnostic bundle。
- optional driverが必要なら別artifact、署名、install、uninstall、recovery。

Exit:

- Input Studio Public GateとShared Distribution Gateが通る。
- LGS環境とG600 stateを元へ戻せる。
- unsupported conditionをUIとrelease noteへ表示。
- canonical inventory全行がSupportedの場合だけLGS Parityを名乗る。一行でも不足すればCore／Partial LGS Replacementとする。

### Phase 8B: Game Operator Distribution（Phase 7後）

目的: Durable AutomationとAI機能を、Input Studio本体の公開可否から独立して配布可能にする。

実施:

- Playbook／journal／Knowledge Packのschema updateとrollback contract。
- Game Operator support matrix、Data Flow、provider、Game Policyの公開情報。
- active Run中のupdate抑止とresume compatibility。
- Observe Only、Teach、Supervised、Verifiedのcapability別release設定。

Exit:

- Shared Distribution GateとGame Operator Public Gateが通る。
- 実game用Verified Stepが独立live session証拠を持つ。
- output ownershipをreconcileするまで再起動後のdispatchを禁止する。
- Input Studioの既存機能と設定をAI／network障害で損なわない。

### Post-8B capability campaign

- G13／G600の既存fast pathを共通の物理USB HID出力へ接続するSerial HID bridgeは、Phase番号を追加せず独立campaignとして扱う。設計、非目標、Task、実機受入の正本は[Serial HID Output campaign](serial-hid-output-campaign-plan.md)。実行状態はLattice storeだけに置く。

## 9. Blockerを潰すfocused experiment

| ID | 実験 | 成功条件 | 失敗時の分岐 |
|---|---|---|---|
| EXP-LGS-01 | LGS 9.04.49 parity inventory | G13/G600全画面と設定挙動を行単位で記録。LampArray等の列挙deviceは提供元device stack（firmware由来かLogitechソフトウェア由来か）を特定する | parity claimを保留 |
| EXP-G13-01 | Raw Input全TLC | G1〜G22、stick、取得可能な補助key、edge、instanceを記録 | firmware差とprofile状態を追加調査 |
| EXP-G13-02 | stress／generation | 5-key同時、layer保持、抜線、sleepでedge整合 | mapping contractを固定しない |
| EXP-G13-03 | RGB／M LED／LCD output | HidUsbを維持し、各機能を独立変更して入力継続 | WinUSB差替えは別decision |
| EXP-G600-01 | Feature Report read-only | F0、F3〜F5を二度読みし完全backup | shared-open競合を特定 |
| EXP-G600-02 | live input route | 通常／G-Shift全control、down/up、legacy重複を分類 | onboard限定またはdriver分岐 |
| EXP-G600-03 | active slot change | F0だけを変更、readback、restore | write capabilityを無効 |
| EXP-G600-04 | 154-byte profile write | byte diff、readback、reconnect、power cycle、restoreを3回 | 追加writeを停止 |
| EXP-MIG-01 | LGS safety | host profileとdevice backupをreadbackし、復帰手順を実証 | ownership移行とwriteを禁止 |
| EXP-IN-01 | SendInput acceptance | Notepad、standard、elevated、target gameを個別分類。判定はAPI戻り値でなく受信側の観測で行う（SendInputはUIPI遮断を戻り値・GetLastErrorで示さないため、standard／elevated各権限で動くtest targetの受信ログと突合する） | unsupported conditionを表示 |
| EXP-IN-02 | foreground generation | Alt+Tab、launcher、UWP、key保持でwrong release 0 | resolver contractを修正 |
| EXP-IN-03 | hard crash key state | Supported OSでoutput残留なしを実証、またはwatchdogが期限内に全release | 未解決ならheld-outputとGame Operator入力を無効 |
| EXP-CAP-01 | NIKKE WGC window | live sequence、hash、resize、遮蔽、最小化、errorを記録 | visible-only等の条件を明示 |
| EXP-CAP-02 | backend matrix | WGC／Duplication／visibleを同じFrame contractへ変換 | backendごとにUnsupported条件 |
| EXP-PB-01 | crash matrix | 全boundaryでunknown再送0、journal再生一致 | state machineを修正 |
| EXP-AI-01 | provider benchmark | frozen corpusで精度、unknown、latency、cost、cancelを比較 | provider未選定を維持 |
| EXP-DATA-01 | privacy path | captureから保存／送信／削除までdata inventory完成 | cloud／recordingを無効 |
| EXP-DIST-01 | packaging identity | tray、autostart、WGC、updateをclean VMで確認。HID・LampArray行はUSB passthrough対応hypervisor（VMware等。Hyper-Vは汎用passthrough非対応）または実機clean環境で確認し、手段を記録する。Phase 8A実施へ割り当てる | MSIX／Sparse／MSIを再裁定 |

G600の「LED 1項目変更」も転送上は154-byte profile全体のwriteになり得る。論理field一つと物理write範囲を混同しない。foreground切替を契機に永続profileを書き換えない。

## 10. 検証戦略

### 10.1 Test層

1. Pure model test: profile、layer、state graph、version、risk、match。
2. Property／invariant test: down/up、generation、pause、manual intervention、event sequence。
3. Golden vector: HID descriptor、report parser、Feature Report encode/decode。
4. Contract conformance: fake、recorded、live adapterを同じsuiteで検証。
5. Fault injection: DB commit、dispatch、capture、Observation、version switch、update。
6. Frame corpus: state別precision／recall、unknown、stale、evidence。
7. AI eval: model／prompt／parameter固定、複数反復、cost／latency／cancel。
8. Windows integration: foreground、UIPI、DPI、HDR、hotplug、sleep。
9. Clean VM: install、update、rollback、repair、uninstall、migration。
10. 実game E2E: 上記が全て通った後に一度だけ最終確認。

通し試験を個別featureの原因調査に使わない。失敗したmoduleを最小再現へ切り分け、focused testで修正確認後、最後に通し試験を再実行する。

### 10.2 Crash matrix

最低限、次の境界へfaultを注入する。

1. Prepared commit前
2. Prepared commit後、DispatchArmed前
3. DispatchArmed commit後、input call前
4. key down後、key up前
5. 外部効果後、input API return前
6. API return後、DispatchReported commit前
7. capture後、Observation commit前
8. Observation commit後、Confirmed transaction前
9. new Playbook version作成後、Run切替前
10. manual intervention中、reconcile前

合格不変条件:

- 未解決DispatchArmed中に次dispatchを出さない。
- OutcomeUnknownをConfirmedとして表示・学習しない。
- ConfirmedにObservationが存在する。
- journal replay projectionが保存projectionと一致する。
- active versionが勝手に変わらない。
- duplicate UI commandはAttempt生成前に排除する。
- 外部効果回数を必ず1回と仮定しない。0、1、partial、unknownを表現できる。

### 10.3 Dataset分離

session単位でdevelopment、calibration、acceptanceへ分ける。同じ連続captureのframeを別splitへ分散しない。

- development: recognizer／prompt作成に利用
- calibration: threshold、confidence calibration
- acceptance: release判定専用。調整へ再利用しない

正解はGameLab内部stateまたは独立human labelから作る。Planner自身を成功判定者にしない。

frozen acceptance datasetはrecognizer、planner、runtimeの品質判定に使うが、実game stepをVerifiedへ昇格させない。実gameの昇格証拠は独立live sessionだけである。

### 10.4 Support matrix

状態はSupported、Experimental、Unsupported、Unverifiedの4値とする。次を列に持つ。

- Windows build／edition／architecture
- standard／elevated権限
- VID/PID／revision／firmware
- Logitech driver、LGS、G HUB、Dynamic Lighting
- display mode、capture backend、resolution、DPI、HDR、monitor数
- occluded、minimized、RDP
- game／region／build／anti-cheat
- input route
- policy確認日
- AI provider／model／network mode
- installer／update route

Windows 10は一般support終了後のOSであるため、Phase 0で明示裁定する。初期primary targetは現在の実機Windows 11 x64とし、Windows 10、ARM64、RDPを自動的にSupportedへ含めない。

## 11. Migration／Distribution／Operations

### 11.1 Migration Safety Gate

最初のdevice writeより前に次を保存する。

- LGS version、mode、process／service
- Logitech driver、G HUB、Dynamic Lighting
- G13/G600 descriptor、firmware、device path
- LGS host profile、macro、app associationの取得可能なbackup
- G600 F0／F3〜F5完全report
- hash、取得日時、restore手順

flowはinventory → dry-run → 利用者確認 → apply → readback → restore testとする。LGS profile削除を既定にしない。自動import不能なら「手動再設定＋元環境へ復帰」と正直に表示する。

### 11.2 Distribution Contract

次をDistribution Contractとして確定する。期限は§16の各行（packaging方式は「最初の外部配布またはLampArray background制御の早い方の前」）に従う。

- public name、package identity、publisher
- MSIX／Sparse／MSI、per-user／per-machine
- self-contained .NETの有無
- elevationとoptional driver
- autostart
- data location
- update channel
- schema migrationとrollback
- signingとtimestamp
- certificate rotation／revocation
- uninstall時のdata保持／削除／device復元

Dynamic Lightingのbackground controlにはpackage identityとAppExtensionが必要なため、G600 LampArrayをbackground制御するreleaseまでにpackagingを確定する。

updateはactive Run、macro、device write中に適用しない。rollback非対応schemaを含むreleaseはautomatic rollback可能と表示しない。

### 11.3 Diagnostics

Execution JournalとEngineering Logを分離する。

Engineering Logにはbuild、schema、OS、device、firmware、driver、capture backend、AI model、error category、correlation ID、wall／monotonic timeを持たせる。

diagnostic bundle:

- localで生成
- manifestとpreviewを表示
- screen、OCR、prompt、journal本文、crash dumpは既定除外
- secret redaction testに失敗したbundleは送信不可
- retentionとsize上限
- 利用者が削除可能
- failure一遷移をcorrelation IDで追える

## 12. Game policy／法務／ライセンス

### 12.1 Game Policy Record

実gameごとに次を保存する。

- publisher、game、region、version
- terms URL、取得本文hash、確認日、確認者
- Observe／Assist／Auto mode別判定
- 根拠条項
- anti-cheat情報
- 次回確認日、変更検出時動作

未確認、変更検出、解釈不明はautomation disabledとする。SendInputが受理されたことを規約許可の証拠にしない。import Playbookもpolicy gateを迂回できない。

### 12.2 商標

Logicool、Logitech、G13、G600は他社brand／製品識別子である。

- public nameとpublisher identityを最初の外部配布前に確定する。
- OpenLogicool名称は提携誤認riskがあるため商標調査と名称裁定を行う。
- installer、About、配布pageへ非公式・非提携を表示する。
- 他社logo、公式UI意匠を使用しない。

### 12.3 OSS／protocol資産

- MIT codeを利用する場合は固定commit、hash、copyright、license noticeを台帳化する。
- 明示licenseのないrepositoryは通信事実の参考に限定し、code、comment、配列表現をcopyしない。
- 実装は自前の型、命名、test vectorで再構成する。
- dependency、OCR model、image、Knowledge Pack素材をSBOM／Third-Party Noticesへ含める。
- Knowledge Packと共有Playbookに含まれる画像・文章の再配布権を確認する。
- LGS binary、firmware、暗号回避物を再配布しない。

## 13. Risk register

| ID | Risk | 現在 | 影響 | Trigger／Mitigation | Gate |
|---|---|---|---|---|---|
| R-01 | G600 live inputを一意に取れない | 未確認 | Critical | EXP-G600-02、onboard限定またはdriver分岐 | G0-Device-W |
| R-02 | G600 writeでunknown byte／profileを壊す | 未確認 | High | full backup、byte diff、power cycle、restore | 各write前 |
| R-03 | G13 LCD endpointへHidUsbのまま書けない | 未確認 | Medium | outputを独立probe、WinUSBは別decision | R5 |
| R-04 | 元入力抑止にdriverが必要 | 未確認 | High | suppressionとvirtual HIDを分離評価 | R1/R5 |
| R-05 | profile切替中にwrong key release | 設計済み未実装 | Critical | PressOwnershipとgeneration property test | R1 |
| R-06 | hard crashでkeyが残る | 未確認 | High | output残留なしの実証、または期限付きrelease watchdog | R1／Game Operator |
| R-07 | capture black／stale／座標ずれ | 一部実測 | High | backend matrix、transform revision | R4 |
| R-08 | 誤認識でfalse success／不可逆操作 | 未確認 | Critical | Unknown分離、stable window、frozen corpus | R4 |
| R-09 | Dispatch後crashで二重実行 | 設計済み未実装 | Critical | DispatchArmed、OutcomeUnknown、fault matrix | R3 |
| R-10 | AIがschema外操作を提案 | 未確認 | Critical | allowlist proposal、runtime rejection | R4 |
| R-11 | 画面／OCR／promptから個人data送信 | data contract未作成 | Critical | default OFF、crop consent、deletion | Phase 4前（§6.12と同時点） |
| R-12 | AI費用／timeoutでrunが停止 | 未確認 | Medium | benchmark、cap、明示停止、no fallback | R4 |
| R-13 | LGS/G HUB移行で設定喪失 | 未確認 | High | Migration Safety Gate、dry-run、rollback | 最初のwrite前 |
| R-14 | game規約／anti-cheat違反 | game別未確認 | Critical | Policy Record、mode gate | 実game前 |
| R-15 | update後schemaをrollback不能 | contract未作成 | High | N-1→N／N→N-1 clean VM | public update前 |
| R-16 | OpenLogicool名称の提携誤認 | 認識済み | High | public name裁定、trademark review | 外部配布前 |
| R-17 | 無許諾code／素材混入 | 部分台帳 | High | fixed commit、SBOM、notices、provenance | dependency採用時 |
| R-18 | single実機が並行Laneのbottleneck | 確認済み | Medium | device session owner、recorded fixture | 全Phase |

各riskにowner、probability、validation evidence、residual risk、acceptance authority、review dateをPhase開始時に追加する。

## 14. Release gate

Public candidateは製品面ごとに判定する。Input StudioはPlaybook、Capture、Perception、AI、Game Policyの完成を待たない。Game OperatorはInput Studioの低遅延pathを変更せず、追加gateを通す。

### 14.1 Shared Distribution Gate

Input StudioとGame Operatorの両方に必要:

1. 公開面でSupported表示するsupport matrix全行が最終artifactで合格。
2. clean install、N-1→N、対応するrollback、repair、uninstall、update interruptionがSupported OSで通る。
3. secretがconfig、DB、log、journal、export、bundle、dumpから検出されない。
4. 既定diagnostic bundleにscreen、secret、personal dataがなく、利用者previewと削除が機能する。
5. artifact、installer、update manifestに署名、timestamp、hash、SBOM、Third-Party Noticesがある。
6. public name、trademark、privacy noticeが承認済み。
7. optional driverがある場合、署名、install、uninstall、rollback、OS matrixを別artifactで通す。
8. 未解決Critical riskは例外なくNo-Go。
9. High riskを受容できるのは、影響capabilityを最終artifactで無効化してUnsupportedと表示し、release claim、device state、利用者data、外部accountへ影響しないことを実証した場合だけ。記録やowner承認だけでは足りない。

### 14.2 Input Studio Public Gate

1. Device／Input／Profile／Application Workspaceの対象requirement（R1／R2行だけ。APP-010／APP-011等のR3／R4行を含まない）が合格。
2. parser replay、generation race、hotplug、sleep、foreground、input acceptanceがSupported matrixで合格。
3. hard crashでoutputが残留しないことを実証するか、watchdogが期限内に全releaseする。
4. LGS migration dry-run、cancel、device restoreが実機で通り、元profileを破壊しない。
5. G600 routeと3-slot／driver等の制約をUI、support matrix、release noteで明示。
6. install、update、rollback中にdevice writeを開始しない。
7. AI、network、captureが利用不能でも全Supported Input Studio機能が動作。

### 14.3 Game Operator Public Gate

Shared Distribution Gateに加えて次を全て要求する。

1. 全fault pointでunknownの自動再送、false success、journal replay不一致0。
2. host process死亡後も所有outputを期限内にreleaseし、再起動後のownership reconcile完了まで次dispatchを禁止。
3. high-impact actionの未承認dispatch 0。
4. frame corpusとAI evalが事前固定thresholdを満たし、dataset、model、prompt、parameterを記録。
5. image保存、cloud送信、削除、provider、costをUIから確認・制御可能。
6. provider data policyと対象Game Policy Recordがmode別に承認済み。
7. Observe Only、Teach、Supervised、Verifiedの各modeがcapability gateを迂回できない。
8. 実game用Verified Stepは同一環境条件の独立live session証拠を持つ。

### 14.4 LGS Parity Claim Gate

LGS Parityは、Phase 0のcanonical inventoryでLGS 9.04.49上の存在を確認した全capabilityが、対象support matrixでSupportedの場合だけ使用できる。Unsupported、Deferred、Out of Scope、未確認が一行でもあれば不合格であり、owner裁定で例外化しない。その場合のclaimはCore LGS ReplacementまたはPartial LGS Replacementに限定する。

## 15. 最初の着手package

計画承認後の最初の実装packageはPhase 0だけとする。

### Deliverable 0A: LGS baseline

- canonical parity matrix
- LGS/G HUB/driver inventory
- migration backup inventory
- public claimの暫定分類

### Deliverable 0B: Device read-only probe

- Windows-native probe skeleton
- G13 all-TLC／Raw Input report
- G600 Feature Report backup
- G600 live input route report
- redacted device fixture
- G0-Device-RO decision（write実験を要するG0-Device-WはPhase 2入口）

### Deliverable 0C: Capture／AI feasibility

- WGC／Duplication／visible backend report
- NIKKE探索実験の再現可能なmetadata
- Frame／Observation／Proposal draft
- Screen Graph draft（node＝state、edge＝visual target／遷移。Observe Only蓄積を前提とした形式）
- Data Flow inventory
- provider benchmark plan
- G0-Automation decision

### Deliverable 0D: UX／GameLab draft

- app-first Workspace wireframe
- onboarding、Teach、pause／resume、diagnostics journey
- GameLab scenario-v1 specification
- contract ownership table
- parallel Lane write scope

### Phase 0受入

- 「確認済み／強い推定／未確認／非対応」が各成果へ付く。
- 失敗をfallbackで隠さない。
- device writeを行っていない。
- cloudへ実game画像を送っていない。
- G600 route、Migration Safety、Frame／Observation契約の次の決定ができる。
- Phase 1へ進めるLaneと、追加実験が必要なLaneを別々に判定できる。

## 16. 未決定事項と決定期限

| Decision | 期限 | 決定材料 |
|---|---|---|
| Windows 10をSupportedに含めるか | Phase 0終了 | lifecycle文書とruntime要件の書面判定（「含める」判断の場合だけclean VM実測を追加してから確定する） |
| G600 方式A／B／C route | **決定済み（2026-08-15 オーナー裁定）: B変種を主経路（side 12ボタンを中間usage F13〜F24へ書換えて legacy 無害化）、Aを補完（slot 一括切替・退避）、Cは不採用のまま最後の手段** | 全実験成立: EXP-MIG-01（apply往復）、EXP-G600-03（slot切替）、EXP-G600-02 write拡張（[g600-route-assessment-2026-08-15.md](g600-route-assessment-2026-08-15.md) §5） |
| hard-crash watchdog | **決定済み（2026-08-16 オーナー裁定）**: watchdog採用は必須（実測確定 2026-08-15）。**uiAccess署名は費用（署名subscription）を理由に不採用**。watchdogの昇格実行も現段階では不採用とし、elevated foreground中の配送・release不能は残留riskとしてSupported matrixの行条件に表示して閉じる。elevated対応の実需要が出た時だけ、昇格watchdog（初回UAC承認のopt-in・証明書不要）を再検討する | EXP-IN-03 実測済み（probe `crash-keystate`・Windows 11 26200・2試行再現）: SendInput key-down は process hard kill（TerminateProcess）後も残留し OS は自動releaseしない（5秒/10秒観測とも残留継続）。別 process からの SendInput key-up で release 成立（通常IL・非elevated foreground 条件）。よって「残留なし実証」ルートは棄却、Supported path は watchdog release が条件。elevated foreground 下の release 可否は EXP-IN-01 の elevated 実測と併せて判定する。証跡: probe-output/crash-keystate-20260815-142715-246.json / -142739-698.json |
| SendInput acceptance（EXP-IN-01） | **実測完了（2026-08-16）**——受信側観測（probe `sendinput-accept`・test target window の WM_KEYDOWN/KEYUP log と送信列の突合・Windows 11 26200）で両側分類確定: **standard foreground＝Delivered（確認済み）**——単一 key・chord とも完全順序一致（6/6）。**elevated foreground＝Blocked（確認済み）**——target の昇格を TokenElevation で実測・foreground 確認済みの条件で受信ゼロ（UIPI 遮断どおり・API 戻り値は成功のまま）。target game 分類は Phase 7 pilot で個別実測。mouse button 分類は cursor 依存のため未実施 | 証跡: probe-output/sendinput-accept-standard-20260816-014738-596.json / sendinput-accept-elevated-20260816-022736-103.json。含意: elevated foreground 中は配送も release 注入も不能＝Supported matrix の行条件として明示必須。watchdog の昇格実行/uiAccess 署名の採否（上行）はこの結果を材料にオーナー裁定 |
| WGC以外のbackendを製品化するか | Phase 5開始 | capability matrix |
| AI provider／local model | Phase 6内・Teach mode実装前 | EXP-AI-01 frozen benchmark（Phase 6序盤に割当。corpusはPhase 5成果を使う）、cost、data policy |
| initial real-game pilot | Phase 7開始 | policy、capture、input、reset cycle |
| public product name | 最初の外部配布前 | trademark、publisher identity |
| MSIX／Sparse／MSI | 最初の外部配布またはLampArray background制御の早い方の前（本行が唯一の期限。§0.2・§11.2はここを参照する） | EXP-DIST-01（Phase 8A実施）、identity、tray、update、driver |
| optional kernel driver | G0-Device-W後 | suppression necessity、signing burden |
| full LGS script／LCD applet compatibility | parity inventory後 | 実装しない場合はLGS Parity claimを使わずCore／Partial claimへ固定 |

未決定を仮の実装で埋めない。各deadlineまではinterfaceとfixtureだけを作り、decision後に一つの方式を実装する。
