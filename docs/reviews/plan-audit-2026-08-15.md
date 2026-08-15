# development-plan.md v0.2 敵対的監査（2026-08-15）

- 対象: [docs/development-plan.md](../development-plan.md)（Screen Graph 追記後、commit 51e4eaa 相当＋当日追記）
- 体制: Claude Fable 5×high 4席（内部整合性／技術成立性／gate・工程構造／自動化・AI契約、read-only）＋ Grok 4.6×high 1席（全視点統合、read-only、Microsoft一次資料の web 裏取りあり）
- 検証: 全指摘を親（ベル）が原文と突き合わせ、確信できたものだけ採用。棄却は各席が自己申告したものに加え、親判定の棄却を末尾に記載
- 監査の制約: 「野心的すぎる／scopeを削れ」系の縮退提案は指摘として数えない（オーナー裁定）。計画が自認する「未確認」を未確認と指摘することも数えない

## 結論

証拠4値・fast path 分離・crash matrix の骨格は5席とも「一貫している」と判定。壊れているのは**分岐と契約の接続**であり、次の8クラスタに集約される。A・B・C・D は複数プロバイダ（Fable と Grok）が独立に同じ穴を突いた高確度の欠陥。

## A. G600 route 決定（G0-Device）の破れ — High

1. **Phase 0 read-only と判定・Gate の矛盾**: §11.1 Migration Safety Gate の flow は apply・restore test（＝device write）を含むのに、Phase 0 実施は Gate を要求し、Phase 0 受入は「device writeを行っていない」を要求する。方式b（中間usage変換）の成立判定も onboard への write なしには原理的に閉じない。G0-Device Exit「a／b／cのいずれかへ決まり」は、read-only 制約下では方式aへの即断か driver 分岐しか選べず、「実測後に選ぶ」（§0）と食い違う。〔Fable gate#4/#9・tech#1、Grok#1〕
2. **a／b／c が未定義ラベル**: 文書のどこにも定義がなく、§0 の3方式と §6.6 の5 route（禁止1・suppression 2変種）のどちらへ写像するか判定不能。〔Fable gate#7・consistency#5、Grok#2〕
3. **onboard 選択時に R2 app-first の合法経路がない**: onboard 直接利用で foreground 切替（APP-006、R2）を成立させるには F0 slot 切替＝device write が要るが、DEV-010 は write 有効化を R5 に置く。MAP-010 は 154-byte write だけを禁じ F0 を逃すが、DEV-010 が write 一般を塞ぐ。software 切替で逃げるならそれは中間usage変換であり、G0 の選択がすり替わる。〔Grok#3、親が原文で確認〕

## B. Attempt state machine の系統的穴 — High

4. **前進以外の遷移がほぼ未定義**: Proposed／Authorized／Prepared からの中止・失敗遷移がない（PB-007 の abandon が機械上存在しない）。DispatchArmed 後に process が生きたまま dispatch を中止した場合（PER-005 発動、Alt+Tab 等）の遷移先がない——契約2は「processが止まった場合」だけを OutcomeUnknown にする。NeedsUserDecision に出口遷移がなく、利用者判断を Confirmed へ写像すると契約4（Observation 必須）と衝突する。crash matrix 境界2（Prepared 後 DispatchArmed 前）の再開状態が未定義。〔Fable contracts#1/#2/#3、Grok#6/#7——同根〕
5. **「〜しない」契約の膠着**: 武装済み Attempt × capture Unavailable × 再送禁止 × UniqueMatch 以外再開禁止、の組で、Confirmed も再開も abandon もできない状態が成立する。脱出経路の未定義が問題（停止が意図なら、その明示がない）。〔Fable contracts 棄却候補の精密化、Grok#7〕
6. **Observation→Attempt の相関 field が契約にない**: 契約4は「同じAttemptを参照するObservation」を要求するが、ObservationResult（§6.4）にも PER-002 にも Attempt 参照がない。紐付けの意味 owner も未定義。〔Fable contracts#5〕
7. **RunEvent 必須 field「Observation ID」を前段 event が満たせない**: Proposed〜DispatchReported には Observation が存在しない。nullable にするなら §6.4 の「nullability変更は semantic breaking」との整合を先に定義する必要がある。〔Grok#6(d)〕

## C. Screen Graph の統合欠落 — High

