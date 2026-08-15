# dotagents Codex native sub-agent gate 互換性・過剰統制レポート

- 作成日: 2026-08-14
- 対象repo: `kitepon-rgb/dotagents`
- 対象commit: `8a90bfb1e80453622d7cc2099c1dd34fcb0517c1`
- 対象branch: `main`（調査時点で`origin/main`追跡、worktree clean）
- 実行環境: Codex Desktop `26.810.4967.0`、WSL2
- 重要度: High（dotagentsのCodex native統括レーンをfail-closedで使用不能にする）
- 影響外: Codex標準のnative sub-agent起動そのもの
- 状態: 再現済み、未修正

## 1. 要約

Codex標準のnative sub-agentは正常に動作している。2026-08-14の実測では、`multi_agent_v1__spawn_agent`から起動した6席すべてが正常に初期化・応答・完了し、指定した`refuter` role、`gpt-5.6-sol`、reasoning effort `high`、role固有developer instructionsも正しく適用された。

一方、dotagentsの`orchestrate`は、実作業を渡す前に独自のrouting smokeと`verify-codex-agent-routing`によるgreen判定を必須としている。この検証経路は旧形式の`agent_path=/root/...`を必須handleとしているが、現在のCodex native sub-agentはUUID形式の`agent_id`を返し、rolloutの`session_meta.payload.agent_path`は`null`である。

そのため、正常に起動した標準sub-agentがdotagents独自gateを構造上通過できず、手順に従うと全席を未使用のまま停止する。

さらに、`refuter.toml`は`sandbox_mode = "read-only"`を宣言しているが、6席すべての実効sandboxは`danger-full-access`だった。検証器はsandbox不一致を既定で警告に留め、routingをgreenにできる設計であり、合成テストもこの不一致を意図的に許容している。

結論は次のとおり。

> Codex標準機能の故障ではない。dotagentsが標準機能の前に置いた旧式の追加gateが正常な利用を阻害し、同時にread-onlyという安全上の主張も実効権限として保証していない。

これは「安全性を上げる制約」ではなく、現在の実装では可用性を失わせながら権限保証も成立させない過剰統制である。

## 2. 発生状況

OpenLogicoolの開発計画をUltraで多視点監査するため、read-onlyの専門監査役を並列起動しようとした。`orchestrate` skillを選択したため、同skillのCodex appendixに従って最初のspawnをrouting smokeだけに限定した。

6席へ渡した内容は「実作業・ファイル編集・調査をせず、`ready`だけ返す」という接続確認だけである。6席すべてが`ready`を返して完了した。

その後、同じ子へ実作業を渡す前の必須手順として`verify-codex-agent-routing refuter <agent-path>`を実行しようとしたが、current native toolは`agent_path`を発行しないため照合不能となった。

実作業は一件もdispatchせず、6席すべてをcloseした。OpenLogicool計画書とdotagents本体に、この試行による変更はない。

## 3. 確認済み事実

### 3.1 Codex標準sub-agentは正常に起動した

6席すべてで次を確認した。

| 項目 | 期待 | 6席の実測 |
|---|---|---|
| terminal status | `completed` | `completed` |
| response | `ready` | `ready` |
| `agent_role` | `refuter` | `refuter` |
| model | `gpt-5.6-sol` | `gpt-5.6-sol` |
| effort | `high` | `high` |
| role developer instructions | applied | applied |
| `agent_path` | dotagentsは絶対pathを要求 | `null` |
| standard handle | UUID agent ID | UUID agent IDをspawn結果が返却 |
| sandbox | roleは`read-only`を宣言 | `danger-full-access` |

実測したagent ID:

```text
019fff40-8d34-7302-8226-cd8d9744752b
019fff40-8dd4-7971-8757-c9ae010aa45b
019fff40-8ee0-7012-992e-116b955e6d8a
019fff40-90a9-7e02-a6eb-ce49ee918029
019fff40-926b-7091-84cf-ad2364127216
019fff40-94cb-74c3-817f-5dddb0f0ba40
```

