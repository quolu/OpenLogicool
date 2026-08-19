# t03-journal 証跡 — append-only journal と projection（PB-006、OPS-008/009）

- 実施: 2026-08-19（hotaru・pull run `phase4-durable-lab-20260819-122051`・worktree base 9a909cb）
- 仕様正本: docs/phase4-campaign-plan.md §t03、docs/development-plan.md PB-006／OPS-008／OPS-009・§6.7〜6.8、docs/contracts/data-flow-contract.md（journal 90日・engineering log 14日）、docs/contracts/run-event.md

## 何を作ったか

1. **journal contract（Contracts/Playbooks/RunJournalContracts.cs・新規）**
   - `RunEventPayloadTypes`: PB-006 の8種（observation／proposal／approval／dispatch／dispatch-result／confirmation／correction／manual-intervention）の閉集合。未知種別は journal に入らない。
   - `IRunJournalStore` port: Append／ReadRun／ListRunIds／PreviewExpiredRuns／DeleteRun。上書き API を持たない。削除は Run 単位だけ（Data Flow Contract の削除経路）。retention preview は削除しない（preview してから削除の前半）。
   - `EngineeringLogEntry`＋`IEngineeringLogSink`: engineering log の1行は相関情報（correlation／causation／run／sequence／event／payloadType）だけ。**payload 本文の field を持たない型**にして「OCR／prompt／journal 本文を engineering log に書かない」を構造で保証。
2. **replay（Domain/RunEventSequenceModel.cs・追記）**
   - `RunEventSequenceModel.Replay(IEnumerable<RunEvent>)`: journal からの状態再生成（OPS-008・§6.8「checkpoint は journal から再生成する」）。連番の穴・stale epoch は既存 Append と同じ検証で例外。
3. **SQLite 実装（Persistence・migration 006＋SqliteRunJournalStore.cs・新規）**
   - `run_events` table: PK (run_id, run_sequence)＋event_id UNIQUE。INSERT と Run 単位 DELETE だけで UPDATE 文が存在しない——append-only を DB 制約とコードの両方で保証。
   - retention: `PreviewExpiredRuns(asOf, retentionDays 1..365)` が「run の最終 persisted が期限超過」の run を列挙（既定90日は呼び出し側の値。範囲外は拒否。「削除するまで」は preview を呼ばないことで表す）。
   - persisted/occurred は固定長 UTC 文字列格納（文字列順＝時刻順を retention 判定が前提とするため）。
4. **engineering log 分離（Persistence/FileEngineeringLog.cs・新規）**
   - journal（SQLite）と別保存先の日付別ローカルファイル `engineering-yyyyMMdd.log`（OPS-009 の分離・Data Flow Contract の Engineering Log 行）。
   - `PurgeOlderThanRetention(asOf)`: 14日ローテーション。明示呼び出しだけで削除し、削除一覧を返す。
5. **append 統括（Playbooks/RunJournal.cs・新規）**
   - §6.3「journal の統括は Playbooks」。payload type 閉集合→種別ごとの必須 ID→sequence／epoch 検証（Domain）→store 追記→engineering log 記録の順。検証で落ちた event は store にも log にも現れない。
   - 種別ごとの必須 ID（run-event.md に決定を正本化）: observation→ObservationId、confirmation→AttemptId＋ObservationId（§6.7 契約4の併記）、dispatch→AttemptId＋CommandId、dispatch-result→AttemptId。
   - `RunJournal.Restore(store, sink)`: 再起動復元（OPS-008）——store の実 event の replay だけを根拠に append 位置を復元。checkpoint 等の別経路を持たない。
   - 訂正（correction）は新 event の追記。確定済み event を変更する口は無い（PB-006／PB-008 整合）。
6. **文書（docs/contracts/run-event.md・更新）**: Phase 1 からの nullability 未決定を t03 で最終決定（schema は null 許容維持・必須性は journal の append 検証が payload type ごとに持つ）。payload type 閉集合を正本化。

## どう確認したか（focused test・worktree 内で実行）

- `tests/OpenLogicool.Domain.Tests` **10件 green**（追加2: Replay の複数 run 交錯復元・欠番 journal 拒否）
- `tests/OpenLogicool.Playbooks.Tests` **32件 green**（追加13: 8種全受理／未知種別拒否（store・log とも未書込）／必須 ID 欠落6パターン拒否／sequence 穴拒否／engineering log 相関記録＋本文 field 不在（reflection）／correlation ID による journal↔log 突合／**Restore 後に「次の正しい sequence だけ受理・重複と穴は拒否」**（OPS-008）／Append・Restore 以外の変更 API 不在（reflection））
- `tests/OpenLogicool.Persistence.Tests` **29件 green**（追加12: 全 field roundtrip 順序保証／**DB 再 open→Replay 復元**（OPS-008）／重複 sequence 拒否・重複 event_id 拒否（SQLite 制約）／未知 schema version 拒否／UPDATE API 不在（reflection）／Run 単位削除の限定性／期限切れ preview の非破壊・「最後の event が retention 内なら期限切れでない」／retention 0・366 拒否。既存 migration 期待値を 6 まで更新）
- `tests/OpenLogicool.Architecture.Tests` **4件 green**（依存規則——Contracts は port のみ・Playbooks は SQLite 非参照のまま）

計 75件 green（新規／更新分を含む focused のみ。通し試験は Exit の最終確認まで行わない）。

## 対象外（scope に含めない）

- Attempt 状態機・DispatchArmed（t04 が本 journal を消費して実装）
- pause／skip 等の run controls（t05）、fake Observation（t06）、recorder／replay の projection 一致検証（t09）
- retention の定期実行・削除 UI（journal は preview API まで。実行系の配線は Host／GameLab の後続 task）
- 実画面・OCR・AI Teach（campaign 非目標）

## 罠・申し送り

- worktree 内 `dotnet build` は obj/bin が Lattice 観測の diff entry 上限（256・--ignored=matching）を壊すため、一時 `Directory.Build.props` で build 出力を worktree 外へ向けた（room [15] 申告済み・commit には含めず削除済み）。architecture test だけは repo root 探索（OpenLogicool.sln）が build 出力位置起点のため redirect を外して単独実行し、生成 obj/bin を即削除した。
