# t04-shadow-compare 証跡

## 実施

- `ShadowCompare` を追加し、利用者の semantic action ID と fake planner の `NextActionProposal` を比較する観測口を作成した。
- `VerifiedRunAction` の action ID が ordinal 完全一致する時だけ一致とし、Teach proposal は不一致として返す。
- この口は proposal の取得・schema 検証・比較だけを行う。dispatch、SendInput、承認、Playbook 書換え、本番 provider は持たない。

## 検証

Windows native で次を実行し、exit 0 を確認した。

```powershell
dotnet test tests/OpenLogicool.Playbooks.Tests/OpenLogicool.Playbooks.Tests.csproj --no-restore --artifacts-path C:\Users\kite_\AppData\Local\Temp\openlogicool-phase7-t04-artifacts-54228 --filter "FullyQualifiedName~ShadowCompareTests" --logger "console;verbosity=normal"
```

focused test 2件で、fake planner が1回だけ呼ばれて一致比較を返すこと、Teach proposal を一致扱いにしないことを確認した。

## 変更ファイル

- `src/OpenLogicool.Playbooks/ShadowCompare.cs`
- `tests/OpenLogicool.Playbooks.Tests/ShadowCompareTests.cs`
- `docs/contracts/shadow-compare.md`
- `evidence/phase7-daily-pilot/t04-shadow-compare.md`
