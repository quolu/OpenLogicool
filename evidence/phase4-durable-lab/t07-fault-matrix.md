# t07-fault-matrix — 証跡

- 実装: hinata（2026-08-19）
- base: 0bd8bdb（t05 着地＋Tally 11種統合＋閉じ手順 docs 後の main）
- 対象: campaign t07（全 fault point で未解決 DispatchArmed から次 dispatch を自動生成しない・
  保証できる中止だけ Disarmed・保証できなければ OutcomeUnknown）、NFR-012、計画 §10.2 crash matrix

## 何を作ったか

### 1. `disarm` payload type（閉集合12種目・Contracts／RunJournal／run-event.md）

- 「外部入力 API を一度も呼んでいないことを runtime 自身が保証できる場合だけの中止終端」の journal 記録。
  AttemptId 必須・**ActorType は System のみ**（runtime の保証判定であり、利用者操作でも自動化の成功でもない。
  journal の append 検証が拒否——run 制御3種の User 限定と対をなす）。
- **OutcomeUnknown は意図的に journal event を持たない**: 「dispatch 済みで解決の記録が無い」ことが
  OutcomeUnknown の定義そのもの（§6.7 契約2）であり、live の分類と復元の既定分類が同じ根拠を読む。
  run-event.md に正本化。
- 外部効果回数を1回と仮定しない表現（§10.2）: 0回保証=Disarmed（disarm event）／報告あり=DispatchReported
  （dispatch-result）／partial・unknown=OutcomeUnknown（event なし）。

### 2. `AttemptFaultClassifier`（Domain・pure・新規）

fault point（HandledStop／TargetWindowLost／PartialSendInput）×外部入力呼出状態
（ProvablyNotCalled／CalledOrUnknown）→ §6.7 終端の決定的写像。

- 未呼出保証がある時だけ Disarmed、それ以外は全て OutcomeUnknown。
- **PartialSendInput×ProvablyNotCalled は例外**——部分成功は「呼ばれた」事実そのものであり、
  矛盾した保証主張を黙って安全側へ丸めると保証の出所の誤りが隠れる。
- crash は列挙に無い: crash の分類は検出でなく再起動復元（gate.Recover・契約2）が担う。

### 3. `AttemptDispatchGate` の拡張

- `CommitDisarmed`: disarm event を journal へ commit し DispatchArmed→Disarmed（Domain 遷移表が検証——
  Prepared 等からの disarm は §6.7 に無いため例外）。
- `Recover` が disarm event を尊重: 保証付き Disarmed が復元で OutcomeUnknown へ劣化しない
  （優先順: confirmation→disarm→run abandon→既定分類）。
- **CommandId 重複排除（§10.2 不変条件「duplicate UI command は Attempt 生成前に排除」）**:
  command→所有 Attempt の対応を保持し、①同一 command の再 proposal は Attempt 登録前に拒否
  ②他 Attempt が同じ command を dispatch する経路も拒否（自 Attempt の proposal→dispatch は通常経路として通る）
  ③Recover が journal の実 event から対応を再構築。terminal 後の同一 command 再投入も拒否＝
  再試行は新しい command として発行する（一度の UI 操作＝一つの command）。

### 4. `RunControls.CommitAttemptObserving`（すずね t05 監査 note [119]① の閉鎖）

attempt 束縛の観測を制御状態の下で gate へ通す口。manual intervention 中は拒否
（「介入開始と終了の間に observation event が現れない」journal 並び——t10 再開照合の前提——を
run-level（t05）に加え attempt 束縛側でも構造保証）。介入終了後は run-level の再照合が済むまで拒否（§6.8）。

### 5. `RunEventTally` へ Disarms（12列目）

replay と逐次 Apply の値等価は既存構造のまま成立（FaultMatrixTests で disarm 込みの一致を確認）。

## どう確認したか（最終試験結果）