8. **第一級成果物なのに契約・owner・検証手続き・ID join がない**: §6.4 contract baseline に Screen Graph 型がなく、§7.2 に semantic owner subtree がなく、KP-005 の「検証」の判定基準がどこにもない（§6.8 の昇格は Playbook step 専用）。「state」が Playbook node・Observation Known・§6.11 `states`・Screen Graph node の4箇所に現れ同一 ID で結ばれず、§6.11 では `states` と `screen-graph` が兄弟で二重定義。Screen Graph の独立 version と Verified Step の環境 scope が連動せず、地図を更新しても古い地図の Verified が走れる。AI 変更禁止リスト（§6.10）に Screen Graph の検証状態が含まれない。〔Fable contracts#4、Grok#9——同根で相互補完〕

## D. Playbook 合成入力の出力契約と所有 — High

9. **Semantic Action→outputs の解決が未定義**: outputs は BindingRevision（物理 control 側）にしかなく、Playbook が Semantic Action を dispatch する時にどの device のどの output を送るか決まらない（MAP-001 の複数 device 割当で非決定）。〔Grok#8〕
10. **PressOwnership が physical down 専用**: Playbook の合成 down は所有集合に入らず、Game Operator Public Gate が要求する crash 時 release の対象が自動化入力に対して未定義。物理入力と自動化入力が同じ Mapping Runtime を共有する際の優先・排他・manual intervention 判定の仲裁契約もない。〔Grok#8〕
11. **MAP-008（停止境界）が R5 なのに R3 から外部入力する**: R3 の Attempt はキーを送り UX-004 の stop はそれを解放する必要があるが、「取消不能 macro を作らない」が R5 要件のため Phase 4〜7 の DoD に入らない。〔Grok#13〕

## E. 検証昇格・実行 mode の梯子 — Medium

12. **replayed の成立条件と replayed→verified の差分が未定義**。しかも §5.4 を字義どおり読むと verified を生産できる mode が存在しない（Supervised の上限は replayed、Verified Run は verified しか実行しない）。〔Fable contracts#6〕
13. **Supervised Run の未知操作 proposal が無制約**: AI-004 の visual target 制約は teach mode 限定で、Supervised の未知確認経路では AI が任意座標を提案できる空白がある。〔Fable contracts#7〕
14. **verified 環境 scope に resolution・display mode・DPI・HDR がない**: §6.11 が recognizer を resolution へ紐付けると自認しているのに、§6.8 の同一環境判定は resolution を問わない。〔Fable contracts#8〕
15. **frozen acceptance dataset の用途が §1/§6.8 と §10.3 で食い違う**: GameLab 内 Verified 昇格に acceptance を使う定義と「release 判定専用・調整へ再利用しない」が両立しない。〔Fable contracts#9〕

## F. Release・parity・gate のラベル矛盾 — High/Medium

16. **chord／有限 sequence が R1（DEV-006）と R5（§4 parity・MAP-007）の両方にある**: Phase 2 で chord を出すと §4 の R5 行と矛盾し、外すと DEV-006 が落ちる。「常用」境界が最初の slice で確定できない。〔Grok#4〕
17. **Wave 7A「Input Studio Public Gateだけで公開できる」が §14.1（Shared Distribution Gate は両製品に必須）・Phase 8A Exit と文言矛盾**。〔Fable consistency#2、gate 席は誤読側として棄却——文言修正で解消する類〕
18. **§14.2.1「Application Workspaceの対象requirement」が APP-010（R3）／APP-011（R4）を含むと読める**: §14 前文「Playbook・AIを待たない」と requirement 集合が二つの答えを持つ。〔Grok#5〕
19. **§3.9 traceability 表の破れ**: UX-003〜005 の二重割当（Phase 2/3・A,D と Phase 4・E,I,K）、APP/MAP 群の R3〜R5 要件を Phase 2/3 へ一括割当、OPS-001/002（R1）がどの行にも属さない、KP-005（Observe Only 前提）の主 Phase 5 と Observe Only 実装 Phase 6 の不一致。〔Fable consistency#4〕
20. **packaging／public name の決定期限が三重定義**: §0.2「Dynamic Lighting background または公開配布前」・§11.2「Phase 4完了まで」・§16「Phase 8前／最初の外部配布前」。優先規則がない。〔Fable consistency#1・gate#3、Grok#11〕

## G. §16 決定期限と実験割当のずれ — High

21. **hard-crash watchdog**: 期限「Phase 2開始」に対し、材料の key state 実測（EXP-IN-03）はどの Phase にも割り当てられておらず、実測対象の SendInput 経路自体が Phase 2 の成果物。「期限内release」の期限値も未定義。〔Fable gate#1、Grok#11〕
22. **AI provider**: 期限「Phase 6開始」に対し、frozen benchmark（EXP-AI-01）の実行は Phase 6 実施内にしかなく、corpus 分離は Phase 5 成果。〔Fable gate#2、Grok#11〕
23. **EXP-G600-03/04・EXP-DIST-01 が Phase 未割当**。Windows 10 裁定（Phase 0 終了期限）の材料 clean VM も Phase 0 にない。〔Fable gate#8、Grok#11〕