代表rolloutの要約:

```json
{
  "session_meta": {
    "agent_role": "refuter",
    "agent_path": null,
    "source": {
      "subagent": {
        "thread_spawn": {
          "agent_path": null,
          "agent_role": "refuter"
        }
      }
    }
  },
  "turn_context": {
    "model": "gpt-5.6-sol",
    "effort": "high",
    "sandbox": "danger-full-access"
  },
  "refuter_instruction_present": true
}
```

### 3.2 dotagents skillはrouting smokeと旧handle検証を要求する

対象: `codex/skills/orchestrate/SKILL.md`

- 12行目: nativeはrouting smoke確認後に同一子へfollow-upすると規定。
- 16行目: 最初のspawnをrouting smokeだけに限定。
- 17行目: `verify-codex-agent-routing <role> <agent-path>`のgreen後だけ実作業を許可。
- 18行目: `refuter`を読み取り専用と定義。

現在のnative toolが返す正式handleはUUID agent IDであり、spawn schemaに`agent_path`入力はない。したがってskillが要求するhandleを標準入口から作れない。

### 3.3 routing verifierは`agent_path`だけでrolloutを探索する

対象: `bin/verify-codex-agent-routing.sh`

- 7〜8行目: CLI引数を`<role> <agent-path>`として固定。
- 21〜24行目: `/`始まりの絶対path以外を拒否。
- 68〜69行目: 第2引数を`expected_agent_path`として扱う。
- 152〜155行目: `session_meta.payload.agent_path`の完全一致だけでrolloutを選択。
- 161〜166行目: 一致する`agent_path`がなければ必ずfail。

現在の実rolloutでは`agent_path=null`のため、role、model、effort、instructionsがすべて正しくても当該箇所へ到達できない。

### 3.4 sandbox不一致は既定でgreenを妨げない

対象: `codex/agents/refuter.toml`、`bin/verify-codex-agent-routing.sh`

- `refuter.toml` 3行目: 説明で「読み取り専用」と明記。
- `refuter.toml` 6行目: `sandbox_mode = "read-only"`。
- verifier 32行目: `CODEX_AGENT_ROUTING_REQUIRE_SANDBOX`の既定値は`0`。
- verifier 221〜225行目: sandbox不一致をerrorにするのは同変数が有効な場合だけ。
- verifier 232〜237行目: 既定ではwarningのみ。
- verifier 245行目: 他項目が一致すればrouting-checkをOKにする。

したがって、現行設計では「refuterはread-only」という宣言と実効権限が一致しなくても、routingをgreenにできる。

### 3.5 テストが旧形式と権限不一致を合成している

対象: `tests/orchestrate/agent-routing-verifier.sh`

- 21〜26行目: 実native spawnではなく、`agent_path`を持つ合成rolloutを生成。
- 38〜41行目: roleの期待はread-onlyなのに、合成`turn_context`を`danger-full-access`として生成。
- 56〜58行目: その状態でrouting-check OKを期待。

対象: `tests/orchestrate/executor-adapters.test.mjs`

- 17〜19行目: routing receiptへ`/root/routing_smoke`を固定。
- 78〜87行目: spawn／follow-up／interruptのcontractを旧`agent_path`形式で検証。
- 96〜102行目: observation handleも`agent_path`だけで固定。

合成テストは自己整合しているが、current Codex native toolとのintegration contractを検証していない。そのためCIがgreenでも実入口は使用不能になる。

## 4. 原因分析

### 原因A: current native handleへの追随漏れ

dotagentsのCodex native adapterは、過去または別入口の`agent_path=/root/...`契約を正としている。current Codex Desktopのnative multi-agent入口はUUID agent IDを返し、follow-up、wait、closeも同じIDを`target`として受け取る。

旧handleを必須にしたままcurrent toolへ接続したことが直接原因である。

### 原因B: 安全gateの適用範囲が標準機能より強い

route、model、effort、instructionsを確認する目的自体は、契約クリティカルなwriter委譲では合理性がある。しかし現在のskill記述は、skill使用中のnative follow-up全般へrouting smokeを要求するよう読める。

