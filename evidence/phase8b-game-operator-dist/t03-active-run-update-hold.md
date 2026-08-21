# t03 active Run update hold — evidence

- task: `phase8b-game-operator-dist/t03-active-run-update-hold`
- scope: pure update-start／resume compatibility contract、focused Packaging test

## 判定

- active Run 中は `HeldForActiveRun` となり、update 開始を許可しない。
- Run 終了後に update を開始できる。
- resume は pin 済み artifact version と installed artifact version が ordinal 完全一致のときだけ許可する。異なる version を互換と推測せず、自動移行もしない。
- `InstallLifecycle` と Playbook／Run の pin 実装は変更していない。

## focused verification

`dotnet test tests/OpenLogicool.Packaging.Tests/OpenLogicool.Packaging.Tests.csproj --filter FullyQualifiedName~ActiveRunUpdateHoldTests`
