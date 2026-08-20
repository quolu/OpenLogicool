# LGS restore / rollback 契約

## cancel

LGS XML dry-run の cancel は、候補数を結果として返すだけで apply を開始しない。元の LGS profile、device state、XML は変更しない。

## G600 restore

G600 baseline の restore は `G600LeftoverSession.Restore()` をそのまま呼ぶ。write、fresh readback、retry、baseline 保持の作法を再実装しない。

`Restore` または `AlreadyRestored` が hard failure なしで返った時だけ `G600BaselineRestored` とする。device 不在、共存ソフト、baseline 不在、byte 不一致は成功扱いにしない。
