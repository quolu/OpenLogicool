# t06 Live observation 証跡

## 実施

- `LiveObservationSource` が `CapturedFrame` 一つの入口から `Known`、`Ambiguous`、`Unknown`、`Unavailable` を合成する。recorded と live は同じ frame contract を渡すだけであり、別の判定経路は持たない。
- Observation は frame source／backend／sequence／時刻／transform revision／age、recognizer version、candidate confidence、evidence region を保持する。未校正、候補なしは `Unknown`、複数候補は `Ambiguous` とし、Known へ丸めない。契約外の candidate は結果を捏造せず明示エラーにする。
- 自動実行を許す口は `Known` のみ。Perception の型・実装に Attempt ID はない。
- `ObservationStabilityWindow` は同一 source／backend／transform revision の同一 Known state が monotonic 時間で指定窓を満たす時だけ true を返す。非 Known、state・座標系変更、時刻逆行で窓をリセットする。操作前後の系列照合は Attempt owner の Playbooks が担う。

## 根拠水準

- **確認済み**: pure test で4状態、frame／recognizer／evidence の伝搬、Known 以外の自動実行拒否、契約外 recognizer 出力の明示拒否、安定窓と reset を確認した。既存の Observation contract conformance は 18/18 green。
- **強い推定**: recorded と live は同一 `CapturedFrame` 入力・同一実装へ正規化されるため、入力種別による状態判定差はない。
- **未確認**: 実 game の recognizer calibration、実 game の操作前後系列での成功判定。未確認事項を Supported と扱わない。

## focused verification

| コマンド | 結果 |
| --- | --- |
| `dotnet test tests/OpenLogicool.Perception.Tests/OpenLogicool.Perception.Tests.csproj --no-restore` | 9/9 green |
| `dotnet test tests/OpenLogicool.Conformance.Tests/OpenLogicool.Conformance.Tests.csproj` | 18/18 green |
