# t06 — Input Studio isolation

## 実装

- `InputStudioIsolation` を Host の pure contract として追加した。
- AI・network・capture fault を Game Operator の degradation として明示し、Input Studio の mapping 編集、profile 保存、mapping 実行は維持する。
- 契約は障害分類だけを行い、既存の fast path、watchdog、dispatch、設定保存、AI／network／capture 実装を呼び出しも再実装もしない。

## focused verification

Windows native の worktree 内で実行:

```powershell
dotnet test tests/OpenLogicool.Host.Tests/OpenLogicool.Host.Tests.csproj --no-restore --filter FullyQualifiedName~InputStudioIsolationTests
```

結果: exit 0（focused green）。確認内容:

- 各単独 fault で Input Studio の3操作がすべて利用可能である。
- 同時 fault は隠さず、Game Operator degradation として全 dependency を列挙する。
- 未知 dependency は黙って隔離しないで拒否する。
