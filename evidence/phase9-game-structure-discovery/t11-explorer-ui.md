# t11 Explorer UI 受入記録

日付: 2026-08-24

## 結論

t11 のコード受入条件は成立した。Game Operator に「構造探索」面を追加し、保存済みStructure Event Storeのprojectionと実行中探索control portをHostで合成した。DesktopからSQLite、探索runtime、Inputへ直接依存する経路はない。

## 成立した表示

- 構造版
- 既知状態／新しく見つけた候補
- 次に調べる候補
- 実行待ちの一手
- 危険度／承認理由
- 残り操作回数／経過時間／推論時間
- 確認できる戻り道
- 停止理由
- 候補／再現済み／確認済み／非対応の件数
- 各画面状態の根拠4値表示（未確認／強い推定／確認済み／非対応）

表示語彙は日本語へ変換し、candidate／replayed／verified等の内部語を画面へ露出させていない。

## 成立した操作

- 一時停止
- 一手だけ進める
- 探索を終了
- 既存画面状態の名前訂正

pause／step／abandonは`IHostExplorerRuntimeControl`を通して選択中のgame／environmentと一致する実行中探索だけへ配送する。実行中でないscopeでは明示エラーとなり、成功表示へ丸めない。訂正は`StructureCorrectionController`だけがUser actorの`CorrectionApplied` eventをappendし、stable state IDと検証段階を変えずに表示名だけを訂正する。

## focused test

- `OpenLogicool.Desktop.Tests`: 88件 green
- `OpenLogicool.Host.Tests`: 131件 green
- `OpenLogicool.Exploration.Tests`: 16件 green
- `OpenLogicool.Architecture.Tests`: 8件 green
- `dotnet build OpenLogicool.sln --no-restore --nologo`: 警告0・エラー0
- `git diff --check`: whitespace errorなし

Host testは実SQLiteへcandidate／replayed nodeを保存し、frontier・根拠表示・risk・承認理由・budget・復帰経路を同じpublic intentから取得した。pause→step→abandonの配送、scope不一致拒否、訂正後のrevision更新・User actor event・identity／検証段階保持も確認した。

## Windows実画面の扱い

開発版Input Studioの起動とAccessibility tree取得までは成立した。前面のNIKKE保護ウィンドウによりComputer UseがInput Studioをactivateできず、Game Operatorタブを開く入力は送出されなかった。同じ入力を再送せず停止したため、t11単独の画面目視は未確認としてt12実機手番へ持ち越す。これはコード受入を成功扱いする根拠には含めていない。
