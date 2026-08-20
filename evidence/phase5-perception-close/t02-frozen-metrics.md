# t02-frozen-metrics

`FrozenMetricRunner` は `AcceptanceCorpus` だけを受け、各 artifact の fixture frame を `FixtureFrameRecognizer` と `LiveObservationSource` へ通した実測結果から Known 誤判定、Unknown→Known、再開不可 case の dispatch false-positive を集計する。3指標すべて 0 件だけを合格とし、training corpus は公開 API に含めない。

最終確認:

- `dotnet test tests/OpenLogicool.Conformance.Tests/OpenLogicool.Conformance.Tests.csproj --filter "FullyQualifiedName~FrozenMetricRunnerTests"` — 2/2 green。acceptance fixture 2件の実測は Known誤判定0、Unknown→Known0、success false-positive0。
- `dotnet test tests/OpenLogicool.Conformance.Tests/OpenLogicool.Conformance.Tests.csproj` — 24/24 green。
- `git diff --check` — 空白エラーなし。

実 game frame の収集・評価は未実施。fixture／acceptance corpus の固定評価だけが本 ToDo の範囲である。
