# Bot通知のキュー受付と作業中への差し込み

取得日: 2026-10-09。確度: 作業中への差し込みは確認済み。変更後の待機中の遅延は未確認。

## 一次資料

- [OpenAI公式のQueueとSteerの説明](https://developers.openai.com/blog/mastering-codex-remote-for-engineering#2-learn-the-difference-between-queue-and-steer): 作業中のQueueは現在の応答が終わるまで待ち、Steerは作業中の応答へ入る。
- [aiterm-steer-deliveryの公式配布物](https://www.npmjs.com/package/aiterm-steer-delivery): 0.3.0で、製品自身のCodex hookを登録せずAitermの親配送へ依頼する公開APIが追加された。配布物のCHANGELOGと`aiterm-provider.js`を確認した。
- [Aitermの正規配達口](https://github.com/kitepon/aiterm-mcp): `aiterm-parent-delivery codex verify|submit|state`。verifyの`steer`は設定、submitは受付、stateの`hook: emitted`と`turn_id`は同じターンへの差し込みを示す。

## 今回の実測

OpenLogicoolの旧アダプターは自製品profileで`aiterm-steer-delivery codex submit`を呼んでいたが、そのprofileの差し込みhookは設定されていなかった。ボーナス通知の受付18:07:53から会話開始18:38:16まで約30分22秒を観測した。待機中のこの遅れの全原因は未解明であり、作業中の未差し込みと同一とは断定しない。

導入済みのAiterm共通配達口は`steer: enabled`だった。同じ作業ターンで1回送信すると、tool callの返りと同時に検証通知が届き、stateでも`hook: emitted`・現在の`turn_id`・`queued: false`を確認した。

OpenLogicoolのアダプターを正規配達口へ接続し、宛先検証と差し込み有効の確認を行うようにした。設定が無効ならキューだけへ切り替えず明示エラーにする。Aitermの設定や私有状態へ直接書かず、送信結果不明も自動再送しない。

変更後の待機中の遅延は、次の自然なBot通知で受付時刻と会話の開始時刻を照合する。詳細と実機記録は[受入記録](../../evidence/bot-bonus-attack11-20261009/acceptance.md)を参照する。
