# Capture support matrix contract

CAP-004／005 の matrix は、backend、target、条件ごとに根拠水準と製品 route の可否を別に記録する。

- 根拠水準は `Confirmed`、`StrongInference`、`Unverified`、`Unsupported` の4値である。`Unverified` を Supported と表示しない。
- route は `Available`、`ProbedOnly`、`Unavailable` の3値である。probe が確認済みでも、製品 backend として採用していなければ `ProbedOnly` とする。
- `CaptureCapabilityMatrix.Select` は指定された backend の行だけを返す。別 backend への fallback はしない。
- 行が無い場合も `Unverified`／`Unavailable` と明示し、失敗理由を返す。

## Reference machine matrix

| backend | target | condition | 根拠 | route | 理由 |
| --- | --- | --- | --- | --- | --- |
| Windows Graphics Capture | window | windowed | Confirmed | Available | Windows 11 reference machine のメモ帳 window で製品 frame を確認済み |
| Windows Graphics Capture | window | minimized | Unsupported | Unavailable | item は有効でも frame 供給が停止する。restore が必要 |
| Windows Graphics Capture | window | borderless / fullscreen / non-default DPI / HDR / multi-monitor / occluded | Unverified | Unavailable | 条件ごとの live 実測が未了 |
| Desktop Duplication | display | windowed probe | Confirmed | ProbedOnly | probe は成立、製品化は t03 の採否待ち |
| GDI BitBlt | display | windowed probe | Confirmed | ProbedOnly | probe は成立、製品化は t03 の採否待ち |

静止した WGC window に frame が届かないことは damage-driven 供給による正常状態であり、この matrix の fault や最小化判定には使わない。最小化は item size の変化と frame 停止の組で区別する。
