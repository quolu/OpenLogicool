# Botの詰まり通知をClaudeの会話へ届ける設計の要点（未検証）

状態: 調査を任せたエージェントの草案を、担当AIが要点にまとめたもの。根拠の確認と別のAIの反証はまだ通していない。採用・実装の前に両方を行う。

- 対象の決裁: K-X9E7Z7（通知の宛先対応と保存先の統一を両方やる。添え書き: 判定のたびに両方のAIを起こさない）。
- 保存先の統一は完了。残りはこの「通知の宛先対応」。
- 採用する時は、ADRをaiterm-mcpの `docs/adr` へ置く（番号は0109を仮置き）。

## 確認できた事実

- Aitermの公開入口 `aiterm-parent-delivery` はCodexの親だけが対象（ADR 0098）。
- 共通部品 aiterm-steer-delivery 0.4.1 には、同じ会話へ何通も送れる受け口（channel）と、会話の受信箱へ1通送る処理（inbox）がある。
- Claudeデスクトップアプリの Code タブの会話は、決裁箱のchannelで、作業中にも待機中にも起こされた（2026-10-10、同じ会話へ4通）。

## 草案の結論（未検証）

1. **土台は channel＋公式hook（asyncRewake）。inbox は使わない。** inboxは届いた事を確定できず、会話のtokenを保存する事になり、保留・破棄・同文の間引きが送り手に見えない。
2. **会話は、Aitermの新しい道具 `parent_channel_open` を1回呼んで受け口を開く。** 返った `channel_id` を製品の登録命令へ渡す。その後は何も呼ばなくてよい。
3. **待機の張り直しは、AitermのStopのhookが行う。** 会話に、通知のたびの手順を求めない。
4. **命令へ `claude verify|submit|state|close --channel <uuid>` を足す。** 設定・受付・実際に届いた事を分けて返す。失敗は型のある符号で断り、ほかの届け方へ切り替えない。
5. **宛先を1つに保つのは製品の務め。** 担当が替わった時、前のchannelを閉じる。

## 作業項目（依存の順）

- 共通部品（0.5.0）: channelの状態を読む関数、未取得の同じ配送idを断る処理。ソースのrepoはこの端末に無い。
- Aiterm（0.59.0）: profileへchannelsとStopのhook、道具 `parent_channel_open`、命令の `claude` 系、試験、ADR、`aiterm-setup` の登録変更。
- OpenLogicool: 担当登録に宛先の種類とchannelを足す。`assistant attach --claude-channel <uuid>` は、`verify` が返すsessionと会話のshellの `CLAUDE_CODE_SESSION_ID` が同じ時だけ登録する。`claim`・`resolve` の呼び出し元は担当の種類で読む。

## 残る危険と未確認点

- Stopは、利用者が番を中断した時とAPIの誤りで終わった時には走らない。その間に届いた通知は、次の番の終わりまで受信箱に残る。
- アプリを再起動すると会話のprocessが替わり、受け口は開き直しが要る。会話は自分では起きないので、人が会話を開くまで通知は届かない。
- matcherなしのStopのhookは、Aitermを入れた端末の全てのClaudeの会話で、番の終わりごとに1回起きる。
- Windowsでは待機のhookが5秒ごとにprocessの一覧を取り直す（共通部品の作り）。
- Aiterm自身のStopのhookがこの環境で起きる事、setupより前から動いている会話が新しいhookを読む事は未確認。
- ADR 0058の「Claude Desktop等の別clientに推測適用しない」との関係。Code タブは実測で成立、チャットタブは対象外、と線を引く案。

## 設計上の分かれ目

| 分かれ目 | 草案の推奨 |
|---|---|
| 土台 | channel＋asyncRewake |
| 会話の手数 | 道具1回＋`assistant attach` 1回（会話ごとに1度） |
| 24時間の期限 | 短い文で1日1回起こして張り直す |
| Stopのhook | Aitermの利用者全員へ常に登録する |
| アプリ再起動の後 | 会話が開き直して登録し直す |