## H. Windows 技術条件の欠落 — Medium

24. **SendInput の UIPI 沈黙失敗と分類軸の不整合**: SendInput はシステム入力ストリームへ挿入し、UIPI で落ちても戻り値・GetLastError は原因を示さない（Microsoft 公式、Grok が一次資料確認）。EXP-IN-01 の「受理／UIPI不達／ゲーム側不達」三分類は API 返却だけでは原理的に立たず、ObservationResult 四値は「届かなかった」と「届いたが変化なし」を区別できない。release 経路（NFR-008・watchdog）にも同じ UIPI 制約が適用されておらず、elevated foreground では期限内 release を保証できない——watchdog の IL／uiAccess 契約が必要。〔Fable tech#2、Grok#10——同根で相互補完〕
25. **「LampArray 列挙 確認済み」の存続条件欠落**: G600（2012年）自体の LampArray 実装は考えにくく、列挙は Logitech ソフトウェア由来の可能性が高い。LGS 除去後も列挙が残るかの条件付けがなく、Phase 2 Exit「LGS virtual bus に依存しない」と噛み合わない。EXP-LGS-01 に提供元 device stack の特定を追加すべき。〔Fable tech#3〕
26. **EXP-DIST-01 の clean VM で HID/LampArray 行が判定不能になり得る**: Hyper-V は汎用 USB passthrough 非対応。hypervisor 条件の明記か行の分割が要る。〔Fable tech#4〕

## I. Ownership 正典の破れ — Medium

27. **SQLite implementation の owner が §6.3「Platform」と §7.1/7.2「K」で矛盾**（Lane J の名称が Platform／Integration のため二重 owner に読める）。〔Fable gate#5・consistency#3〕
28. **owner の引けない成果物**: SemanticAction 契約（owner「Domain」）に対応する Contracts subtree が §7.2 にない。DeviceInstance/PhysicalInput は G13/G600 横断契約だが置き場が未定義。Domain module 内の Playbook pure model（§6.3）が Lane D/E どちらの write scope にも属さない。〔Fable gate#6〕

## 親判定で棄却した主な指摘

- §6.12「Phase 4前」と R-11「Phase 5前」の Data Flow 期限差 → Phase 4/5 並行のため同時点（Fable consistency 席の棄却を支持。Grok#12 の「三重定義」は G0-Automation が「項目決定」で段階が異なるため部分棄却）
- G0-Automation が GameLab fixture を要求する循環（Grok#12 後段）→ Phase 0 の許可範囲に GameLab prototype が含まれるため弱い。仕様表現で判定可能
- 「§14.3-1 false success 0 は検証不能」→ GameLab oracle と human label で判定手段あり
- 各席が自己棄却したもの（Observing 中 crash は契約2が包含、承認の単一使用内束縛、F13-24 の usage 数、ほか）は各席の判断を支持

## 修正の優先順位（提案）

実装より先に一枚の契約へ落とすべきもの: ①G0 の写像（方式→どの release で何を write してよいか）と Phase 0 の write 例外規定、②DispatchArmed の中止辺を含む Attempt 全遷移表、③Screen Graph の contract 行・owner subtree・検証手続き・state ID の統一、④Semantic Action→outputs の解決規則と合成入力の PressOwnership、⑤SendInput 不達の観測点と watchdog の IL 契約。F・G・I は文言・表の修正で閉じる。

## 体制の評価メモ（モデル配置の実測データ）

- Fable 4席（視点分業）: 計27指摘・自己棄却あり。整合性・工程系の網羅が強い。所要 約4.5分/席、各席 約6万 subagent tokens
- Grok 4.6×high 1席（全部盛り）: 13指摘。うち6件（A3・B7・D9/10/11・F16・F18）は Fable 4席の誰も出していない固有発見で、特に D クラスタ（Playbook 出力契約）は本監査の最重要発見の一つ。Microsoft 一次資料の web 裏取りを自発的に実施。大クラスタ（A・B・C・G）では Fable と独立に一致——cross-provider 確証になった。実働 約40分
- 運用上の注意: aiterm 経由の Grok 起動は権限プロンプトで無人停止し得る（always-approve 前提が必要）。本監査で aiterm の Windows バグ2件を修正、3件目（transcript 越境）と自動更新巻き戻しは未解決
