# t09 Windows実UI・NIKKE・Nano受入

## Windows UI

- `OpenLogicool.Host ui --db %LOCALAPPDATA%\OpenLogicool\input-studio.db --duration-ms 15000`をWindows nativeで実走し、exit 0。
- [input-studio-ui.png](input-studio-ui.png)で、既存の上部app／出力欄、左操作一覧、中央G13/G600図、右Inspector、下部保存／Game Operator導線が維持されていることを目視確認。
- 右Inspectorには既存の「録って追加／録って更新」を維持し、その下へ「マクロを選ぶ」だけを追加。未選択時はdisabled。
- WPF STA focused testで既存Game Operatorの同じTabControlに「マクロ」tabが追加され、別app／別window構成でないことを確認（5件 green）。

## NIKKE・Nano・AI 0再生

- 対象: 実process `nikke`、保存済みgoal「アークを開く」、1 step Learning Route。
- 開始画面: [nikke-before.png](nikke-before.png)（ホーム）。
- 新しい`WindowsPurposeMacroExecutionEngine`から`MacroPlaybackMode.AiFree`で実行。
- 結果: `Completed`、step 1、source `保存済み`、action `アーク`、Compare `Moved`、AI call **0**、route revision **1**。
- 機械JSON: [nikke-ai-free.json](nikke-ai-free.json)。`ExecutionRoute=NanoSerialHid`、`ComputerUse=false`、`SendInput=false`を含む。
- 同じ保存routeを`MacroPlaybackMode.AiMonitored`でも正常再生し、正常stepではAI call **0**・route revision **1**のまま完了した。機械JSONは[nikke-ai-monitored-normal.json](nikke-ai-monitored-normal.json)。
- 入力route: product executorの構造は**NanoSerialHidのみ**。Computer Use／SendInput game dispatch／fallback実装なし。macro snapshot自体はdispatch receiptを含まないため、JSONのroute 3 fieldはCLIが構造上の選択を投影した値である。
- 遷移後: [nikke-after-ai0.png](nikke-after-ai0.png)（アーク画面）。
- Nano `Key:Esc`で復帰し、[nikke-restored.png](nikke-restored.png)でホーム復帰を確認。復帰もAI call 0。
- live DB copy: `nikke-live.db`。元の未追跡probe DBは変更・削除していない。
- 終端取り直しで、保存route再生前にも旧索引更新が走りOCR anchorなしで停止する欠陥を発見した。保存routeの正常stepは既存edge／座標を使い索引を再作成しないよう根治し、focused test後に現行コードで上記AI 0実測を取り直した。

## 根拠4値

- Windows UI配置維持: 確認済み。
- Game Operator「マクロ」tab: コードとWPF STA testで確認済み。実window screenshotは未確認。
- 実UIからの新規作成／G13・G600割当／合成／再起動再生の全一巡: 未確認。public intent＋fake／実SQLite一貫scenarioでは確認済み。
- 製品macro executorによるNIKKE保存route AI 0再生: 確認済み。
- Moved／route revision不変: 確認済み。Nano-onlyは実装経路＋CLI投影で強い推定（macro snapshot内の独立dispatch receiptはなし）。
- AI監視ありの失敗step修復: fake＋SQLite一貫scenarioで確認済み。NIKKE liveでは両modeとも正常な保存stepが成立したためrepairは発動せず、live repair発動は未確認。
