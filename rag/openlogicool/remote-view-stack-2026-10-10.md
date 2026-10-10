# 遠隔表示の部品（MediaMTX・ffmpeg・Windows.Graphics.Capture）

- 取得日: 2026-10-10
- 確度: 高（公式資料と、この PC での実測）。個別に「未確認」と書いた項目を除く。

## MediaMTX（v1.21.2）

- publish の受け口は SRT・WHIP・RTSP／RTSPS・RTMP。どれも H.264＋Opus が通る。<https://mediamtx.org/docs/publish/ffmpeg>
- 視聴は組み込みページ `http://host:8889/<path>`、WHEP は `/<path>/whep`。<https://mediamtx.org/docs/read/web-browsers>
- NAT や container の内側に置く時の公式の手順は2通り。固定の UDP 8189 を NAT で通して `webrtcAdditionalHosts` へ外側の address か DNS の名前を書く形と、STUN で穴を開ける形（ランダムな UDP port を使い、port を開けない）。TCP で受ける `webrtcLocalTCPAddress` もある。<https://mediamtx.org/docs/features/webrtc-specific-features>
- B-frame 付きの H.264 は WebRTC で再生できない。<https://mediamtx.org/docs/features/webrtc-specific-features>
- 認証は `authMethod: internal|http|jwt`。`authInternalUsers[].permissions[].action`（publish／read）と `path` で利用者ごとに分けられる。WHIP は `Authorization: Bearer user:pass`。既定の設定は `user: any` に publish／read を許すので必ず置き換える。<https://mediamtx.org/docs/features/authentication>
- 合図は HTTP `webrtcAddress :8889`、media は `webrtcLocalUDPAddress :8189`。NAT や docker では `webrtcAdditionalHosts` を設定して UDP 8189 を開ける。
- **実測**: 既定で MoQ の待ち受け（:8892 TCP/UDP・:8893 UDP）が全ての interface に開く。`moq: false` で切る。初回の起動で Windows ファイアウォールの確認が出た。
- **実測**: 資格情報なし・誤ったパスワード・視聴用の利用者での送信は、すべて 401。
- docker の公式 image は `bluenviron/mediamtx`。<https://mediamtx.org/docs/kickoff/install>

## ffmpeg（9.0.1 full・Gyan・winget）

- `gfxcapture`: Windows.Graphics.Capture で窓を取り込む filter。`hwnd`・`max_framerate`・`width`／`height`・`resize_mode=scale_aspect`・`capture_cursor`・`display_border`（既定 false）。d3d11 の frame を出し、`h264_nvenc` へそのまま渡る。公式に「安定した FPS を保たない」「窓が閉じると EOF」。<https://ffmpeg.org/ffmpeg-filters.html#gfxcapture>
- **実測**: 動かない窓では送出コマが 0 のまま進まず、音も出ない（`color`＋`realtime` へ `overlay` で重ねる形、`-fps_mode passthrough` でも同じ）。
- WHIP の muxer は公式に「experimental」。`-authorization` に `user:pass` を渡す。<https://ffmpeg.org/ffmpeg-formats.html#whip>
- 低遅延の指定: `-tune ull -zerolatency 1 -rc cbr -bf 0`、libopus は `-application lowdelay`。
- 音の入力に process loopback は無い。stdin から `-f f32le -ar 48000 -ch_layout mono -i pipe:0` で渡す。
- **実測**: RTSP を ffmpeg で読み戻すと約 1.2 秒遅れて見える（読む側の溜め）。遅れの測定には WebRTC の視聴を使う（0.15〜0.17 秒）。
- 同梱と配布はしない。利用者の環境へ package manager で入れる。GPL build を別 process として起動するだけの場合の扱いは、公式の文書に記載が無い（未確認）。

## Windows.Graphics.Capture

- 同じ窓へ複数の session を張れる。<https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture>
- 黄色い枠を消す `IsBorderRequired=false` には `RequestAccessAsync(Borderless)` と package manifest の `graphicsCaptureWithoutBorder` が要る。unpackaged の扱いは記載なし（未確認）。<https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscapturesession.isborderrequired>
- `MinUpdateInterval` は build 26100 以降。

## Cloudflare Realtime TURN（外から見る経路の候補。オーナーの指示で不採用）

- 料金: egress 1GB あたり $0.05。SFU と合わせて毎月 1,000GB まで無料。課金されるのは TURN サーバーから TURN client へ送った分で、client からの ingress は無料。STUN（`stun.cloudflare.com`）は無料。<https://developers.cloudflare.com/realtime/sfu/pricing/>・<https://developers.cloudflare.com/realtime/turn/faq/>
- 受け口: `turn.cloudflare.com` の 3478/udp（代替 443/udp）、3478/tcp（代替 80/tcp）、TLS は 5349/tcp（代替 443/tcp）。<https://developers.cloudflare.com/realtime/turn/>
- 資格情報は固定にできない。TURN key（長期の秘密）を作り、`POST https://rtc.live.cloudflare.com/v1/turn/keys/<key id>/credentials/generate-ice-servers`（`Authorization: Bearer <key の API token>`・body `{"ttl": 秒}`）で短期の username／credential を発行する。**有効期限は最長 48 時間**。期限が切れた接続は少し後に切断される。取り消しは `…/credentials/<username>/revoke`。<https://developers.cloudflare.com/realtime/turn/generate-credentials/>
- private な address 範囲への CreatePermission／ChannelBind は拒否される。relay の address は IPv4 だけ。1 client あたり 50〜100Mbps・5〜10kpps を超えると落ちる場合がある。
- MediaMTX の `webrtcICEServers2` は固定の username／password か、coturn 形式の `AUTH_SECRET` だけを受ける。`clientOnly: true` で「ブラウザーだけが使う」にできる。Cloudflare の短期の資格情報を使うには、期限の前に設定を書き換える定期の処理がサーバーに要る。<https://mediamtx.org/docs/features/webrtc-specific-features>
- 未確認（推測）: MediaMTX 自身が TURN を使えば（`clientOnly` なし・STUN なし）、MediaMTX の候補は家の中の address と Cloudflare の relay だけになり、家の回線の address を視聴側へ渡さずに外から届く。実測で確かめる。

## Safari

- WebRTC は H.264 と Opus に対応する。<https://webkit.org/blog/7726/announcing-webrtc-and-media-capture/>
- 現行の iOS での WHEP の再生は未確認（実機で確かめる）。
