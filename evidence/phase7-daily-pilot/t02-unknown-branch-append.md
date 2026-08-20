# t02-unknown-branch-append 証跡

## 実施

- `UnknownBranchAppend.Append` を追加した。既存の Verified `PlaybookVersion` を変更せず、未知 branch の node と edge を ParentVersionId 付きの新 Version へだけ追記する。
- 未知 branch は追加 node を終点にし、空でない branch condition を持つよう制約した。
- Version と graph の検証は既存 `PlaybookCorrection`／`PlaybookMaterializer` に委ね、規則を複製していない。

## 検証

Windows native で次を実行し、exit 0 を確認した。

```powershell
dotnet test tests/OpenLogicool.Playbooks.Tests/OpenLogicool.Playbooks.Tests.csproj --no-restore --artifacts-path C:\Users\kite_\AppData\Local\Temp\openlogicool-phase7-t02-artifacts-54228 --filter "FullyQualifiedName~UnknownBranchAppendTests" --logger "console;verbosity=normal"
```

focused test 2件で、旧 verified Version のシリアライズ不変、新 Version だけへの未知 node/edge 追記、終点不一致 branch の拒否を確認した。

## 変更ファイル

- `src/OpenLogicool.Playbooks/UnknownBranchAppend.cs`
- `tests/OpenLogicool.Playbooks.Tests/UnknownBranchAppendTests.cs`
- `docs/contracts/unknown-branch-append.md`
- `evidence/phase7-daily-pilot/t02-unknown-branch-append.md`
