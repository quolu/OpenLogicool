# t12 NIKKE safe slice 実機受入

取得日: 2026-08-24  
判定: **成立**

## 結論

NIKKE lobbyの非課金・非消費・非戦闘範囲で、ローカルvisionが発見したframe-bound targetをNano Serial HIDだけで開き、遷移先を観測し、Escapeで戻る可逆edgeを実機で成立させた。別process実行のlive sessionで同じ出発画面と遷移先を再同定し、同じopen→observe→backを再実行できた。

state IDとedge IDはsystem発行の不透明IDであり、`部隊`等の表示文字列をidentity keyにしていない。画面同一性は、独立frame間で一致した2個以上のgrounded anchorと位置から判定した。二つの独立sessionまで成立したため、node 2件と可逆edge 2件のverificationは`Replayed`である。`Verified`へはt13の別Supervised Runを証拠として一段だけ昇格する。

機械可読な判定記録は`t12-nikke-safe-slice.json`に保存した。

## 実測列

| session | 操作 | 画面証拠 | 判定 |
|---|---|---|---|
| discovery | lobby観測 | `live-discovery-observe-20260824-135015-905.json` | `部隊`、`ロビー`、`隊員募集`をgrounding |
| discovery | open | `live-discovery-nano-action-20260824-135058-072.json` | before／after SHA-256変化、Nano click、fallbackなし |
| discovery | 遷移先観測 | `live-discovery-observe-20260824-140858-183.json` | `部隊編成`、`CAMPAIGN`をgrounding |
| replay | lobby再同定 | `live-discovery-observe-20260824-164057-992.json` | discoveryと`ロビー`、`隊員募集`が一致 |
| replay | learned target再遷移 | `live-discovery-nano-action-20260824-164149-734.json` | discovery観測のlocatorを再使用、画面変化 |
| replay | 遷移先再同定 | `live-discovery-observe-20260824-164202-669.json` | discoveryと`部隊編成`、`CAMPAIGN`が一致 |
| replay | back | `live-discovery-nano-escape-20260824-164220-566.json` | Escape down/up、画面変化、PASS |
| replay | lobby帰還確認 | `live-discovery-observe-20260824-164237-584.json` | discoveryと`部隊`、`ロビー`が一致 |

このほか、replay開始前に遷移先から戻す独立Escapeを`live-discovery-nano-escape-20260824-164041-820.json`で確認した。

## 境界

- 操作はCOM8のSparkFun Pro Micro firmware 1.1.0経由だけ。`SendInputDispatchCount=0`、`ComputerUseDispatchCount=0`、fallbackなし。
- purchase 0、resource consumption 0、combat 0、account mutation 0。
- visionはMicrosoft Foundry Local 0.10.3とローカルQwenだけ。non-loopback接続0、外部AI送信0、外部AI API key 0、外部AI API費用0。
- 成功は画面before／afterと次frameの再同定だけで判定した。入力APIの成功値はgame内成功に算入していない。

## 発見したプローブ不具合の根治

初回open証拠は、遷移後にも共通navigation labelが残るのに`--expect-label-absent`を指定したため、画面SHA-256が変化して遷移先も観測できた一方で`Passed=false`になっていた。製品プローブを修正し、標準の成功条件を「click直前frameからscreen hashが変化したこと」とした。label消失は明示指定時だけ追加条件とし、変化を待たず直後frameを成功扱いする挙動も廃止した。

focused testは`OpenLogicool.Probe.Tests` 26件green。旧ログの`Passed`値を成功へ書き換えず、誤った呼出条件、raw before／after hash、後続destination観測をそのまま記録した。
