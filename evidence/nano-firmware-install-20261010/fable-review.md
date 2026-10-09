# Fableの反証相談と親の受入

2026-10-10。Aiterm session `nano-firmware-fable`、delivery `57d3e265-047a-4af4-b89b-bc8a754c696e`、outcome done。Claude Code／Fable 5.1／high。子はread-only依頼を守り、ファイル・Bot・serial・deviceを変更していないと報告した。

Fableは固定Arduino core 1.8.8、旧1.1.3の退避source、SparkFun Caterina bootloader、8/29の標準upload成功記録を照合した。親もCDC.cppの1200-baud close時のboot key＋watchdog120ms、取消時の鍵解除・watchdog復元、Caterina.cのEXTRF二度でbootloaderへ進む分岐、PORFでsketchへ直行する分岐、旧版の給餌追加前後の導入記録を読んで受け入れた。

現在の正規入口では、旧1.1.3がcoreのwatchdog resetを給餌で妨げるため、PC側の標準1200-baud操作で書込みモードへ入れない。PnP再起動による通信復旧はMCUのbootloaderへの移行を意味しない。写真や機種確認は更新の前提にしない。

正規flash scriptの待機表示だけを、実際のRST/GND接続操作へ修正した。PowerShell構文確認成功。firmwareのコード変更・green testの再実行は行っていない。

ゲームが報酬画面にいることを確認し、Botのhandled stopとapp closeで接続を回収した。Aitermの正規pwsh PTY `nano-firmware-upload`で同じNanoを対象に`flash-serial-hid.ps1 -WaitForReset`を開始し、build完了と待機表示を確認した。K-S426G8で刻印RST/GNDの2点だけを一瞬つなぎ、0.75秒以内にもう一度つなぐ操作を依頼した。待機は30分で、upload完了はまだ未確認。

受入へ「1.1.4導入後、標準uploadを同じHEXで再実行して、手操作なしの更新成功を確認」を追加した。続いてREADY・idle200ms/1000ms・Bot再開と実進行を確認する。子の助言だけで実機更新済みとは扱わない。
