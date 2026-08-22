# t00 G600 dogfood 欠陥修理の証跡

取得日: 2026-08-23

## 原因

`G600OnboardPlanner` は未割当 control を通常層・G-Shift 層とも明示的な `00 00 00` cell にしていた。`G600OnboardImage.Build` は baseline を複製してからその cell を上書きするため、G2〜G5 に残っていた右クリック・中クリック等の出荷割当まで無動作に置換していた。

旧実装 commit `5042a8e` に「未割当 G2〜G5 の cell が存在しない」ことを要求する focused test を追加して実行し、G2 の `G600OnboardCell { MouseCode = 0, Modifiers = 0, HidKey = 0 }` から一致して失敗することを確認した（失敗1、合格0）。

## 修理

- 明示割当は G2〜G20 の従来どおり cell 化する。
- 未割当 G2〜G5 は cell を作らず、baseline の両層を保持する。
- 未割当 G6〜G20 は両層とも従来どおり `00 00 00` にする。
- 反映されない場合の G600 USB 挿し直し案内を apply 成功メッセージへ追加する。
- device write は既存の `G600EvidenceWrite` 経路を変更せず、fresh open・settle・handle 非再利用・fresh verify・規定試行の契約を維持する。

## 検証

`dotnet test tests/OpenLogicool.Host.Tests/OpenLogicool.Host.Tests.csproj --logger "console;verbosity=minimal"`

- 失敗: 0
- 合格: 85
- スキップ: 0
- 未割当 G2〜G5 の baseline 保持、明示 G2/G5 割当、未割当 G6〜G20 の無動作化、G6 selector の固定、USB 挿し直し案内を含む。

未追跡の `probe-output/ui-test-scenario-20260822-094519-943.json` は対象外であり、変更・追加していない。
