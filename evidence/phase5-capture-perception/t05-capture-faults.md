# t05 Capture faults 証跡

## 実施

- fault を `CaptureFaultKind` の別状態にし、静止の無 frame と混同しない。
- fault、backend、transform revision、stale は `CaptureContinuityGate` で自動入力を停止し、同一 frame identity の再校正だけで解除する。
- WGC の item size 変化と frame pool 再作成は `Resize`、item size が 0 の最小化は `Minimized` として詳細読取口から明示する。別 backend への fallback は実装しない。
- WGC は画素 fingerprint の最終変化 QPC 時刻から `FreshnessMs` を実際に構築する。同じ内容の後続 frame が stale 閾値を超えれば gate が停止する。静止して frame が供給されないだけの場合は freshness を進めず fault にもしない。

## 根拠水準

- **確認済み**: focused test で static 無 frame の許可維持、全 fault の停止、WGC freshness の生成、stale／backend／transform 不連続、再校正解除を確認した。WindowsNative 試験で resize が `Resize` fault を返すことを確認した。
- **未確認**: 実 game の stale、black、遮蔽、最小化、device lost。これらを Supported と表示しない。

## focused verification

| コマンド | 結果 |
| --- | --- |
| `dotnet test tests/OpenLogicool.Capture.Tests/OpenLogicool.Capture.Tests.csproj` | 14/14 green |
| `dotnet test tests/OpenLogicool.Capture.Tests/OpenLogicool.Capture.Tests.csproj --filter "Category=WindowsNative"` | 1/1 green |
