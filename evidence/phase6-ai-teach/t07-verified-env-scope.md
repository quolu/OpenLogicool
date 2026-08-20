# t07-verified-env-scope 証跡

## 実施

- `VerifiedEnvScope` を Playbooks の pure value として追加した。Verified の根拠は environment ID の ordinal 完全一致時だけ適用できる。
- GameLab scope は同じ GameLab scenario には適用でき、異なる実 game scope には適用できないことを focused test で固定した。
- input、capture、provider、永続化への参照や実 game への昇格経路は追加していない。

## 検証

Windows native で次を実行し、exit 0 を確認した。

```powershell
dotnet test tests/OpenLogicool.Playbooks.Tests/OpenLogicool.Playbooks.Tests.csproj --no-restore --artifacts-path C:\Users\kite_\AppData\Local\Temp\openlogicool-phase6-t07-artifacts-54228 --filter "FullyQualifiedName~VerifiedEnvScopeTests" --logger "console;verbosity=normal"
```

対象の focused test は 2 件である。

## 変更ファイル

- `src/OpenLogicool.Playbooks/VerifiedEnvScope.cs`
- `tests/OpenLogicool.Playbooks.Tests/VerifiedEnvScopeTests.cs`
- `docs/contracts/verified-env-scope.md`
- `evidence/phase6-ai-teach/t07-verified-env-scope.md`
