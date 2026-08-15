# AI provider 評価設計（Phase 0 deliverable / 2026-08-15）

計画の Phase 0 deliverable「AI provider 候補の data policy、画像対応、構造化出力、費用／遅延評価設計」に対する設計。**本書は評価の方法を定義するもので、provider を選定しない**。選定は Phase 6 の EXP-AI-01（frozen corpus benchmark）で行い、それまで provider 未選定を維持する（計画 §16、実験台帳）。

具体的な model 名・単価・実測値を本書へ書かないのは、Phase 0 時点の値が benchmark 実施時には陳腐化しており、「未決定を仮実装で埋めない」（AGENTS.md 裁定6）に反するため。EXP-AI-01 実施時に **その時点の一次情報を取得してから** 表を埋める。

## 1. 評価対象の限定

評価するのは NextActionProposal を返す能力だけ（[contracts/next-action-proposal.md](contracts/next-action-proposal.md)）。AI は入力・DB・device API へ到達せず、fast path に一切関与しない（§6.3、AGENTS.md 裁定2）。したがって provider の遅延は **durable path の応答性**の問題であり、入力遅延の問題ではない。この区別を評価レポートでも維持する。

## 2. 測定軸（AI-007 の6項目に対応）

| 軸 | 測り方 | 受入の考え方 |
|---|---|---|
| 精度 | frozen corpus に対する proposal の正答率。正答＝ `semanticActionId` が期待集合に含まれ、precondition/expectedOutcome が schema 適合 | 閾値は corpus 確定後に設定。corpus を見る前に閾値を決めない |
| **未知棄却** | 「正解が存在しない／情報不足」な item に対し、誤った proposal を返さず棄却できる率 | **精度より優先する**。Known へ丸める provider は、精度が高くても不採用（§6.9 の丸め禁止と同じ原則） |
| 遅延 | p50 / p95（p99 は corpus 規模次第）。measurement は request 送出〜schema 検証完了まで | 上限は Teach mode の対話性から決める。fast path 要件（NFR-008 の 250ms）とは別基準 |
| 取消 | 実行中 request の cancel が実際に課金・処理を止めるか、時間内に制御が戻るか | 取消不能な provider は Teach mode に使わない |
| 費用 | corpus 1 周あたりの実費と、1 proposal あたり単価。画像添付時／非添付時を分けて測る | daily cost cap（§6.10）を設定可能な粒度で測れること |
| data policy | 下記 §4 のチェックリスト | 不明な項目が1つでも残る provider は cloud 送信の既定 OFF を解除できない |

## 3. corpus 設計

- **frozen**: EXP-AI-01 開始時点で凍結し、benchmark 中に追加・修正しない。corpus は Phase 5 成果（実 session から採取した PlannerContext と Observation）を使う。
- **構成**: (a) 正解が一意な item (b) 複数候補が拮抗する item (c) 正解が存在しない／観測不足の item。(c) を全体の3割以上入れる——未知棄却を測るため。
- **入力形式**: PlannerContext（記録済み）＋ ObservationResult（記録済み）＋ 必要なら evidence crop 画像。**full-screen frame は corpus に含めない**（§6.12 の既定 OFF と整合）。
- corpus は GameLab prototype と実 game の両方から採る。GameLab の Verified は GameLab 内だけで有効（§6.8）であり、benchmark 結果を実 game の Verified 昇格の証拠に使わない。

## 4. data policy チェックリスト（provider ごとに回答を一次情報から取得）

1. 送信データが model 学習に使われるか。opt-out の有無と既定値
2. 保持期間と削除経路（削除要求の実効性）
3. 画像入力の扱いが text と異なるか
4. 保存先リージョン、下請け処理者の有無
5. zero-retention / no-training の契約オプションの有無と条件
6. rate limit・quota 超過時の挙動（明示エラーか、暗黙の劣化か）
7. 構造化出力（JSON schema 強制）の対応と、schema 違反時の挙動
8. 取消 API の有無と課金への反映

7・8 は機能要件でもある: **schema 違反を黙って補完する provider は使わない**（Runtime 側で Rejected 扱いにできるよう、違反は違反として返る必要がある）。

## 5. 比較の禁止事項（計画からの継承）

- provider を黙って切り替えない。local→cloud の fallback もしない（§0、AI-008）。benchmark 中も fallback 経路を作らず、失敗は失敗として記録する。
- provider error / timeout / rate limit / schema error / budget 到達は明示停止する（§6.10）。これらの発生率も評価軸として記録する（隠さない）。
- fine-tuning、provider 側学習、Playbook 学習は別概念として記録を分ける（AI-011）。benchmark は fine-tuning なしの素の model で行う。

## 6. 成果物

EXP-AI-01 の出力は eval report 1本とし、次を含める。

- 実施日、各 provider の model 識別子と版、取得した単価（取得日つき）
- 測定軸6項目の実測値（未知棄却は棄却率と誤答率を分けて）
- data policy チェックリストの回答と出典 URL
- 不採用理由（採用しなかった provider について、どの軸で落ちたか）
- **測れなかった項目**（例: 取消の課金反映が確認不能だった等）を「測れなかった」と明記する

## 7. Phase 0 で確定したこと・していないこと

- 確定: 評価軸、corpus の性質、data policy 確認項目、fallback 禁止の benchmark 運用。
- 未確定（Phase 6 で埋める）: provider 候補の具体名、model、単価、閾値、corpus の実体。
