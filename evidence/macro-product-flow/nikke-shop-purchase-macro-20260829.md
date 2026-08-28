# NIKKE shop purchase macro（2026-08-29）

## 目的

利用者の一言goalから、OpenLogicoolがChatGPT subscription Codexを呼び、NIKKEの3ショップで明示許可された商品だけを購入してLearning Routeへ保存する。

## Game Policy境界

- 一般ショップは価格が無料または0の商品だけを購入。
- 商品更新は費用が無料または0と確認できる場合に1回だけ実行。
- ボディラベルショップのSALE商品は、利用者が明示許可したボディラベル通貨で購入。
- キャッシュショップは赤ポチ付きかつ無料または0の商品だけを購入。
- 有償通貨、ジュエル、現金、非無料商品、募集、戦闘は禁止。
- 価格または確認画面が曖昧なら購入せず停止。

## 実走結果

- Product Host operation: `create`。
- 固定game profile: `nikke`。
- 監視役: OpenLogicoolがCodex App Serverで起動したCodex。
- game input: Nano Serial HIDのみ。
- Computer Use／SendInput／fallback／自動retry: 0。
- terminal: `Completed`。
- AI call: 1。
- step: 20。
- route revision: 21。

購入結果:

1. 一般ショップで価格0のクレジット60,000を購入。
2. 確認画面で更新費用0を確認し、商品更新を1回実行。
3. 更新後の一般ショップで価格0のクレジット60,000を購入。
4. ボディラベルショップで40% SALEの支援型共用コンソールを最大数量10個購入。ボディラベル2,400を消費。
5. キャッシュショップで赤ポチ付きデイリー無料パックを購入。
6. キャッシュショップで赤ポチ付きウィークリー無料パックを購入。
7. デイリー／ウィークリー無料商品がSOLD OUTとなり、該当する赤ポチが消えたことを確認。

禁止した有償通貨、ジュエル、現金、非無料商品、募集、戦闘は実行していない。

## 証拠

- 製品出力: `probe-output/codex-monitored-shop-purchases-20260829.json`。
- game agent thread: 既存NIKKE durable threadをresume。
- Nano firmware: 1.1.1、run中のtimeout 0。

## 判定

- 一言goalからの複数shop逐次購入: **確認済み**。
- 価格0／SALE／赤ポチ無料の条件分岐: **確認済み**。
- 明示許可外の支払い・操作0: **確認済み**。
- 同routeのAI 0再現: 日次／週次購入済み商品のため、次回reset前は未実施。
