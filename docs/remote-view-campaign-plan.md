# Remote View campaign（ゲームの窓を iPad／iPhone で見る）

- 承認: オーナー（2026-10-10）。この承認を、ゲームの映像と音を利用者自身の中継サーバーへ送ることを認める裁定として記録する（[Data Flow Contract](contracts/data-flow-contract.md)・計画 RV-001〜003）。
- 成立性の実測: [段0の記録](probes/remote-view-feasibility-2026-10-10.md)
- 外部の部品の調査: [rag](../rag/openlogicool/remote-view-stack-2026-10-10.md)

## 承認後に実測で改めた点

1. **無音の補い方**: 計画は「鳴っていない間は時計に合わせて無音を書く」としていた。送った合計を時計と比べて埋めると、ffmpeg が読み始めるまでの遅れを毎回不足と数え、鳴っている音へ無音が挟まる（14秒に74回の途切れを実測）。**音源から何も届かない時間のぶんだけ無音を書く**形にする。
2. **動かない窓は配信が始まらない**: ffmpeg が自分で取り込む方式は、画面が変わった時だけコマが来る。止まった表示の窓では映像も音も出ない。ゲームは描き直しを続けるのでコマは届く（マビノギモバイルで5分・9000コマ・途切れ0）。製品は、映像のコマが届いていない状態を表示する。
3. **段の順番**: 段0 のうち、サーバーと iPhone が要る項目（音と映像のずれ・Safari での再生・外からの遅れ）と、Bot 稼働中の同居は、段1〜4 と並行して進める（オーナーの指示 2026-10-10）。
4. **中継ソフトの設定**: MediaMTX は既定で MoQ の待ち受けを全ての interface に開く。使わない受け口は明示して切る。
5. **コマの数では合否を決めない**: 段0 は「30分・切断0・コマ数」で連続稼働を合格にしたが、届いた絵の中身を見ていなかった。製品は、コマは毎秒30届くのに絵が0.3〜0.9秒おきにしか変わらない状態で配信していた（オーナーが iPad と Mac で発見。2026-10-11）。連続稼働と再生の合否は、受信側で「前のコマと全く同じ絵」を数えて決める（`scripts/remote-view-receive-check.mjs`）。段0 の合格は取り直す。
6. **映像と音の時刻**: 計画は音を stdin へ生のまま渡すとだけ決めていた。届いた量から音の時刻を数えると、渡せなかった間に失った音のぶんだけ映像より遅れ、ffmpeg が音を待って映像を止め続ける。映像と音の両方へ、同じ起点の現在時刻を ffmpeg に振らせる（[調査](../rag/openlogicool/remote-view-stack-2026-10-10.md)）。
7. **取り込みのコマ数**: 取り込みの上限を送るコマ数と同じにすると、同じコマの繰り返しが毎秒2回ほど起きる。取り込みは2倍にして、送る直前に間引く。
8. **配信の入り切り**: 計画は PC のアプリの画面の開始／停止として作った。外出先からは入れられず、アプリを入れ直すと止まったままになる。オーナーの指摘で、見る側から入り切りできる形へ改めた。中継サーバーは、送り手がいない所へ来た見る側を最長20秒待たせる（MediaMTX の `runOnDemand`）。PC のアプリは中継サーバーへ見ている端末の数を聞きに行き、見に来た時に送り始め、いなくなって15秒で止める。


## Context

クオ君の依頼: ゲームの窓の映像と音を iPad／iPhone で見たい。リモートデスクトップに近い形で、当面は見るだけ。将来は遠隔操作へ広げる。Chrome Remote Desktop は窓のフォーカスを奪うので使えない。

会話で決まったこと（合意済み・逐語で守る）:

| 項目 | 決定 |
|---|---|
| 見る場所 | 外出先（携帯回線）からも、家の Wi-Fi からも。VPN は使わない |
| つなぎ方 | クオ君のサーバーを中継にする（外から届き、docker を動かせる） |
| 音 | ゲームの音も聞く |
| 映す範囲 | ゲームの窓だけ（ほかの窓・通知は映さない） |
| 見る方法 | まず Safari で開く。専用アプリは必須にしない |
| 画質 | 切り替え式。既定は 720p・毎秒30コマ・約3Mbps、「きれい」は 1080p・約8Mbps |
| 見る側の守り | ID とパスワード |
| 配信の開始 | PC のアプリで「受け付ける」を入にしている間、見る URL を開いた時に始まり、閉じると止まる（オーナーの回答 K-RNKK4B・2026-10-11。設計時の「スイッチを入れた時だけ」を PC の画面のスイッチとして作ったのを改めた）。切の間は映像も音も外へ出さない |
| 絶対条件 | ゲームの窓からフォーカスを奪わない |

## 方式（標準の部品を組み合わせる。圧縮と通信は自作しない）

```
ゲームの窓 ──(ffmpeg が自分で取り込む: gfxcapture)──┐
                                                      ├─ ffmpeg（H.264=NVENC／Opus）──WHIP(HTTPS)──▶ MediaMTX（サーバー・docker）──WebRTC──▶ Safari
ゲームの音 ──(OpenLogicool: process loopback)─ stdin ─┘
```

- **映像**: ffmpeg 9 の `gfxcapture`（Windows.Graphics.Capture で窓を取り込む filter。GPU の中で `h264_nvenc` へ渡る）。OpenLogicool は画素を運ばない。`WgcFrameSource` は変えない（Bot の取り込みに触れない）。
- **音**: ffmpeg は process 単位の音を拾えないので、既存の `ProcessLoopbackAudioSource` で拾って ffmpeg の stdin へ渡す。
- **中継と視聴**: MediaMTX（v1.21 系・公式 docker image）。視聴は組み込みの WebRTC ページを Safari で開く。publish と read を別の利用者に分けて認証する。
- **OpenLogicool が担う範囲**: 音の取り込み・ffmpeg の起動と監視・開始と停止・状態の表示・設定と秘密の保存だけ。製品の中に待ち受け（HTTP／TCP）は作らない。

設計担当が一次資料で確かめた事実（MediaMTX の受け口と認証・ffmpeg 9.0.1 full がこの PC に導入済みで `gfxcapture`／`whip`／`h264_nvenc`／`libopus` を持つこと・WGC は同じ窓へ複数 session を張れること）は、段1 で `rag/` へ記録する。

退けた案: 製品内に WebRTC と encoder を持つ（作る量と危険が大きい）／Sunshine＋Moonlight（画面全体を映す・中継の形に合わない・将来の入力が Nano 経由にならない）／MJPEG（音なし）／HLS（遅れが大きい）／OpenLogicool が画素を pipe で渡す（1080p30 で約 250MB/s の複製が要る）。

## 段階

### 段0: 成立性の実験（製品のコードは変えない。ここが通らなければ先へ進まない）

- 0a: 手動で一巡させる。ffmpeg（`gfxcapture`＋試験音）→ MediaMTX（まずこの PC で公式 release の実行ファイル、次に本番サーバー）→ Safari。
- 0b: `src/OpenLogicool.Probe/RemoteViewSmoke.cs` を足す（`Program.cs` に分岐 `remote-view-smoke`）。既存の `ProcessLoopbackAudioSource` の音を stdin で渡し、合否を JSON に残す。
- 合否の条件:

| 項目 | 条件 |
|---|---|
| フォーカス | 開始・停止・ffmpeg の強制終了を各10回行い、前面の窓の変化が 0 |
| 遅れ（p95） | Wi-Fi 500ms 以下、携帯回線 800ms 以下 |
| 音と映像のずれ | 開始時と30分後に 100ms 以内 |
| 負荷 | ゲームの fps 低下 5% 以下、Bot の観測周期の増加 10% 以下 |
| 連続稼働 | 30分間、切断 0 |
| 認証 | ID とパスワードなしの publish／視聴が拒否される |
| 再生 | iPhone と iPad の Safari で音つき再生 |