実際には、通常の計画監査やread-only fan-outでもこの手順が適用され、標準Ultraが正常に行える並列監査を独自gateが停止させた。

通常レーン、read-only監査、Control writer、本番・H操作を同じ強度で縛る根拠は示されていない。必要性を実証していない安全装置を広範囲へ適用した設計である。

### 原因C: 宣言上のread-onlyと実効sandboxを分離したまま成功扱いできる

role TOMLの`sandbox_mode`がcurrent native spawnへ伝播していないか、spawn runtimeが親sandboxを継承している。どちらであっても、dotagentsは実効read-onlyを確認できていない。

にもかかわらず、verifierはsandbox不一致を既定でwarningへ降格し、合成テストも不一致状態のgreenを要求する。これは「安全gate」という目的と矛盾する。

### 原因D: 実入口を使うintegration testがない

現在のテストは、dotagentsが期待する旧schemaのrolloutを自身で生成して検証している。current Codex native toolのspawn result、rollout、follow-up targetを使っていないため、host側schema変更を検出できない。

## 5. 影響

### 5.1 直接影響

- `orchestrate`手順に厳密に従うと、current native sub-agentへ実作業を渡せない。
- role routingが正しくても`agent_path=null`だけでfalse negativeになる。
- 正常な標準Ultra／multi-agent処理を、親agentが自ら停止する。
- routing smokeだけで席と時間を消費し、成果は得られない。
- read-only監査役が機械的にはfull-accessで動く可能性を残す。

### 5.2 間接影響

- 標準機能の障害とdotagents独自gateの障害を誤認しやすい。
- ユーザーが有効化したUltraの品質・並列性を独自規則が低下させる。
- CI greenが実入口の可用性を意味しない。
- 親agentが「安全手順だから」と旧gateを優先し、通常作業を不必要に統括レーンへ昇格させる。

### 5.3 影響しないもの

- Codex標準のnative sub-agent spawn、wait、close。
- `refuter` role、model、effort、developer instructionsのrouting。
- OpenLogicoolのファイル、ゲーム、デバイス。

## 6. 設計上の問題

本件は単なるfield renameではない。標準機能へ追加制約を置く判断自体を見直す必要がある。

1. 標準のnative sub-agentが既に返す正式handleを、独自handleへ置き換えない。
2. 標準機能を禁止するgateは、実在する高影響リスクと防止効果を説明できる範囲だけに置く。
3. read-onlyを名乗るなら、指示文ではなく実効sandboxで保証する。
4. 権限を保証できない場合は、保証済みと表示せず、通常権限のagentとして扱う。
5. 通常のUltra fan-outと、Control配下の契約クリティカルwriterを別契約にする。
6. 安全装置が標準機能を停止した時、それを「安全に失敗した」と成功扱いしない。

## 7. 推奨修正

### 7.1 最小修正

1. `codex-native`の正式handleをcurrent spawn resultの`agent_id`へ変更する。
2. follow-up、wait、interrupt、closeの`target`へ同じ`agent_id`を渡す。
3. verifierを`verify-codex-agent-routing <role> <agent-id>`へ変更する。
4. rollout探索は`session_meta.payload.id`またはspawn receiptと一致するsession IDで行う。
5. `agent_path`必須、`/root/...`形式検証、旧handle fixtureを削除する。
6. 通常レーンのread-only fan-outではrouting smokeを要求しないことをskillへ明記する。

### 7.2 sandbox契約

次のどちらか一方を明示的に選ぶ。

**案A: 実効read-onlyを保証する**

- native spawn入口でsandboxを指定・検証できる仕組みを接続する。
- `turn_context.sandbox_policy.type == "read-only"`を必須gateにする。
- 不一致時はrole起動失敗として扱う。

**案B: roleからsandbox保証を外す**

- `sandbox_mode = "read-only"`と「読み取り専用」という保証表現を削除する。
- 「書込み禁止の行動指示だが、実効sandboxは親権限を継承する」と正直に記載する。
- writer権限を与えられない場面では、そのroleを使わない。

