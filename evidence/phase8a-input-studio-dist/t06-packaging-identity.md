# t06 Package identity と配布 layout

## 実装

- unpackaged 開発配布の identity と layout を `OpenLogicool.Packaging` に定義した。
- Host と Watchdog を同じ application 配下の必須ファイルとして示す。
- MSIX／Sparse Package／MSI、autostart、update manifest は EXP-DIST-01 の clean VM 実測前のため、未決定・未確認として固定した。
- install/update が device write を開始しないことを contract にした。

## 検証

`dotnet test tests/OpenLogicool.Packaging.Tests/OpenLogicool.Packaging.Tests.csproj --no-restore --filter FullyQualifiedName~PackageIdentityTests --logger console;verbosity=normal`

- 2 tests passed / exit 0
- unpackaged layout が公開方式の採択を名乗らないこと、autostart/update が未確認であること、device write を開始しないことを確認した。
