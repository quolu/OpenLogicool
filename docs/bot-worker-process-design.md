# Bot の実行を別 process へ出す設計

Bot を動かす仕組みを、アプリ本体（常駐 Host）とは別の process で動かす。Bot の仕組みを入れ替えても、本体（G13／G600 のキー割当・遠隔表示の配信）は止めない。その後に、設定のファイルが変わったら Bot が動いたまま読み直す形を足す。

- 利用者の指示（2026-10-11）: 「Bot の機能追加のたびに OpenLogicool そのものの再起動になるのはなぜ」「設定変えるたびに再起動するアプリって微妙」「B（別 process）をやってから A（読み直し）をやる」「B も実装しろ。そのあと A もだ」。
- 本書は設計の下書きである。反証を通す前で、実装は始めていない。現在地は[引き継ぎ状態](bot-assistance-handoff.json)の`processSplit`で読む。
- 表記: 【確認】はコードを読んだ事実、【推測】【未確認】は読んでいない・測っていないもの。ファイル名は断りが無ければ`src/OpenLogicool.Host/`配下。行番号は調査時点（2026-10-11 02:30 ごろ）のもの。

## 1. Bot が本体から借りているもの

1. **Nano の接続と送出**【確認】
   - `Program.cs`が`residentHost?.BorrowedNanoSession`（`ResidentInputHost.cs`）を渡し、`HostBotScriptIntents.cs`が借りる（無ければ自分で開く）。
   - `VisualKeyAssistRuntime.RunAsync`が Nano を使うのは3か所だけ。`nano.DeviceIdentity`（手入力の見分け）、`SerialHidNanoGameInputDevice`（6操作の`INanoGameInputDevice`）、`WindowsTaskbarNanoWindowActivator`（中身は`Click`1回）。
   - `SerialHidEmitter`は「押下中の全キーの完全な状態」を1つ持つ（`src/OpenLogicool.Input/SerialHidEmitter.cs`）。fast path と Bot は今も同じ lock を共有している。
   - 50ms の heartbeat（`ResidentOutputSession.cs`）も本体の thread。
2. **物理ボタンの観測**【確認】: `ResidentInputHost`（累計 edge 数と押下数）→ `Program.cs` → `BotUserInputGate.cs` → `PhysicalInputPauseBridge.cs`。
3. **実行の排他**【確認】: `DemonstrationRecordingGate`を Bot・マクロ・記録が共有する。
4. **操作の通信口と状態**【確認】: `ApplicationControlRegistration.cs`が`IBotScriptIntents`をそのまま公開する。状態は`HostBotScriptIntents.Observe`がイベント名から組み立てる。モードは`RunningMode`を観測ごとに読む。
5. **通知・決裁箱・判断**【確認】: ファイル経由だけ（`bot-review-mcp.json`、`bot-screen-judge.json`、`<db>.bot-assistance/state.json`）。`BotAssistanceStore`は名前付き Mutex で、既に process 間で安全。異常終了の通知は本体が出す。
6. **保存先**【確認】: `bot-runs/<id>.db`は SQLite として開かず、`.visual-recovery.json`と`.remembered.json`の名前の元にだけ使う。ほかは`bot-runs/<id>/<実行>/`、`bot-runs/modes.json`（本体が書く）。
7. **取り込みと文字の読み取り**【確認】: `WindowsWgcGameFrameSource`と OCR は process ごとに独立で、本体と共有しない。
8. **手入力の監視**【確認】: pipe の待ち受けは Bot の側が作る（`ElevatedUserInputWatch.cs`、固定名）。分離後もそのまま動く作り。
9. **設定の読込（見落としやすい結合）**【確認】: `HostBotScriptIntents.Create`が本体の起動時に`AppContext.BaseDirectory/BotScripts`を厳格に検証して、機能とモードの一覧を固定する。新しい機能やモードは、本体を起動し直すまで一覧に出ない。設定の誤りは、本体の起動失敗になる。

## 2. 境界の案と推奨

