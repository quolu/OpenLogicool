# t11 実機確認（オーナー手番4点＋持ち越し）

- 実施: 2026-08-17（オーナー実機操作＋統括観測）
- 環境: G13/G600 両実機接続・既定 DB（`%LOCALAPPDATA%\OpenLogicool\input-studio.db`）・`ui --resident` および `run --trace`

## 結果

1. **キー録画 modal（t09 持ち越し）: 成立**。録画（chord 表示・録り直し）・「これに決める」確定・Esc キャンセル（割当不変）をオーナー目視で確認。
   - **発見バグ→根治**: IME オン時に実キーが `Key.ImeProcessed` に化けて録れない（`Vk:0xE5` が保存される）。modal 内 IME 無効化＋`ImeProcessedKey` 解決で修正（commit 13d35cd）。
2. **実機接続下 `ui --resident` 保存→即時反映（t09 持ち越し・Exit 条件成立要素）: 成立**。G9→`Key:A` 保存後にメモ帳へ「a」、録り直して `Key:B` 保存後に再起動なしで「b」（`db` の b。d は併存中の LGS（LCore 常駐を実測確認）のゲーム別割当が同時発動したもの——不具合ではなく LGS 併存の想定動作。移行時に LGS を止めれば消える。LGS 非常駐時は onboard 割当が同様に発動するため、その無害化＝B変種残置運用は移行フェーズの範囲）。
   - **発見バグ→根治**: `ui --resident` 同居時、表示用 raw input source の生成→Dispose がプロセス単位の raw input 登録を横取り・解除し、resident の実機入力が全死する（`run` 単体・実機なしでは再現しない）。resident 起動時の列挙結果を再利用して修正（commit 3584440）。
3. **Alt+Tab 編集対象保持（Exit 条件2）: 成立**。未保存変更（操作追加）を作って Alt+Tab 往復→編集内容・「未保存の変更あり」表示とも保持をオーナー目視で確認。
4. **NIKKE launcher→本体遷移（Exit 条件3・p1-core 持ち越し）: 成立**。`default-G600` を `C:\NIKKE\NIKKE\GAME\NIKKE.EXE` へ関連付け、`run --trace` の判断 log で観測:
   - launcher 前面: 「既定 app（identity 識別済み・関連付けなし）」のまま——本体用への誤切替なし
   - 本体前面: path 一致を観測（「一致 app」）
   - 本体終了後: 既定へ復帰
5. **Unknown 実トリガー（APP-008・p1-core 持ち越し）: 初実測成立**。NIKKE 本体（anti-cheat 保護）で「Unknown Application（identity 取得不能・既定 profile 適用）」を観測——黙って継続せず明示表示＋既定適用の設計どおり。

## 補足

- 条件3の実測は関連付け profile＝既定 profile の構成（切替は MatchKind 遷移で観測）。異なる profile 間の実切替適用は p1-core live run（explorer path 一致・Store メモ帳 package 一致）で実測済みであり、判断層＋適用層の両方が実測で閉じた。
- 表示の軽微課題（別 profile 未変更時の state 詳細が先頭 device の MatchKind を引く）を観測——機能影響なし、磨きフェーズ送り。