実効権限を確認できないまま、宣言だけread-onlyとしてwarningでgreenにする現在案は採用しない。

### 7.3 過剰gateの縮小

routing smokeと追加検証を残す場合も、次へ限定する。

- Control配下のwriter委譲
- 認可、トランザクション、公開契約、履歴修復など契約クリティカルな作業
- 本番・公開・H操作の直前

次には適用しない。

- 通常のUltra multi-agent
- 計画書の観点別read-only監査
- sorterによる機械的分類
- parentが結果を読み、自身で裁定する一時的な相談

### 7.4 テスト修正

- current `multi_agent_v1__spawn_agent`の実schemaから得たfixtureを正本にする。
- UUID agent IDでspawn→follow-up→wait→closeを通すintegration testを追加する。
- rolloutの`agent_path=null`をcurrent正常系fixtureとして追加する。
- role、model、effort、instructionsの不一致を個別にnegative testする。
- read-onlyを保証するなら実効sandbox不一致を必ずfailさせる。
- `/root/routing_smoke`を自己生成してgreenにするだけのテストをcurrent contractの証拠に使わない。

## 8. 受入条件

修理完了は次の全項目で判定する。

1. current Codex native toolから`refuter`を1席spawnできる。
2. spawn resultのUUID agent IDを正式handleとして保存できる。
3. 同じagent IDへfollow-upを送り、同一子から回答を回収できる。
4. role、model、effort、developer instructionsを実rolloutから照合できる。
5. `agent_path=null`を理由に失敗しない。
6. read-onlyを保証する場合、実効sandboxもread-onlyである。
7. 実効sandboxがfull-accessなら、read-only保証を表示せずgreenにしない。
8. 通常のUltra計画監査はrouting smokeなしで標準fan-outできる。
9. Control writerだけは、必要なrouting／権限gateを通過しなければ実作業へ進まない。
10. 合成unit testと実native integration testの両方がgreenになる。

## 9. 再現手順

### 9.1 標準sub-agent起動

current tool contractで次を実行する。

```json
{
  "agent_type": "refuter",
  "fork_context": false,
  "message": "Routing smoke only. Do not perform work. Reply ready."
}
```

spawn resultからUUID agent IDを得る。waitすると`completed: ready`になる。

### 9.2 rollout確認

該当rolloutの最初の`session_meta`と最後の`turn_context`を確認する。

期待されるcurrent実測:

```text
agent_role = refuter
agent_path = null
model = gpt-5.6-sol
effort = high
sandbox = danger-full-access
developer instructions = applied
```

### 9.3 旧verifier確認

```text
verify-codex-agent-routing refuter /root/refuter_smoke
```

実測結果:

```text
FAIL: 直近 300 秒の rollout に agent_path='/root/refuter_smoke' が見つからない
```

current spawn schemaには`agent_path`を指定するfieldがないため、この失敗を呼出側で解決できない。

## 10. 修理時に避けること

- current native toolを使わず、旧形式の合成JSONだけを更新して完了扱いしない。
- `agent_path=null`の時に最新rolloutを無条件採用するfallbackを入れない。並列spawn時に別agentを誤認する。
- sandbox不一致を警告だけにしてread-only保証を残さない。
- standard multi-agent全体を停止することで問題を回避しない。
- OpenLogicool側へdotagents互換コードを追加しない。
- agent IDとpathの両方を曖昧に受け付けるsafe defaultを作らない。current正式handleを一つに決める。

## 11. 推奨裁定

1. Codex標準native sub-agent機能は正常と判定する。
2. dotagentsの`agent_path`前提をcurrent native toolとの互換性欠陥と判定する。
3. `read-only`宣言と実効sandboxの不一致を別欠陥として扱う。
4. routing smoke強制の価値をControl writerに限定して再評価する。
5. current toolに合わせた最小修正と実入口integration testを同じ変更で行う。
6. 修理完了まで、通常のCodex Ultraへdotagentsのrouting smoke gateを適用しない。