- **案A（推奨）: 本体が Nano を持ち続け、Bot は`INanoGameInputDevice`の6操作を依頼する。**
  - 矢印を目標へ寄せる閉ループ（`SerialHidRelativePointer`）は本体に残り、入力1回が往復1回になる。
  - fast path の thread は pipe に触れない。依頼を受ける thread が、今の Bot と同じ lock を取るだけなので、fast path への干渉は今と同じ（裁定2を保つ）。
  - 有限の入力は本体の中で完結するので、Bot が途中で死んでもキーは残らない。
- **案B: 実行中だけ Bot へ接続を渡す。** 不採用。Nano は1本で、G13 のキーも Nano から出るため、Bot の間 G13／G600 が無出力になり、手入力優先が成り立たない。接続の切替で fast path が fault 停止する。
- **案C: Nano 専用の仲介 process。** 不採用。fast path が process 間のやりとりを待つことになり、裁定2に反する。
- **案D: 生の命令（SetState／MouseDelta）単位で依頼する。** 不採用。クリック1回で最大128往復になる。

**実行ファイルの形:** 新しい project は作らず、同じ`OpenLogicool.Host.exe`に`bot-worker`のサブコマンドを足し、別フォルダーへもう一度 publish して子 process として起動する。コードの移動と層の表の変更が要らない。専用 project への切り出しは、必要が出るまで行わない。

**計画との矛盾:** 計画 §6.2 は「最初は一つの resident process。不要な IPC を先に作らない」。今回の決定は §6.2 の変更なので、計画 §6.2 と §16 を先に直す。「executor は一つ」は、gate を本体に残して保つ。§6.5（合成入力の解放）は、Nano が本体にあるので変わらない。

## 3. やりとりの形と内容

実行ごとの乱数名の名前付きパイプを使う（本体が待ち受け、`CurrentUserOnly`、行区切り JSON。既存の`ApplicationControlPipe`と同じ流儀）。接続は2本。入力中に観測の問い合わせが待たされないようにするため。

| 向き | 内容 |
|---|---|
| Bot→本体（入力・往復） | `hello{protocol, dpi}` → `{nano の識別}`。`keyTap／hover／click／scroll／drag／flick` → `{ok, receipt}` または `{kind: pointer-unmoved｜fault, message}` |
| Bot→本体（問い合わせ・往復） | `physical` → `{edges, held}｜null`、`mode` → `{mode｜null}` |
| Bot→本体（片道） | `event{…}`（`events.jsonl`と同じ1行。`Observe`へ渡す） |
| 本体→Bot | 標準入力を閉じる＝停止。本体が死んだ時も同じ扱いになる（Watchdog と同じ作法） |
| 終了 | Bot が`result.json`を書いて終了。本体は終了コードとこのファイルを読む |

- `pointer-unmoved`は Bot 側で`SerialHidPointerMoveException`へ戻す（`NanoGameInteractionActions.cs`の判定を保つ）。
- protocol の番号が合わない時は、明示のエラーにする（黙って旧経路へ戻さない）。

**遅れが乗る場所:** キー1回は、往復1回＋直列通信2回。往復は 0.1〜0.3ms と【推測】（未実測）。クリックは、本体側の閉ループが支配する（1刻みごとに 8ms 待ち）。OCR（数百 ms）に比べると、往復の遅れは小さいと【推測】。

## 4. 導入の変更

- 本体は今のまま`artifacts/development/OpenLogicool/`に置く。
- Bot は`artifacts/development/OpenLogicool.Bot/<日時>/`へ publish し、`current.txt`を一時ファイルからの置き換えで更新する。
  - 本体が握る DLL とは別のファイルなので、本体が動いたまま入れ替えられる。
  - 動作中の Bot も別の版のフォルダーなので、publish の前に止める必要がない。
  - 戻す時は`current.txt`を書き戻す。
- 新しい`scripts/install-bot.ps1`の手順: publish → 必須ファイルと`bot-worker --self-test`（設定の読込と protocol の番号）を確かめる → `current.txt`を更新 → 古い版を2つ残して消す → `install-user-input-watch.ps1`を呼ぶ（中身が同じなら UAC は出ない）。
- 本体は`bot start`のたびに`current.txt`を読む。`BotScripts`も Bot のフォルダーのものを使う。

## 5. 段階

