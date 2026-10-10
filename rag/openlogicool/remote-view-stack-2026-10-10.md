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
- **実測（2026-10-11・マビノギモバイル）**: `max_framerate` を送るコマ数と同じ 30 にすると、届くコマは毎秒 28 ほどで間隔が揃わず、30 コマへ揃える段（`-fps_mode cfr -r 30`）で同じコマの繰り返しが 15 秒に 28〜36 回起きる（視聴側でコマががたつく）。`max_framerate=60` で取り込んで 30 へ間引くと、繰り返しは 0 回（15 秒・2回ずつ測定）。
- **実測**: 動かない窓では送出コマが 0 のまま進まず、音も出ない（`color`＋`realtime` へ `overlay` で重ねる形、`-fps_mode passthrough` でも同じ）。
- WHIP の muxer は公式に「experimental」。`-authorization` に `user:pass` を渡す。<https://ffmpeg.org/ffmpeg-formats.html#whip>
- **実測（2026-10-11）**: WHIP の UDP 送信の溜め場（`-ts_buffer_size`・既定 -1）を指定しないと、別の機器の MediaMTX（家の中の LAN）へ送った時に、接続の確立の直後・最初の数コマで `UDP send blocked, please increase the buffer via -ts_buffer_size`（-11）で終了する。同じ PC の中の MediaMTX へ送る時は起きない。`-ts_buffer_size 4194304` で 720p・3Mbps・20秒（600コマ）を送り切った。
- **実測（2026-10-11）**: ffmpeg の WHIP は、answer の候補（`a=candidate`）のうち先頭の1つだけへ接続する。先頭が届かない address だと DTLS の handshake が 5 秒で時間切れになる。MediaMTX 1.21.2 は `webrtcAdditionalHosts` の最後に書いた address を先頭の候補にする（2通りの並びで計5回の観測）。家の中の address を最後に書く。
- **実測（2026-10-11）**: 終了時の WHIP の DELETE は、Cloudflare のトンネル経由だと応答を読めずに `Failed to dispose resource`（-5）が出る。MediaMTX は session を `terminated` で閉じている。
- **実測（2026-10-11）: 音の時間が映像より遅れると、同じ絵の繰り返しが終わりまで続く**。ffmpeg は出力へ混ぜる前に入力どうしの時刻を揃え、進んでいる側の読み取りを止めて遅れている側を待つ。映像（`gfxcapture`）は取り込みの時計で時刻が進み、stdin の生の音（`s16le`）は届いた量から時刻を数える。音を渡せない時間があると（WHIP の接続の確立に約1.1秒かかり、その間は読まれない。process loopback の溜め場は200ms）、失った音のぶんだけ音の時刻が遅れたままになる。ffmpeg は映像の取り込みを止めて待ち、`-fps_mode cfr` が止まった間を同じコマで埋める。視聴側ではコマは毎秒30届くのに、絵が0.3〜0.9秒おきにしか変わらない。
  - 本物の中継サーバーへ20秒: 593コマ中416コマが繰り返し。送信なし（`-f null`）で音が実時間で届く時は0。音を途中で0.9秒失わせると、送信なしでも450コマ中253コマ。
  - 修理: 映像と音の両方の入力へ `-use_wallclock_as_timestamps 1` と同じ `-itsoffset -<起動時刻の unix 秒>` を付け、`-copyts` で入力ごとの時刻の引き直しを止める。音は `-af aresample=async=1` で時刻に合わせて埋める・切る。同じ条件で繰り返しは600コマ中43コマ（つなぎ始めの約1.4秒ぶん）。音を失った時は、その間だけ絵が止まって元へ戻る。
  - `-isync` は使えない。生の音の入力は開いた時点で始まりの時刻を持たず、「Unable to identify start times」で調整されない。`-itsoffset` なしの `-copyts` は時刻が大きすぎて `frame duplication too large` で何も出ない。
  - 確かめ方: コマの数ではなく、受けた映像の絵が前のコマから変わったかを数える。コマ数と間隔だけを見ると正常に見える。
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