- 記録するだけの項目: 窓の大きさ変更・最小化・窓を閉じた時の挙動、動かない画面で届くコマ数（取り込みは画面が変わった時だけコマが来る）、開いた直後に映像が出るまでの時間、WHIP と SRT の差、黄色い枠の有無。
- 遅れの測り方: 時刻を表示する試験用の窓を配信し、PC のブラウザで受けた映像と並べて撮る。携帯回線の分だけ、クオ君に iPhone と PC の画面を1枚撮ってもらう。
- 条件を満たさない項目が出たら、別方式へ黙って切り替えない。結果と選択肢を報告する。
- 記録先: `docs/probes/remote-view-feasibility-<日付>.md`

### 段1: 取り決めの文書（コードより先）

- `docs/contracts/data-flow-contract.md` へ2行足す。
  - 遠隔表示の映像と音: 生成元は ffmpeg と process loopback、保存なし、送信先は利用者が設定した自分の中継サーバーだけ、保持は配信中だけ、停止で消える、既定 OFF、開始は利用者の明示の操作だけ。
  - 中継サーバーの資格情報: Windows の資格情報マネージャー（計画書 §6.12 の定め）。export と診断 bundle の対象外。
- `docs/development-plan.md`: 要件 ID の新設（遠隔表示）、製品境界へ「視聴目的の送信」の明文化、独立 campaign として記載（Serial HID の前例に倣う）。
- `docs/remote-view-campaign-plan.md` を作る。
- `rag/openlogicool/remote-view-stack-<日付>.md` と `rag/INDEX.md`: MediaMTX・ffmpeg・WGC の枠の条件の調査記録（URL つき）。

この計画の承認を、「ゲームの映像と音を自分のサーバーへ送ること」を取り決めに認めるオーナー裁定として記録する。

### 段2: 音（Capture）

- `src/OpenLogicool.Capture/ProcessLoopbackAudioSource.cs` に `public int ReadStereo(Span<short> interleaved)` を足す（左右を混ぜずに渡す）。既存の `Read`（mono・G13 の音連動が使用中）と `IProcessAudioSource` は変えない。

### 段3: 常駐の処理（Host）

`G13AudioBacklightRuntime`（`src/OpenLogicool.Devices.G13/`）と `SerialHidOutputSettingsStore`（`src/OpenLogicool.Host/SerialHidOutputSettings.cs`）の型を踏襲する。

- `src/OpenLogicool.Contracts/Playbooks/RemoteViewContracts.cs`: `IRemoteViewIntents { RemoteViewSnapshot Current(); void Start(); Task StopAsync(); 設定の読み書き }`、画質は `Standard`（1280×720・30・3000kbps）と `Fine`（1920×1080・30・8000kbps）。
- `src/OpenLogicool.Host/RemoteViewSettings.cs`: 設定（中継サーバーの URL・画質・ffmpeg の場所）と `RemoteViewSettingsStore.ForDatabase`。
- `src/OpenLogicool.Host/RemoteViewSecretStore.cs`: publish 用の ID とパスワードを資格情報マネージャーへ保存する。
- `src/OpenLogicool.Host/RemoteViewFfmpegArguments.cs`: ffmpeg の引数を組み立てる pure な関数（低遅延の指定・B-frame なし・WHIP・認証）。
- `src/OpenLogicool.Host/RemoteViewRuntime.cs`: `Start(WindowsGameTarget)`／`Stop()`／`Status`。
  - 専用の低優先度 thread で音源を作り、読み、stdin へ書き、破棄する。鳴っていない間は時計に合わせて無音を書く（書かないと音が先へ進む）。
  - ffmpeg は窓を出さずに起動し、Job Object（`KILL_ON_JOB_CLOSE`）へ入れる。Host が落ちた時に配信だけが続くのを防ぐ。
  - ffmpeg が終了したら `Faulted` にして stderr の末尾を表示する。自動の再起動はしない。窓が閉じた時は「ゲームの窓が閉じた」と表示して止まる。
