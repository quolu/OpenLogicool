# 既知の商人メニュー終了をBotへ保存

2026-10-09、Windows native。オーナーの「過去突破しているのになぜ詰まる」に対し、[当時の実測](../../docs/sessions/2026-10-08-mabinogi-key-assist.md)を確認した。過去は操作者がNanoでスキップ後、Escの会話終了を実行していた。Botの進行設定へ終了規則を残しておらず、商人メニューでも会話Spaceを送って停滞したのが原因。

## 最終変更

下部の商店・世間話・会話終了・ESCが揃ったサービスメニューでは、`merchant-exit`でEscを1回送る。通常会話のSpaceより優先する。購入・修理・くじは選ばない。実画面fixtureでEsc選択、終了表示がない場合の不一致、Host507件成功を確認した。

## 導入後の実機

実行`20261009-073448-310-2aa00b0a`で、5,914msに`merchant-exit`のEscをNanoSerialHidから送出。8,355msに次の会話Space、14,627msにコンパスSpaceを送った。商人メニューを閉じた後の進行と、HP100％・250ms前後の回復監視の継続を確認した。

`before.json`、`restart.json`、`after.json`、`live-events.jsonl`に証拠を保存。導入Host DLLとRelease成果物の一致は`build-hashes.json`。録画・マクロ・割当・回復設定は変更しない。

同じ既知操作への再確認K-K7R4YTは、取消時点で既に取り下げ済みだった。再申請せず、Botと5分ごとの監視を継続する。
