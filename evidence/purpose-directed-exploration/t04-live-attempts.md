# t04 実game目的 run

判定日: 2026-08-26

## 確認済み

- Windows 11／NIKKE実window／WGC／Foundry Local Qwen3-VL-4B／Nano Serial HID COM8を同じ製品compositionへ接続した。
- Foundry model明示load後、AI target discoveryは実応答した。外部AI transmission 0、外部API費用0。
- Nano KeyTap Escでイベント報酬→イベントstage→イベントoverview→イベントmenu→ロビーの実遷移を個別に確認した。Movedになった実証JSONは`game-interaction-foundation-key-tap-20260825-225146-563.json`、`225610-678.json`、`225740-655.json`、`230935-519.json`ほか。SendInput／Computer Use／fallback／自動再送0。
- ロビーの「部隊」をAI text groundingで発見し、Nano clickで部隊編成画面へ実遷移した。旧判定はafter identity Ambiguousだけで`Undetermined`となりroute未保存。実画面を根拠にMovedへ丸めず、判定順をfocused testで根治した。
- Compare前後のscene表現不一致、destination未確認gate、OCR anchor更新衝突、destination local OCR evidence欠落、Draft prefix完了誤判定を原因確認後に修理した。
- focusedはFoundry Local goal filter／類似OCR、known-first、route直接再生、正常edge再利用、失敗step差替え、1 anchor、Compare／learningを個別にgreen。最終関連8 project・516件、full regression 22 project・1192件green。
- COM8の物理USB再接続後、`serial-hid-direct-smoke-20260825-233256-966.json`がPASSし、firmware 1.1.0、Nano Serial HID、AllUpを再確認した。

## 実button目的の初回学習

- 採用する実目的はNIKKEロビーから「アークを開く」とした。課金、消費、戦闘開始は行わない。
- 空DB `purpose-directed-exploration-ark-visual-final-10s.db`から`game-interaction-foundation-purpose-run-20260826-005402-987.json`を実行し、PASSした。
- 現在pageに保存actionが無いためFoundry Local visual discoveryを1回だけ使い、target「アーク」、normalized bounds `[0.534, 0.628, 0.148, 0.148]`を発見した。AI callは1。
- Nano click後、Compareは10,059ms・19観測を継続して`Moved`。destination ID一致はgateにしていない。
- `edge:16bb782725f646e0affc565f5f34fc10`をStructureへcommitし、決定的route IDのrevision 1を`Compiled`で保存した。route revisionは0→1、purpose statusは`Completed`。
- SendInput 0、Computer Use 0、fallback 0、自動retry 0、外部AI transmission 0、外部API費用0。

## 別process再起動後の保存button AI 0再現

- `game-interaction-foundation-key-tap-20260826-005451-977.json`でアーク画面から戻り、`game-interaction-foundation-observe-20260826-005453-105.json`の実画像でロビー復帰を確認した。
- 初回の再生`game-interaction-foundation-purpose-run-20260826-005543-687.json`はMoved／CompletedでもAI call 1だったため不採用とした。原因は保存route hintがKnown OCR state分岐内に閉じていたこと。
- route edgeが保存するsemantic key、primitive、normalized boundsをcurrent frameへ直接束縛し、current window／frame／transform成立だけで既知actionを先に使うよう修正した。非遷移時だけ同じstepをAI repairへ移す。
- ダイアログ無しロビーを`game-interaction-foundation-observe-20260826-010630-030.json`の実画像で確認し、他の入力を挟まず別の`dotnet run` processで同じDBを再openした`game-interaction-foundation-purpose-run-20260826-010722-497.json`がPASSした。
- `UsedSavedRoute=true`、locator type=`saved-route`、target「アーク」、座標は初回と同一、AI call 0、route revision 1→1不変、Compare 10,087ms・18観測、`Moved`、purpose status `Completed`。after実画像はアーク画面で、`TRIBE`／`TOWER`／`ARENA`／`迎撃戦`等を含む。
- `CommittedEdgeId`とroute edgeはともに`edge:16bb782725f646e0affc565f5f34fc10`。正常replayはStructure edgeを再commitしていない。
- SendInput 0、Computer Use 0、fallback 0、自動retry 0。最後に`game-interaction-foundation-key-tap-20260826-010822-081.json`で戻り、`game-interaction-foundation-observe-20260826-010823-284.json`の実画像でダイアログ無しロビーを確認した。

## KeyTap目的の補助実証

- `game-interaction-foundation-purpose-run-20260826-001000-937.json`は、ロビーから「ゲームを終了しますか」確認画面を開くKeyTap目的を10,151ms・16観測、Moved、route 0→1で完了した。
- `game-interaction-foundation-purpose-run-20260826-001128-902.json`は別processでAI 0、route 1→1、10,180ms・16観測、Movedを確認した。ただしKeyTapはglobal candidateを合成するため、保存buttonのknown-first再生証拠には数えない。

## 学習結果として保持した非遷移

- 会話pageで誤候補の台詞clickは`Stayed`。
- Skip後ロード画面、イベントpageのEsc一部、部隊遷移の旧identity判定は`Undetermined`。
- イベントstageのBack visual targetは座標がbuttonより上で`Stayed`。
- いずれもRun成功へ丸めず、Learning Route revisionは0のまま。正常routeや既存evidenceを作り直していない。

## 不採用の旧試行

- `game-interaction-foundation-purpose-run-20260826-000721-101.json`も目的完了までPASSしたが、live Probeの待機上限が旧60秒で、裁定の10秒観測を満たさないため受入証拠には採用しない。製品共通runtimeは変えず、Windows live Probe構成の上限だけを10秒へ修正して上記2 Runを取り直した。
- `003532-079`は「部隊編成」goalに対して「アーク」を押し、正規化後空文字となる句読点OCRでcompletionがtrueになる欠陥を検出した。空文字をcompletion対象外にした。
- `004031-123`はvisual AIが「出撃」を返した誤grounding。target intentと無関係なvisual labelをFoundry Local専用adapterで除外するよう修正した。
- `004507-383`／`005100-797`はAI labelと同一frame OCRの完全一致要求で入力0。類似OCRを一意の候補へrebindするよう修正した。
- `004908-342`は「イベント」visual targetの座標が外れて非遷移。`Stayed`／`Undetermined`と同じく未達のまま保持した。
- `005826-295`はJSON上AI 0／Moved／Completedでも、開始frameに終了確認ダイアログが残り、保存座標clickがダイアログを閉じただけだった。円卓の実画像反証で検出し、受入から撤回した。
- visual遷移後にOCR anchor 2件を要求してdurable commitを拒否した旧条件を撤去し、1件の類似anchorでも保存可能にした。0件は既存visual fallbackだけを使う。
- 途中の`Stayed`／`Undetermined`／AI誤groundingは、正常遷移へ丸めず既存probe出力とSQLite学習結果に残した。失敗step以外の正常routeを作り直していない。

## 判定

確認済み。実gameの実button目的を未知AI探索から完了し、button、座標、destination node、edge、routeをdurable保存した。別process再起動後は保存actionをAI 0で再利用し、同じ目的を完了した。失敗stepだけのroute差替えはfocused SQLite testで確認し、liveでは意図的に正常routeを壊していない。