- `src/OpenLogicool.Host/HostRemoteViewIntents.cs`、`ApplicationControlRegistration.cs`（`registry.Add("remoteview", …)` と `app status` への追記）、`Program.cs`（組み立て）。
- 対象のゲームは、Game Operator の既存の「対象」（`MacroTargetSettingsStore`）と `WindowsGameTargetLocator.Locate` で決める。窓を選ぶ画面は足さない。
- fast path とは別 thread・別 process。配信の失敗を入力の処理と Input Studio へ伝えない（`docs/contracts/input-studio-isolation.md`）。

### 段4: 画面（Desktop）

- `src/OpenLogicool.Desktop/RemoteViewPanel.cs` を作り、`GameOperatorWindow.cs` の左の一覧へ「遠隔表示」を足す（`BotScriptPanel` の型を踏襲）。
- 中身: 開始／停止のスイッチ、画質の切り替え、状態（配信中・停止・失敗の理由）、見るための URL の表示と複製、中継サーバーの設定。
- `src/OpenLogicool.Host/UiSnapshot.cs` に見本を足す。

### 段5: サーバーと運用

- 中継サーバーへ MediaMTX を docker で置く。設定は、WebRTC だけ有効・publish 用と視聴用の利用者を分ける・パスワードは hash で保存・`webrtcAdditionalHosts`・image の版を固定。HTTPS は既存の reverse proxy に載せ、UDP 8189 を開ける。
- `docs/remote-view-operation.md`（設置と使い方）。
- 実機で段0 の表を測り直し、`docs/remote-view-exit-assessment.md` で合否を宣言する。

### 段5 の現在地

- オーナーの回答（決裁箱 K-EZE9L9）と指示（2026-10-11・会話）: サーバーへの設置を進める。Cloudflare の中継サービス（TURN）は使わない。名前を引いてメインサーバーへ届いた後は直結にする。
- 設置した形と手順、戻し方は[設置と使い方](remote-view-operation.md)。
- 済み（2026-10-11・実測）: MediaMTX の起動、Caddy・トンネル・DNS への追加。`https://stream.kitepon.dev/game/` は、ID なしと誤ったパスワードが 401、視聴用の ID で 200、視聴用の ID での送信が 401。この PC から試験用の映像と音を送り、家の中の直結（UDP 8189）で届いた（MediaMTX の記録で 2 tracks・publishing）。この PC のアプリへ送信先・視聴用の URL・送信用の ID とパスワードを保存した。視聴用の ID とパスワードを利用者の 1Password へ保存した（一覧で1件を確認。中身の読み戻しは未実施）。
- 直した欠陥: ffmpeg の WHIP の送信の溜め場が既定のままだと、別の機器の中継サーバーへ送る時に最初のコマで終了する。`-ts_buffer_size` を足した（[調査](../rag/openlogicool/remote-view-stack-2026-10-10.md)）。
- 家の回線の実測（サーバーから STUN 3か所へ問い合わせ）: 外側の IPv4 は1つで、内側の port がそのまま外側の port になり、宛先を変えても変わらない。IPv4 を共有する回線（使える port が限られる形）ではない。
- 直した欠陥（2026-10-11）: 視聴側で絵が0.6秒おきにしか変わらない（映像と音の時刻のずれ）、取り込みのコマの繰り返し。修理後、アプリからの配信を受信側で測り、30秒862コマのうち前のコマと全く同じ絵は3コマ（最長2コマ続き）、別の20秒583コマでは0。Bot を動かしたままの同居で配信は220秒続き、Bot は動作中のまま。オーナーが視聴側で映像が滑らかになったと報告した（音と、動きと音のずれは未聴取）。
- 見に来た時だけ送る形（2026-10-11）: PC 側（受け付けの入り切り・視聴の有無の問い合わせ・画面）は実装し、Host と Desktop の遠隔表示のテスト72件が成功。仕組みはこの PC の中で同じ版の MediaMTX を動かして実測した。中継サーバーの設定の書き足し（API・`runOnDemand`・Caddy の読むだけの口）は、オーナーの許可（決裁箱 K-HWEJ7B）を待っている。入るまで、受け付けを入にしても「問い合わせできません」と出て送らない。
- 未実施・未確認:
  - オーナーの耳での確認（音・動きと音のずれ）と、iPhone の Safari での再生。
  - 外からの直結。家のルーターで UDP 8189 をメインサーバーへ転送する設定は、オーナーが足した（決裁箱 K-SA89N9）。外から届くかは未実測で、携帯回線で確かめる。
  - 家の回線の address が変わった時の扱い。MediaMTX の設定は address を固定で持つ。`kitepon.dev` の DNS は全てトンネル経由の CNAME で、家の回線を指す名前と、それを更新する定期の処理は無い。
  - 段0 の表の測り直しと合否の宣言（遅れ・音と映像のずれ・負荷・30分の連続稼働を、絵の変化の測定つきで）。
  - 配信の最初の1分に、PC から中継サーバーへの区間で映像の部品が落ちた記録が2回ある（53個と1個）。原因は未調査。

