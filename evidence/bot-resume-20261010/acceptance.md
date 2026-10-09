# ゲーム復帰と入力を伴わない画面取得

2026-10-10、Windows native。

## 修理した製品欠陥

`game-index capture`が画像取得前にDBを開き、ゲームを前面化し、Nanoへ接続してALL_UPを送っていた。画像の読取りが入力機器の状態と接続の排他に依存していた。

修理前は、起動中のOpenLogicoolを対象に、存在しない出力機器を指定すると「SparkFun Pro Microが見つかりません」で終了コード2になり、PNGが作られなかった。`capture-before.txt`を参照。

画像取得をNano／DBの初期化より前へ分離した。共通の結果保存処理だけを抽出し、入力操作の経路は変更していない。修理後は同じ対象と存在しない出力機器の指定でPNGを保存できた。DB指定も不要になった。`capture-after.json`を参照。Hostの560テスト成功、build警告・エラー0。

mainへpush後、正規開発版installerで導入してアプリを通常起動した。導入版でも同じ条件で取得成功。Host DLLのSHA-256はRelease成果物と一致した。`installed-capture.json`、`installed-hashes.json`、`install.log`を参照。最小化中はWGCのframeが届かず明示エラーになった。画面を読むためにウィンドウを勝手に復元・前面化する処理は追加していない。

## ゲームの現在地

Nanoの正規診断はREADY 1.1.3・全解放21msで成功した。firmwareの修正版は未導入。

前のターンでNanoからEscを送った直後、Computer Useが物理Escによる停止を報告した。Nano入力と停止報告の時間的対応は確認できるが、ツール内部の入力元判定は未検証で、修理済みとは扱わない。今回は製品のWGC取得とNano操作の正規CLIでゲーム画面と操作結果を確認した。

ゲーム本体を直接起動した試行はERROR I.10121で停止した。終了確認をNanoクリックで閉じ、エラー確認を経て、通常のゲーム終了ボタンで終了した。公式サイトからの起動は旧文書・新規文書で試したが、ゲーム本体の起動は確認できなかった。ページ内の`ReferenceError: $h is not defined`も観測したが、認証エラーや起動不能との因果は未解明。

公式FAQはログインできない場合の問い合わせ先とエラー番号の添付を案内しているが、今回のコードの原因を特定する説明はなかった。参照: https://mabinogimobile.nexon.co.jp/support/faq/detail/3541532

利用者からゲームがログアウト状態との観測があった。普段の入口から起動・ログインして通常画面へ戻れるかをApproval Box K-JANNVFへ確認中。BotはStoppedで、ログイン後の再開と実進行は未確認。録画原本・マクロ・割当は変更していない。
