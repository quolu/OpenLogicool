# t08 Corpus split 証跡

- development、calibration、acceptance を型で分離し、training API は acceptance を表現できない。
- **確認済み**: focused conformance test で training から acceptance が不在であること、artifact の校正／acceptance 再利用を拒否することを確認する。
- **未確認**: 実 game 探索 frame の収集。artifact は出典を必須とし、未確認を学習済み／一般対応と表示しない。

| コマンド | 結果 |
| --- | --- |
| `dotnet test tests/OpenLogicool.Conformance.Tests/OpenLogicool.Conformance.Tests.csproj --no-restore` | 20/20 green |