## クオ君の手が要る所

1. **サーバーの情報と設置の許可**（段0 の後半と段5）: ドメイン、既存の reverse proxy、UDP 8189 を開けられるか。設置はサーバーの変更なので、内容を示して許可をもらってから行う。
2. **iPhone／iPad での確認**（段0 と段5）: Safari で音つきで見られるか、携帯回線での遅れの写真1枚。
3. **ID とパスワード**: 視聴用を決めてもらう（publish 用は私が生成して資格情報マネージャーへ入れる）。

## やらないこと（今回の範囲外。必要なら別に提案する）

遠隔操作／家の中だけの直結の経路（家の Wi-Fi でもサーバーを往復する）／回線に合わせた画質の自動調整／専用の iOS アプリ／複数人での視聴／録画。

## 検証

- focused test（`tests/OpenLogicool.Host.Tests`）: ffmpeg の引数の一致、無音の補い、ffmpeg 終了時の `Faulted`、設定の往復、資格情報が設定ファイルと `app status` に出ないこと。
- `tests/OpenLogicool.Architecture.Tests`: 製品のコードに `HttpListener`／`TcpListener`／ASP.NET Core の参照が無いことを足す。project 参照の許可表は変えない。
- `tests/OpenLogicool.Desktop.Tests` と `ui-snapshot`: 画面の見た目。
- 完了時に関連 test を1回（Host・Desktop・Architecture・Probe）。`Capture.Tests` は窓が出るので、Bot が動いている間は走らせない。
- 実機: 段0 の表を、製品へ組み込んだ形でもう一度測る。Bot を動かしたまま配信し、Bot の観測周期・ゲームの fps・G13／G600 の取りこぼし 0 を確かめる。

## Bot 担当との調整

- 実装の前に最新の main を取り込む。`Program.cs`・`ApplicationControlRegistration.cs`・`OpenLogicool.Host.csproj` は Bot 担当も触るので、差分を小さく保つ。
- アプリの起動・終了・導入の前に、Bot 担当の会話へ一言入れる。段0 の実験は別 process（Probe と ffmpeg）で行い、動いているアプリには触れない。

## 未確認のこと（段0 で確かめる）

- Bot が動いている間も、ffmpeg の取り込みでフォーカスが動かないこと（資料では前面化の呼び出しは見当たらないが、未実測）。
- 動かない画面で、新しく開いた視聴ページに映像が出るか（コマが来ないと黒いままになる恐れ）。
- 現行の iOS の Safari で、この組み合わせ（H.264＋Opus・WHEP）が音つきで再生できるか。
- ffmpeg の WHIP の出力は公式に「experimental」。安定しなければ SRT と比べる。
- 黄色い枠（Windows が取り込み中の窓へ出す印）が出るか。Bot の取り込みが動いている間は、どのみち出る。
- ffmpeg へ渡す認証の文字列は起動時の引数に載る（同じ利用者の process から読める）。資格情報マネージャーの保護範囲と同じだが、渡し方の代替があるかを確かめる。
