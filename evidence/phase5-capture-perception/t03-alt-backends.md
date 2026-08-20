# t03-alt-backends 証跡

## 成果

CAP-004 の選択契約を `docs/contracts/capture-alt-backends.md` に追加した。Desktop Duplication と可視 desktop 領域（GDI BitBlt）は probe の frame 取得成功だけでは製品 backend に採用せず、要求時は理由付きで非対応と表示する。WGC window の fault、最小化、停止、非対応条件で別 backend へ自動切替しない。

matrix の両 backend 行も「t03 の採否待ち」から「この Phase では製品 backend に採用していません」へ更新した。機械可読の `ProbedOnly` 応答と契約文書の非採用判断を一致させ、両 backend の理由を確認する focused test を追加した。

## 最終確認

- `rg --files src/OpenLogicool.Capture | rg "(Duplication|BitBlt|Gdi)"`
  - 結果: 該当する製品 backend 実装なし。非採用の決定と一致する。
- `dotnet test tests/OpenLogicool.Capture.Matrix.Tests/OpenLogicool.Capture.Matrix.Tests.csproj --nologo --logger "console;verbosity=normal"`
  - 結果: Desktop Duplication と GDI BitBlt の確定した非採用理由を含め、6/6 passed、0 failed。
- `git -C <t03-worktree> diff --check`
  - 結果: 出力なし。空白エラーなし。

変更は matrix、focused test、契約文書、証跡に限定した。capture backend 実装は追加していない。