- **段階0（挙動不変）:** `RunAsync`の引数を`INanoGameInputDevice`＋`SerialHidCandidate`へ替え、前面化にも同じ口を受けさせる。
  - 完了条件: Host の試験が通り、導入後の`bot start`が今までどおり動く。
- **段階1（最小の分離）:** `bot-worker`、パイプ、`Create`の中の実行部分を子 process の起動へ替える、`install-bot.ps1`。`Create`の引数は変えない。
  - 完了条件（実機）: ①本体の PID が変わらないまま Bot を入れ替え、`run-started`に新しい版が出る。その間 G13 のキーが効く。②Bot の実行中に G13 を押すと`user-input-paused`になる。③Bot を強制終了すると、本体が異常終了を表示して通知し、gate が空き、キーが残らない。④本体を閉じると Bot も終わる。
- **段階2:** 本体が Bot の設定を解釈しない。一覧は`bot-worker describe`に聞く。
  - 完了条件: 機能のファイルを足すと、本体を起動し直さずに`bot functions`に出る。壊れた設定でも本体が起動する。
- **段階3（設定の読み直し）:** Bot がファイルの変更を見て、検証に通った時だけ差し替える。失敗した時は`profile-reload-failed`を出して旧設定で続け、状態にも出す。
  - 完了条件: Bot が動いたまま規則の位置を直し、次の観測から効く。

## 6. 試験と実測

- **層の規則**【確認】: `tests/OpenLogicool.Architecture.Tests/ProjectReferenceDirectionTests.cs`は project の集合が完全に一致することを要求する。同じ実行ファイルの案なら変更は要らない。待ち受け禁止の規則は`HttpListener`／`TcpListener`だけが対象で、名前付きパイプは当たらない。足す規則は、`OpenLogicool.Input`が`System.IO.Pipes`を参照しないこと、`bot-worker`の経路が`SerialHidDiscoveryService.Resolve`を呼ばないこと。
- **focused test:** にせの装置での往復、`pointer-unmoved`の写し、Bot が死んだ時の gate の解放、標準入力を閉じた時の停止、protocol 不一致の拒否。既存の`HostBotScriptIntentsTests`は実行部分が差し替え口なので、そのまま使える。
- **実測するもの:** Bot のクリック中の fast path の p99（基準 3.425ms）、往復の遅れ、`bot start`から`run-started`までの時間、両 process の DPI の扱いが同じか、管理者権限のゲームで監視 process が Bot 側の pipe へつながるか、窓なしの子 process で WGC と OCR が動くか。

## 7. 危険と未確認

- **DPI**【未確認】: 座標の計算は Bot、矢印の読み取りは本体になる。本体は WPF を初期化するが、Bot はしない見込み。拡大率が 100% でない画面でずれる恐れがあるので、`hello`で突き合わせ、不一致なら拒否する。
- **版のずれ**: イベント名（`Observe`）と設定の形式が、本体と Bot の間の契約になる。知らないイベントは無視されるので、足す分には安全【確認】。
- **残る再起動**: 本体に残る部分（6操作、閉ループ、パイプ）を直す時は、本体の再起動が要る。
- **Nano の fault**: 通信の fault は今と同じく本体ごと止まる。分離では改善しない。
- **publish の衝突**【推測】: 2つの会話が同時に`dotnet publish`すると、`obj`で衝突する。
- **残った Bot**: 本体を起動し直した後に古い Bot が残ると、監視の pipe（同時に1つ）を握ったままになる。標準入力が閉じたら必ず終わる作りにする。

## 8. 遠隔表示への影響

- **コード**: `Program.cs`の`Ui()`は遠隔表示と共用の配線。`Create`の引数を保てば触れない。`ApplicationControlRegistration.cs`も変更不要。
- **導入**: Bot の修理で本体を閉じなくなるので、配信が途切れなくなる。逆に、遠隔表示の側が本体を導入すると Bot も止まる。本体の導入スクリプトで`bot status`を見て警告を出す。
- **取り込み**: 映像は ffmpeg、音は本体、Bot の WGC は別 process で、互いに独立【確認】。
- **試験**: 層の試験のファイルは共用なので、追記だけにする。
