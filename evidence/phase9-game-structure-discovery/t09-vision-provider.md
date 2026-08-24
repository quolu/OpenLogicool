# t09 Vision provider 実装・検証記録

取得日時: 2026-08-24
対象: Phase 9A / `t09-vision-provider`
状態: **成立**

## 結論

t05で採用・実機実証したFoundry Localのloopback vision経路を、Game Structure探索契約へ接続する製品providerとして実装した。

- providerは`FoundryLocalVisionClient`だけを使用し、cloud・外部API・別providerへのfallbackを持たない。
- VLM出力は文字labelだけを受理し、同じframeのOCR regionへ一意にgroundできた候補だけを`AffordanceCandidate`へ変換する。
- 完全一致を優先し、一般化した文字列類似度は0.85以上かつ次点との差0.15以上だけを採用する。同一labelが複数の物理regionへ一致する場合は採用しない。
- affordanceはobservation ID、frame sequence、transform revision、target window source、正規化bounds、locator revision、evidence regionへ拘束した。
- 文字region由来のaffordanceに許可するprimitiveは`click`だけである。provider座標や生screen座標をproposalへ持ち込まない。
- 同一scene候補0件は`Novel`、1件は`Known`、複数件は`Ambiguous`として、capture可否とstate同定を混同しない。
- `ExplorationProposal`は`click`がpolicyで許可された場合だけ生成し、Destination／Novel／NoChange／Ambiguous／Unavailable／Fault／OutcomeUnknownを明示する。
- provider失敗・不正schemaはaffordanceとproposalを生成せず、失敗を保持する。fallbackで成功扱いしない。

## 観測可能性

各実行について次を`LocalVisionProviderTelemetry`へ記録する。

- provider ID、loopback endpoint、model ID
- prompt revision、prompt SHA-256
- crop ID、幅、高さ、crop SHA-256
- grounded affordance数、所要時間、request bytes、input/output token数
- 外部AI送信回数0、外部AI API費用0 USD

同じ入力を同じ証拠として扱えるよう、座標keyの文字列化はInvariantCultureへ固定した。

## t05証跡の訂正

t05証跡に記載した日本語fuzzy例が実試験より短く、記載上の文字列では閾値0.85を満たさなかった。実際に試験した`タップして受けける`→`タップして受け取る`へ証跡だけを訂正した。閾値は変更していない。

## focused test

- `OpenLogicool.AI.Tests`: 28件 green
- `OpenLogicool.Conformance.Tests`: 57件 green
- `OpenLogicool.Architecture.Tests`: 7件 green
- `git diff --check`: 違反0（既存の改行変換警告だけ）
- 製品provider内のgame固有語、外部URL、API key、cloud経路: 0件

focused testは、frame-bound affordanceとproposal、Novel／Known／Ambiguous、完全一致と一般化fuzzy、一意でない物理regionの棄却、provider不正応答時のno-fallback、window不一致・frame外regionのdispatch前拒否を確認した。

## 実測境界

Foundry Local本体へのlive loopback、外部送信0、費用0、NIKKE frameのlabel提案はt05で実測済みである。t09は同じclientの決定的なgrounding・contract変換層なので、gameを再操作する通し試験は行っていない。
