# t03-alt-backends 証跡

## 成果

CAP-004 の選択契約を `docs/contracts/capture-alt-backends.md` に追加した。Desktop Duplication と可視 desktop 領域（GDI BitBlt）は probe の frame 取得成功だけでは製品 backend に採用せず、要求時は理由付きで非対応と表示する。WGC window の fault、最小化、停止、非対応条件で別 backend へ自動切替しない。

変更は契約文書と本証跡のみであり、capture 実装、contract 型、試験 project は変更していない。

## 最終確認

- `rg --files src/OpenLogicool.Capture | rg "(Duplication|BitBlt|Gdi)"`
  - 結果: 該当する製品 backend 実装なし。非採用の決定と一致する。
- `git -C <t03-worktree> diff --cached --check`
  - 結果: 出力なし。空白エラーなし。

実行試験は行っていない。変更対象は実行コードではなく、既存 probe の確認済み事実と非採用の利用者表示を固定する契約文書だけである。
