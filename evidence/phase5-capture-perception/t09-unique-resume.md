# t09 Unique resume 証跡

## 実施

- t06 の Observation を `StateMatcher` と Phase 4 の `ResumeGate` へ供給する dispatch 前 gate を実装した。
- app、target window、capture source、input target を明示比較し、不一致時は `DispatchAllowed=false` と理由を返す。InputEmitter を参照しないため、拒否時に入力 API へ進む経路はない。
- run closed、version drift、manual intervention 後の再観測要求は既存 `ResumeGate`／`ResumeReadiness` の判定をそのまま使う。

## 根拠水準

- **確認済み**: pure test で UniqueMatch のみ許可し、Ambiguous／Unknown／Unavailable、stale、安定窓未達、window／capture source／input target 不一致、manual intervention 後の未記録 observation を拒否する。
- **未確認**: 実 game の window と capture source と input target を同時照合した dispatch。未確認を Supported と扱わない。

## focused verification

| コマンド | 結果 |
| --- | --- |
| `dotnet test tests/OpenLogicool.Playbooks.Tests/OpenLogicool.Playbooks.Tests.csproj` | 106/106 green |
