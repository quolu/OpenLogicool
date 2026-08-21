# t08 Install lifecycle

## 実装

- install、update、rollback、repair、uninstall の lifecycle 契約を `OpenLogicool.Packaging` に追加した。
- 全操作が device write を開始しないことを固定した。
- rollback と uninstall の LGS 復帰は既存 `leftover restore` を要求するだけで、Packaging から device API や restore を再実装しない。

## 検証

`dotnet test tests/OpenLogicool.Packaging.Tests/OpenLogicool.Packaging.Tests.csproj --no-restore --filter FullyQualifiedName~InstallLifecycleTests --logger console;verbosity=normal`

- 2 tests passed / exit 0
- 全 lifecycle 操作の device write 不開始と、rollback/uninstall の restore 要求を確認した。
