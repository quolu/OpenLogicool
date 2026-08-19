# t09-recorder-replay 証跡

- 実装者: なぎ（pull run `phase4-durable-lab-20260819-122051`・base `ce4cb20`）
- 対象: session recorder／replayer。journal replay と projection の一致（受入条件4）、active Run の version が replay・crash 復元で勝手に変わらない（受入条件5）。実画面・OCR・AI Teach は対象外。

## 何を作ったか

t03 の journal（`RunJournal`／`IRunJournalStore`／`RunEventSequenceModel`）と t02 の version pin（PB-002）の上に、次の3ファイルだけを追加した。Contracts（F 直轄）と Persistence（migration）は無変更。

1. **`src/OpenLogicool.Domain/RunProjection.cs`**（pure・immutable・record 値等価）
   - `RunEventTally`: PB-006 閉集合8種の件数。未知 payload type は例外。
   - `RunProjection`: 1 Run の projection（RunId・PlaybookId・pin 済み version・LastSequence・executor epoch・LastEventId・最新観測 ID・tally）。
   - `FromFirstEvent`（runSequence 1 必須）／`Apply`（連番・stale epoch・RunId/PlaybookId 不変を検証）／`Replay`（event 列から再生。逐次 Apply と同一検証＝同一入力から同一値）。
   - **version 不変の構造化**: pin と異なる `PlaybookVersionId` を運ぶ event は `Apply` が例外で拒否する。projection の version が黙って変わる経路は型の上に存在しない。明示的な version switch は PB-007（t05）の面であり、ここに口を作っていない（t05 が switch を実装する時にこの検証を拡張する縫い目である旨をコメントに明記）。
2. **`src/OpenLogicool.Playbooks/SessionRecorder.cs`**
   - journal append と同じ event 列から Run ごとの projection を逐次構築する。順序は「projection 計算（pure・失敗時 store 未書込）→ `RunJournal.Append`（検証＋永続化）→ projection 確定」。どちらかの検証で落ちた event は store にも projection にも現れない——recorder 経由で両者が乖離する経路が無い。
   - `Restore(store, sink)`: OPS-008 の crash 復元。store の実 event の replay だけを根拠に projection と追記位置を再生する（checkpoint 等の別経路なし。空 store は新規 session）。
3. **`src/OpenLogicool.Playbooks/SessionReplayer.cs`**
   - `Replay(store)`: 永続化済み journal から Run ごとの projection を再生。store の読み取り API（`ListRunIds`／`ReadRun`）しか呼ばず、書き込みの口を持たない——replay が journal・pin 済み version を変えない性質は構造で成立。

## どう確認したか（focused test・worktree 内で実行）

- **`tests/OpenLogicool.Domain.Tests/RunProjectionTests.cs`（12件追加）**: 先頭 event の pin と初期値／seq 1 以外の開始拒否／逐次適用の蓄積（seq・epoch・tally・最新観測。confirmation の observationId 併記では観測を進めない）／連番の穴拒否／stale epoch 拒否／別 Run の event 拒否／**version 変更拒否（pin 維持）**／PlaybookId 変更拒否／未知 payload type 拒否／**同一 event 列の逐次適用と Replay の値等価**／空列拒否。
- **`tests/OpenLogicool.Playbooks.Tests/SessionRecorderReplayTests.cs`（6件追加）**: 2 Run interleave の記録に対する **`SessionReplayer.Replay` と recorder projection の全 Run 一致（受入条件4）**／replay の読み取り専用性（store 不変・再 replay 決定性）／journal 検証で落ちる event（dispatch の必須 ID 欠落）が projection に現れない／**version 変更 event は永続化前に拒否され pin 不変（受入条件5）**／**crash 復元（recorder 破棄→store だけから `Restore`）で全 projection 値一致・両 Run の pin 済み version 不変・復元後の追記継続と replay 一致**／空 store の復元は空 session。
- 実行結果（worktree・build 出力は一時 `Directory.Build.props` で scratchpad へ redirect——Lattice 観測保護のため。accept 前に削除）:
  - `dotnet test tests/OpenLogicool.Domain.Tests` → **22件 green**（既存10＋追加12）
  - `dotnet test tests/OpenLogicool.Playbooks.Tests` → **38件 green**（既存32＋追加6）
  - `dotnet test tests/OpenLogicool.Architecture.Tests` → **4件 green**（repo root 探索が redirect と両立しないため、この 1 run だけ worktree 内出力で実行し、直後に obj/bin を削除。csproj・参照は無変更）
- 再走コマンド: worktree 直下に build 出力 redirect の一時 `Directory.Build.props` を置き、`dotnet test tests/OpenLogicool.Domain.Tests` と `dotnet test tests/OpenLogicool.Playbooks.Tests`（手法は room [15] と同じ）。architecture は redirect なしで回して obj/bin を消す。

## 実装しなかったもの（判断）

- SQLite 実 store を跨いだ再 open 復元の結合試験: store の永続化忠実性（append-only・再 open 復元・未知 version 拒否）は t03 が Persistence focused test 29件で実証済み。t09 の replay／projection は store interface の上の pure 論理であり、fake store での検証と t03 の実証の合成で閉じる（Persistence 無変更のため境界も広げない）。
- version switch の journal 表現: PB-007（t05）の所有。t09 は「黙って変わらない」拒否だけを置いた。
