# 遠隔表示の設置と使い方

ゲームの窓の映像と音を、利用者の中継サーバー経由で iPad／iPhone の Safari に表示する。取り決めと段階は[計画](remote-view-campaign-plan.md)、部品の調査は[rag](../rag/openlogicool/remote-view-stack-2026-10-10.md)。

## 通り道

```
PC（ffmpeg）──WHIP(HTTPS)──▶ stream.kitepon.dev（Cloudflare → トンネル → Caddy）──▶ MediaMTX :8889   合図とページ
PC（ffmpeg）──UDP 8189──────────────────────────────────────────────────────────▶ MediaMTX         映像と音（家の中は直結）
Safari ───── WHEP(HTTPS) ──▶ stream.kitepon.dev ──▶ MediaMTX :8889                                  合図とページ
Safari ───── UDP 8189 ─────▶ 家の中: メインサーバー ／ 外: 家の回線の address（ルーターが転送）     映像と音（直結）
```

Cloudflare のトンネルは Web の通信だけを運ぶ。映像と音は通らない。

## サーバーに置いたもの（main-server）

| 何 | 場所 | 戻し方 |
|---|---|---|
| MediaMTX（版は compose に固定） | `~/remote-view/`（`docker-compose.yml`・`mediamtx.yml`）。container は `remote-view-mediamtx`、Caddy と同じ docker network | `docker compose down` して folder を消す |
| Caddy の項目 `stream.kitepon.dev` | `~/license-server/Caddyfile` の末尾。変更前の写しは `Caddyfile.pre-remote-view-20261011.bak` | 項目を消して `caddy reload` |
| トンネル（home-server）の行き先 | Cloudflare の設定。変更前の全体は `~/remote-view/tunnel-config-before-*.json` | 保存した内容で書き戻す |
| DNS の `stream` | Cloudflare（トンネルへの CNAME・proxied） | レコードを消す |

`mediamtx.yml` の決まり:

- 使う受け口は WebRTC だけ。ほかの受け口（RTSP・RTMP・HLS・SRT・MoQ・API）は切る。
- 利用者は送信用（publish だけ）と視聴用（read だけ）に分け、パスワードは hash で持つ。
- `webrtcAdditionalHosts` は、家の回線の address・家の中の address の順に書く。最後に書いた address が候補の先頭になり、送信側の ffmpeg は先頭の候補だけを使う。
- 設定を変えた後は `docker compose restart` で読ませる。container の中に shell の道具は無い。
- `Caddyfile` と `mediamtx.yml` は file ごと mount している。追記か上書きで書き換える（置き換えると container から見えなくなる）。

## ID とパスワード

- 送信用: この PC の資格情報マネージャー（アプリの「遠隔表示」→ 中継サーバーの設定、または `remoteview save-settings`）。
- 視聴用: 利用者の 1Password。

## 使い方

1. Game Operator の「対象」でゲームを選ぶ。
2. 「遠隔表示」で開始を押す（`remoteview start`）。止める時は停止（`remoteview stop`）。
3. iPad／iPhone の Safari で視聴用の URL を開き、視聴用の ID とパスワードを入れる。

## 映像が正しく届いているかの確かめ方

コマの数と間隔だけでは、同じ絵の繰り返しを見逃す。受信側で、届いた絵が前のコマから変わったかを数える。

```powershell
node scripts/remote-view-receive-check.mjs https://stream.kitepon.dev/game/ <視聴用のIDとパスワードのJSON> 30
```

- 窓を出さない Chrome で視聴ページを開いて測る。ゲームの窓からフォーカスを奪わない。
- `identicalToPrevious` の `frames` が前のコマと全く同じ絵の数、`longestRun` が最長の連続。動いているゲームでは、どちらも 0 に近い値になる。`runsOf3OrMore` が 0 でない時は、送り出し側が映像を止めて同じコマで埋めている。
- JSON は `{"viewUser": "...", "viewPass": "..."}`。repo へ置かない。

## 外から見る時に要るもの

- 家のルーターが UDP 8189 をメインサーバーの 8189 へ転送すること。
- `mediamtx.yml` の `webrtcAdditionalHosts` に、家の回線の今の address が書いてあること。address が変わったら書き直して `docker compose restart` する。
