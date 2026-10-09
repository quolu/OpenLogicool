# firmware 1.1.4の導入準備

2026-10-10、Windows native。利用者から未導入修正を実施する指示を受けた。

未導入なのは、無入力時のlease失効と1200-baud close中のwatchdog給餌を修理したfirmware 1.1.4。PCの受信済みACK読取り修理・通信診断・読取り専用captureは導入済みである。

現在の対象はUSB本体`USB\VID_1B4F&PID_9206\HIDFG`と、その同一ContainerIdのCDC COM3。runtime PID9206だけが存在し、書込みモードのPID9205は存在しなかった。直前の正規診断で実機版1.1.3を確認済み。

正規`build-serial-hid.ps1`で1.1.4のbuildに成功した。sketch 6548bytes、global 263bytes。HEXとSHA-256、既定ブランチへpush済みのsource commitを保存した。firmwareソースは前回のnative試験後に変更していないため、同じgreen testは再実行していない。

旧1.1.3による標準1200-baud resetの阻害と前回upload失敗は既存の実測に残っている。新たな条件なしで同じ失敗試行は繰り返していない。メーカーの正規手順ではresetの2回操作でbootloaderへ入る。参照: https://learn.sparkfun.com/tutorials/pro-micro--fio-v3-hookup-guide/troubleshooting-and-faq?raw=true

前回は利用者が操作箇所を把握できず手動reset依頼を取り下げた。今回はUSBにつないだ基板の写真を会話へ送ってもらい、ボタン・接点を実機で特定してから待機を開始する。Approval Box K-U5885Yへ申請済み。写真待ちの間、Botの接続を回収したり、書込み待機や基板操作を開始したりしていない。

次は操作箇所の確認、正規flash入口の待機、同じ物理接続のbootloader捕捉、verify付きupload、READY 1.1.4・全解放・idle200ms/1000msの実機確認、Bot再開と実進行の順に行う。まだ実機へ書き込めていない。
