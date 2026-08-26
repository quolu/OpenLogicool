# Codex subscription game agent live acceptance（2026-08-26）

## 目的

利用者がアプリへ一言のgoalを渡すだけで、OpenLogicoolがChatGPT subscription認証済みCodexを起動し、Codex自身がOpenLogicool dynamic toolsを使ってNIKKEの日課情報を最下端まで取得する。

利用者goal:

> NIKKEの日課の未完了項目と進捗を最下端まで取得して一覧化する。報酬受取、資源消費、課金、募集、戦闘は行わない。

## 製品経路

- 認証: `codex-cli 0.149.0`の`Logged in using ChatGPT`。子processの環境から`OPENAI_API_KEY`を除去し、`ApiKeyInherited=false`の同条件でもChatGPT loginを確認。OpenAI API keyは不使用。
- 起動: Windows adapterがPATH上の公式`codex.ps1`を実行時解決し、PowerShell 7から`codex app-server --stdio`を開始。
- protocol: App Server JSON-RPC `initialize`（`experimentalApi=true`）→`thread/start`または`thread/resume`→`turn/start`→`item/tool/call`→`turn/completed`。
- thread: game profileごとのdurable threadを`session.json`へ保存し、二回目のrunで同じthreadをresume。
- workspace manifest: `RootKind=UserData`、`RelativePath=game-agents/nikke-157eacb84ec0`。resolved absolute pathはmanifest／sessionへ保存しない。
- system prompt: game別`AGENTS.md`と`thread/start.baseInstructions`へ同じ正本を渡す。developer instructionsには固定profile、goal、保存action優先、毎手再観測、finish必須を渡す。
- sandbox: Codex threadは`read-only`。ゲーム操作はOpenLogicool dynamic toolsだけ。
- dynamic tools: `observe`、`use_saved_action`、`click`、`scroll`、`back`、`wait`、`finish`。
- 入力: Nano Serial HIDのみ。SendInput／Computer Useは0。
- 既存基盤: dynamic toolの一手は`ProductGameExplorerRuntime`へ渡し、WGC→Nano→10秒Compare→LearnTransition→structure／route commitを通す。

## 実走

### Run 1 — 不成立を正しく返した

- Codexは介入なしでタイトル→ロビー→MISSION→デイリーtabまで到達し、一覧上部7項目を取得。
- 10秒観測でafter frameが0件だったstepが`ObservationUndetermined`を返した際、既存Coordinatorのactive probeが閉じず、次proposalが「未完了のprobe」で拒否された。
- Codexは`finish`を呼ばず、未完了として終了。成功扱いしなかった。

### 原因修正

- `ProductGameExplorerRuntime`がafter frame 0件をOutcomeUnknownとして`LearnTransition`へ渡し、durable outcomeを記録してactive probeを閉じるよう修正。
- UndeterminedはRun失敗や自動入力retryへ変換しない。

### Run 2 — 成立

- NIKKEを別processで再起動。
- アプリへgoalを一度投入した後、親AIからの座標・Back・scroll・steeringは0。
- OpenLogicoolが同じgame workspaceとCodex threadをresume。
- Codexが保存routeを優先し、失敗stepだけ修復しながらMISSIONのデイリーtabへ到達。
- 一覧を最下端までscrollし、追加項目がないことを確認して`finish`を呼んだ。
- App Server turn status: `completed`。
- OpenLogicool terminal phase: `Completed`。
- dynamic tool call: 46件。
- Learning Route revision: 11。
- 報酬受取、資源消費、課金、募集、戦闘: 0。

## 取得結果

- デイリーポイント: `0/100`
- 確認時の残り時間: 約11時間28分

| point | 未完了項目 | 進捗 |
|---:|---|---:|
| 20 | シミュレーションルームに1回挑戦する | 0/1 |
| 20 | 迎撃戦を1回クリアする | 0/1 |
| 20 | ニケの面談を1回実行する | 0/1 |
| 10 | 派遣を3回実行する | 0/3 |
| 10 | 基地防衛報酬を1回獲得する | 0/1 |
| 10 | まとめて殲滅を1回実行する | 0/1 |
| 10 | フレンドにソーシャルポイントを1回プレゼントする | 0/1 |
| 10 | キャンペーンを1回実行する | 0/1 |
| 10 | 隊員募集を1回実行する | 0/1 |
| 10 | タワーに1回挑戦する | 0/1 |
| 10 | 一般ショップでアイテムを1回購入する | 0/1 |
| 10 | ルーキーアリーナに2回挑戦する | 0/2 |
| 10 | ニケを1回レベルアップする | 0/1 |
| 10 | 装備レベルアップを1回行う | 0/1 |

## 判定

- アプリによるgame別workspace作成: **確認済み**
- ChatGPT subscription Codex起動・thread resume: **確認済み**
- API key非継承状態でのChatGPT login: **確認済み**
- system／developer promptとdynamic tools: **確認済み**
- 利用者goal一言からのアプリ内Codex完走: **確認済み**
- 最下端までの情報統合とUI/CLI result: **確認済み**
- 禁止操作0、Nano-only: **確認済み**
- 関連test: Host 241・Desktop 98・Exploration 50・Playbooks 164・architecture 8、合計561件green。
- 最終full regression: 22 test project・1240件green、失敗0、skip 0。
- full regression後のsubscription境界focused test: 3件green。
