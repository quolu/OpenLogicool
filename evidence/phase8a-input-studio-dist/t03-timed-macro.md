# t03 Timed macro 証跡

## 実施

- delay、repeat while held、toggle、有限回 repeat を `TimedMacroState` の明示状態にした。
- emission は一回で完結する tap action とし、保持 output を作らない。
- `Stop()` 後に新しい action を返さず、`Resume()` 後だけ次の physical activation を許可する。
- profile 適用前 validator で timed macro と通常 output の同一 control/layer 混在を拒否した。
- 既存の有限 `Tap:` sequence を timed macro の outputs に混ぜず、再実装しない。

## focused verification

| command | result |
| --- | --- |
| `dotnet test tests/OpenLogicool.Input.Tests/OpenLogicool.Input.Tests.csproj --nologo --filter 'FullyQualifiedName~TimedMacroTests' --logger 'console;verbosity=minimal'` | 6/6 green |
