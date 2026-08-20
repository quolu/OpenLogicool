# t03-fixture-recognizer 証跡

## 作成物

- `FixtureFrameRecognizer` を製品 `IFrameRecognizer` として追加した。登録済みの fixture／自前 window source に対して、幅・高さ・pixel format・BGRA8 画素の SHA-256 が完全一致する rule だけを認識する。
- 一致しない画素は候補なしの校正済み結果として返し、既存 `LiveObservationSource` が `Unknown` に正規化する。未校正 rule は `Unknown`、複数候補 rule は `Ambiguous` のままで、Known へ丸めない。
- 未登録 source、画素なし、空画素は契約外として明示エラーにした。別 recognizer や capture backend への自動切替はない。
- WGC の静止 window は新 frame を供給しないことが正常であるため、frame 未到着で recognizer を呼ばず、capture fault や `Unknown` を合成しないことを contract に明記した。

## 最終試験

`dotnet test tests/OpenLogicool.Perception.Tests/OpenLogicool.Perception.Tests.csproj --nologo --logger "console;verbosity=normal"`

- 結果: 16/16 passed、0 failed。
- 追加確認: exact calibrated fixture の `Known`、未校正の `Unknown`、複数候補の `Ambiguous`、許可済み source の未知画素の `Unknown`、未登録 source／画素なしの明示エラー、rule 重複拒否。
- `git diff --check` は出力なし。

## 非目標

実ゲーム一般への対応、未知画素からの推論、学習、閾値調整は実装していない。