worktree（base 0bd8bdb の clean checkout）内で focused test を実行。build 出力は一時
`Directory.Build.props`（宣言境界外・commit 前に削除済み）で scratchpad へ redirect、architecture test は
redirect 先に sln コピー＋src/tests junction（t05 と同じ測定器調整・テスト対象は実 worktree の実物）。

- `dotnet test tests/OpenLogicool.Domain.Tests` → **87件 green**（既存81＋AttemptFaultClassifierTests 6）
- `dotnet test tests/OpenLogicool.Playbooks.Tests` → **94件 green**（既存83＋FaultMatrixTests 10＋
  RunJournalTests disarm 検証 1。既存 fixture 2件は全 dispatch に既定 `command-1` を使い回しており
  新しい重複排除に該当したため、別 command へ修正——排除は §10.2 の意図どおりの動作で、
  修正は fixture の command 一意化のみ）
- `dotnet test tests/OpenLogicool.Architecture.Tests` → **4件 green**

### §10.2 crash matrix の対応（FaultMatrixTests・fault fixture・実画面なし）

crash は「journal がそこで途切れた」ことと等価（§6.7 契約2: 復元は journal の実 event だけを根拠にする）。

| 境界 | 試験 | 確認した不変条件 |
|---|---|---|
| 1・2 Prepared 前後 | proposal＋approval で途切れ→Recover | Cancelled（外部入力呼出前が確定） |
| 3 arm 後 input call 前（crash） | dispatch で途切れ→Recover | OutcomeUnknown・**次 dispatch 拒否（契約5）** |
| 3 arm 後（handled stop 検出・未呼出保証） | classifier→CommitDisarmed→Recover | Disarmed が復元でも保持・解決済みなので次 dispatch は通る・Prepared からの disarm は例外 |
| 4 key down 後 key up 前（partial SendInput） | externalInput が例外→classifier | 呼出1回のみ（自動再送なし）・OutcomeUnknown・保証主張との矛盾は例外・event なしで復元一致 |
| 5・6 外部効果後〜report 前 | journal 形は境界3と同一（区別不能） | 同上（OutcomeUnknown） |
| 7・8 capture 後〜Confirmed 前 | dispatch-result＋observation まで積んで途切れ→Recover | **observation event が在っても confirmation なしでは Confirmed へ戻らない**（false success 0） |
| 9 新 version 作成後 Run 切替前 | switch の journal append を fail 注入 | pin 不変・replay projection も旧 pin（active version が勝手に変わらない） |
| 10 manual intervention 中 reconcile 前 | begin 後の attempt 観測・終了直後の attempt 観測 | 介入中と再照合前の observation は journal へ入らない・crash 時は OutcomeUnknown |
| — duplicate UI command | 再 proposal／他 attempt dispatch／復元後 | Attempt 生成前に排除・復元でも排除が再構築される |
| — replay 一致 | disarm 込み全列で Replay vs 逐次 Apply | 値等価（NFR-012: journal 再生不一致 0） |

## 監査へ（見てほしい点）

1. **disarm の ActorType=System 限定と OutcomeUnknown の「event なし」対称性**が §6.7 契約2 の写しとして
   過不足ないか（記録なき解決を信じない原則と、記録ある保証を劣化させない原則の両立）。
2. **CommandId 重複排除の意味論**——「terminal 後も同一 command を拒否（再試行は新 command）」は
   §10.2「duplicate UI command は Attempt 生成前に排除」の読みとして妥当か
  （§6.7 契約8「前提が変わった Attempt は新 AttemptId」と整合させた設計判断。既存 fixture 2件の
   command 一意化はこの帰結）。
3. **境界5・6 を境界3と同一の journal 形として1試験に畳んだ判断**——「journal からは区別不能＝同一分類」
   という §6.7 契約2 の帰結の読みとして正しいか。

## 統合面（本 task の外・既存の親仕分けリストと同種）

- SessionRecorder／Replayer・ResumeReadiness は disarm を特別扱いしない（attempt 束縛 event として中立）。
  Tally は12列で自動追従済み。t08 GameLab の状態表示が Disarmed を表示語彙へ足すかは表示面（磨き/統合）。
