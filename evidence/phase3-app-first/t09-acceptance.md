# t09 受入証跡（UI 本体実装）

- 受入日: 2026-08-16 夜（統括 bell-fable）
- 経過: 初期実装3段受入→オーナー0点評価→モック承認制へ転換（Grok モック docs/ui-mocks/ をオーナー承認）→WPF を mock 準拠へ張替え→残作業4点（磨き3点・キー録画 modal・診断分離・ui --resident）実装。
- commit: fa9c6a2 / c13c2c9 / 4a256aa（張替え本体はその前段 commit 群）
- 検証: Desktop.Tests 58・Host.Tests 38・Architecture.Tests 4 全 green（統括再走で確認）。禁止語 grep 通過（画面表示文字列に token/capability/assoc/revision/compile/identity/Supported/Experimental なし・「既定」→「共通設定」）。実走 `ui --duration-ms` exit 0（空DB/サンプルDB）、`ui --resident` device 0台 exit 0・二重起動明示エラー。統括の目視（PowerShell CopyFromScreen キャプチャ）で mock 一致・磨き3点の解消を確認。オーナー評価「良いと思う」（方向承認）。
- 実装中発見の既存 crash 欠陥（SelectionChanged 再入→StackOverflow）は最小修正で同梱。
- 未実測の持ち越し（t10/t11 へ）: 実機接続下 `--resident` 保存→即時反映、modal 対話操作の目視、NIKKE launcher→本体遷移、Unknown 実トリガー。
- オーナー裁定: 装飾・実画像は機能完了後の磨きフェーズ（Phase 3 外）。
