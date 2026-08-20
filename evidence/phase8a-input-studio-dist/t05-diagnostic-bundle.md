# t05 Diagnostic bundle

## 実装

- `DiagnosticBundle` を追加した。preview は filesystem に書かず、既定 bundle の固定 manifest を返す。
- create は preview 済み manifest 1件だけをローカルへ書き、delete はその bundle 1件だけを削除する。
- 既存 `diagnostics` CLI の device／DB／profile／identity 収集は再実装しない。既定 bundle は screen、OCR、prompt、journal 本文、crash dump、secret、個人データを探索・収集・保存しない。

## 検証

`dotnet test tests/OpenLogicool.Host.Tests/OpenLogicool.Host.Tests.csproj --no-restore --filter FullyQualifiedName~DiagnosticBundleTests --logger console;verbosity=normal`

- 2 tests passed / exit 0
- preview が未書込みで固定 manifest だけを示すことを確認した。
- create の内容が preview と一致し、delete 後にその bundle だけが消えることを確認した。
