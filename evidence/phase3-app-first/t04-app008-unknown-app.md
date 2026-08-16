# t04-app008-unknown-app 完了証跡

- 実装: implementer（sonnet×medium）委譲・統括が focused test 再実行と diff 確認で受入
- `ForegroundState`（KnownMatched／KnownDefault／UnknownApplication）＋pure 導出（全 identity-unavailable → Unknown・一致1種別でもあれば KnownMatched）・遷移時のみ log（Unknown への遷移と復帰の両方向）
- 「直前 profile を黙って継続しない」は既存の identity-unavailable → 既定再解決で構造的に成立——新しい抑止機構は追加せず、2 tick 通し test（p-game→p-default）で性質を固定
- diagnostics は別 process のためその場の identity＋resolver から同じ pure 導出で状態を表示
- 検証: Host 34件（+7 fact）・Architecture 4件 green（worker＋統括の両方）。実走: 実機2台＋temp DB で run 5 秒 exit 0・初期状態行表示・diagnostics の状態行一致
- 未実測: 実機での Unknown 遷移トリガー（昇格 process 前面化が必要）——オーナー実機手番に含める
- 発見事項: `HasAppAssociations` は path/package 関連付けだけを見る（既定のみでは poll 非起動）。現行設計どおりだが、UI 設計時に「既定だけの構成でも poll を起動するか」を論点として持ち越す
