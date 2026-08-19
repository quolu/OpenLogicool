# t08-gamelab-oracle 証跡 — GameLab oracle・状態常時表示・停止操作（APP-010、UX-003〜005）

- 実施: 2026-08-19（hotaru・pull run `phase4-durable-lab-20260819-122051`・worktree base 36185d4＝t06 着地後）
- 仕様正本: docs/development-plan.md APP-010・UX-003/004（UX-005 は t10 所有）、docs/phase4-campaign-plan.md §t08、既存 GameLab（GameLabStateMachine oracle・ScenarioRunner——t01 以前からの決定的実験基盤）

## 何を作ったか（すべて tools/OpenLogicool.GameLab＋tests/OpenLogicool.GameLab.Tests）

1. **GameLabRunStatus／GameLabStatusProjector（GameLabRunStatus.cs・新規・pure）**
   - UX-003 の9状態 enum（提案待ち・承認待ち・入力中・結果確認中・利用者停止・対象不一致・認識不能・完了・失敗）。
   - `GameLabStatusInput`: 現在 state の入力は oracle 由来 flag・Attempt 状態（Contracts）・fake Observation status（Contracts）だけ——**実画面を表す field が存在しない**。完了と失敗の同時成立は `GameLabRunOutcome?` の型で排除（bool 2本にしない）。
   - `Project`: 全域 pure 写像（常時表示＝どの入力組合せでも必ず1状態）。優先順は 利用者停止＞対象不一致＞認識不能（Observation Unknown/Unavailable）＞終端＞Attempt 進行（Proposed→承認待ち／Authorized・Prepared・DispatchArmed→入力中／DispatchReported〜NeedsUserDecision→結果確認中／Attempt なし・終端→提案待ち）。
2. **GameLabRunConsole／RunHistoryView（GameLabRunConsole.cs・新規）**
   - **UX-004**: `Pause`／`Resume`／`EmergencyStop` は内部 flag 遷移だけで即時成立し、AI・capture・対象 device の応答を待たない。emergency stop に解除の口は無い（新しい Run でだけ再開）。停止中・終端後は `CanDispatch=false`。
   - 状態報告口は `ReportAttempt`／`ReportObservation`（fake Observation の status）／`ReportTargetMatch`（照合判定は t10 所有——結果だけ受ける）／`ReportOutcome`（二重終端は拒否）。
   - **APP-010（閲覧）**: `RunHistoryView` が journal store（`IRunJournalStore`）から run 一覧と event 要約（sequence・payloadType・attemptId・correlationId・時刻）を read model 化。payload 本文は運ばない（型に field が無い）。書く口を持たない。
   - **APP-010（編集）**: Playbook の編集は既存 `PlaybookCorrection`（PB-008・新 version 作成・旧 version 不変）が正であり、GameLab から同経路で使えることをテストで実証（機能の複製なし）。
3. **csproj**: GameLab に Contracts＋Playbooks 参照を追加（campaign 罠「Prototype を黙って製品扱いにしない」——製品 GameLab は Contracts を正とする方向の一歩）。GameLab.Tests に Fakes 参照を追加。
4. UI window は追加しない（オーナー裁定「UI は最後、機能を計画順で先行」。装飾・実画像は磨きフェーズ）。

## どう確認したか（focused test・worktree 内で実行）

- `tests/OpenLogicool.GameLab.Tests` **18件 green**（既存 ScenarioRunner 7＋新規 GameLabRunConsoleTests 11: 9状態すべてへの到達／**全入力組合せ 2×2×2×17×5×3=2,040 通りの全域性**（例外なく必ず定義済み状態）／利用者停止の最優先／**GameLab assembly が AI・Capture・Devices・Input を参照しない**（参照検証＝UX-004 の構造保証）／pause→resume の即時 dispatch 制御／emergency stop の解除不能・Resume 拒否／二重終端拒否／oracle＋fake Observation だけからの状態遷移一巡（Known→ConfirmingResult→Unknown→Unrecognized→照合不一致→TargetMismatch）／履歴要約（run 一覧・相関情報・本文 field 不在）／GameLab 経由の Playbook 訂正で新 version・旧 version 不変）
- `tests/OpenLogicool.Conformance.Tests` **12件 green**（GameLab 参照者の互換確認——csproj 参照追加の影響なし）

## 対象外

- UX-005（再開時の confirmed state・差分・次操作の表示）は t10-resume-ux 所有
- t05 run controls（pause の Run 進行との統合・PB-013 の物理入力検出）はひなた実装中——console の Pause/EmergencyStop は GameLab 面の操作提供で、executor 停止との配線は t05 着地後の統合面
- 実画面・OCR・装飾 UI（campaign 非目標）

## 罠・申し送り

- GameLab.Tests・Conformance.Tests は fixture を repo root（OpenLogicool.sln 探索）から読むため build 出力 redirect 下では落ちる——redirect を外して実行→obj/bin 即削除（t06 と同じ手法）。
- witness compile は後勝ち置換のため、t08 の compile では canonical の t09 エントリを同梱した（なぎ [43] の申し送りどおり。次に compile する席は t08＋t09＋自分のエントリの同梱が要る——ひなたへは room [73] で伝達済み）。
