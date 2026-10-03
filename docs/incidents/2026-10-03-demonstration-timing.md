# NIKKE操作デモの入力欠落と画面束縛の遅延

## 判定

記録停止と保存は確認済み。利用者が行った「アークを開いてロビーへ戻る」の2操作を原本へ保存する受入は不成立。今回の原本から往復マクロを作れるとは判定しない。

利用者へ各画面で60秒待つよう依頼したが、利用者は待たずに操作し、終了表示を確認した。左下の画面ボタンでロビーへ戻ったという回答を得た。操作間隔と最初のクリック時刻は未取得。待たなかったことを利用者の失敗とは扱わない。

## 実測

対象原本は `demo:0b197a6355374be4b3367a569682fb49`。目的は「アークを開いてロビーに戻る」。本番DBとWGC証拠は利用者端末に保持し、調査では読み取りだけを行った。

| 時刻（日本時間） | 取得された内容 |
| --- | --- |
| 13:11:11.725 | session開始 |
| 13:11:11.785 | 初期WGC画像。目視ではロビー |
| 初期画像の解析 | Foundry Local Qwen3-VL-4Bが30,008msでTimeout |
| 13:11:50.002 | 唯一の操作。クリック位置は正規化座標 `(0.072098, 0.930385)` |
| 13:11:51.151 | 次のWGC画像はロード画面。AI応答はInvalidResponse |
| 13:11:58.533 | WGC画像はロビー。AI解析は10,844msでCompleted |
| 13:11:54.962 | 対象アプリからの前面切替。ただし保存処理は操作判定の終了待ち |
| 13:12:35.216 | 操作を保存。45,076msで安定待ちTimeout、CompareはUndetermined |
| 13:12:35.236 | Stoppedを保存 |

原本はOperation 1件、FocusLost 1件、Stopped 1件。After.StableSceneはなく、失敗理由は「観測処理がtimeoutを超えました。」。クリック位置と利用者の説明は戻る操作と整合する。しかし操作前として束縛された画像はロビーであり、アーク画面のbeforeは取得されていない。

最初のアークへのクリックが初期解析中に行われたという説明は**強い推定**。最初のクリック時刻を取得していないため、今回の欠落箇所の実機時系列までは確認済みとしない。GPU負荷やhookの失効を原因とする証拠は得ていない。

## 製品経路で確認した原因

1. `HostDemonstrationRecordingIntents.StartAsync`は記録器の初期ObserveとDiscoverTargetsをawaitした後にcollectorを開始する。初期画像取得後もAI完了待ちの間は入力の受け口がない。public intentsからのfocused再現では、初期解析中と開始完了後に1回ずつクリックし、期待2操作に対して実保存1操作となった。診断試験は期待通り失敗し、sourceとTRXを `probe-output/demonstration-timing-20261003/` に保存した。試験sourceは通常test projectから外し、製品コードは変更していない。
2. `DemonstrationRecordingPump`は1操作のWaitStableが終わるまで後続の入力とfocus処理を進めない。`DemonstrationRecorder`は前の操作の安定後sceneを次のbeforeとする。取得時刻と処理時刻を分けていないため、短い間隔で行った実操作に当時の画面を保証できない。今回も操作の保存とFocusLostの保存が45秒の判定待ちになった。
3. `DemonstrationRouteCompiler`は訪問済みsignatureへの戻りを常にExcludedDetourとする。ロビー→アーク→ロビーという目的で両操作を正しく記録できても、最後の戻りはrouteに採用されない。このルール変更は既存の寄り道除外契約に触れる。

安定判定runtimeは観測区間の最後まで確認し、遅い画面変化で初期の安定を取り消す契約を持つ。45秒を途中で打ち切って成功にする修理は行わない。AI timeout、InvalidResponse、判定不能をMovedへ置き換えない。

## 承認された修理範囲

利用者の入力・対象画面の取得を、AI解析と候補route作成から分離する。

1. 対象windowのWGC取得と入力受理を開始する。AI解析は記録の開始条件にせず、入力受理が実際に始まった時点でUIに「記録中」を表示する。初期画像が取得できない場合は開始失敗を明示する。
2. 明示的な記録区間だけ対象windowのframe列を保持し、入力・focus・座標transformを取得時刻で関連付ける。押下前に取得済みのframeをbeforeに使う。hookとdevice fast pathでWGC、AI、SQLiteを待たせない。他appの入力を取得しない境界を維持する。
3. 記録停止で入力と画像の取得を閉じ、保存したframeを既存の認識・Observe・WaitStable・Compareへ接続して解析する。解析中はUIへ進捗と失敗を表示する。入力が続いた区間や証拠不足は判定不能として保持し、後から現在のゲーム画面で古い操作を判定しない。
4. 10秒Compareの既存条件を維持するため、次の入力を跨ぐ観測の扱いを記録timelineの契約として明示する。短い操作間隔でもMovedを保証するとは約束しない。記録の欠落と、証拠不足による判定不能を分けて表示する。
5. 訪問済み画面への戻りは自動で寄り道と決めず、利用者の目的と候補route上の採否に基づいて扱う。往復目的で最後の戻りを保持する。判定不能のstepを成功扱いする変更や、座標列の直接再生は加えない。

所有範囲はDemonstrationのcontract／store／recorder、Windowsの対象frame取得adapter、Hostのlifecycle、記録UI、route導出。既存再生器、Nano出力、Input Studioの割当、device writeは再設計しない。既存原本は保持し、新しい記録形式はversionを上げる。

初期解析中の2クリック欠落、解析待ち中の複数操作とbefore束縛、前面切替直後の取得抑止をfocused試験で確認する。Windows self-windowと実NIKKEで通常の操作間隔を測り、取得件数・画面束縛・判定結果を別々に照合する。往復が判定不能なら候補マクロの成立とは扱わず、残る原因を報告する。開発版installと同じ実UIでの受入まで実施する。

この本体変更はApproval Box K-N2UDGCで、修理・検証・開発版更新まで承認された。K-3LQUGAで利用者によるOpenLogicoolの通常終了も確認した。

## 実装と検証

- 実記録を`DemonstrationTimelineRecording`へ接続。取得中にAIを呼ばず、入力とWGC画像をschema `0.4.0`の原本へ保存する。入力のdesktop絶対座標と他appのpathは原本へ保存しない。
- 停止時に入力・frame取得を閉じ、原本の保存を完了してから`DemonstrationTimelineAnalyzer`で解析する。解析画像はSHA-256を検証し、原本のframe／transformへ束縛する。稼働中のゲームから画像を取り直す入口を解析器へ渡さない。
- 安定判定はAI処理時間でなく原本の観測時刻を使い、既存`GameInteractionStabilityRuntime`と`GameTransitionJudge`を通す。区間は次の入力・focus喪失・停止で閉じ、上限10秒。短い区間や証拠不足はUndeterminedで保存する。
- 候補マクロはMovedを記録順で採用し、訪問済み画面への戻りも保持する。Stayed／Undeterminedと同じ遷移の重複は理由を残して除外する。
- UIへ原本保存後の解析状態と進捗を追加し、終了時に判定不能の件数を明示する。取得・保存・画像読戻しの失敗を成功扱いしない。

最初のnative測定では2クリック保存まで成立したが、静止したself-windowのWGCが新frameを出さず、安定判定は2件ともUndeterminedとなった。調査で同じframeの観測時刻が欠けていたことを確認し、画像識別子と各回の観測時刻を分離した。同じPNGは共有し、画面の取得時刻・識別子を捏造しない。このケースのfocused再現も追加した。最初の失敗報告は保持する。

再測定は`probe-output/demonstration-timeline-smoke-20261003-133927-619/report.json`。Windowsの実self-window、WGC、Nano物理HID入力、Foundry Local、実SQLite、Host public intentsで次を確認した。

| 項目 | 根拠の状態 | 実測 |
| --- | --- | --- |
| AI待ちを含まない記録開始 | 確認済み | 450ms |
| 通常の数秒間隔で2操作を保存 | 確認済み | クリック時刻の差3,449.6155ms、保存2操作 |
| 2操作のbefore／afterと安定判定 | 確認済み | frame 1→2、frame 2→3。安定区間2,105ms／2,099ms、両方Moved |
| 戻りを含む候補マクロ | 確認済み | 2手順 |
| SendInput／Computer Use／外部AI API | 確認済み | すべて0 |
| 新方式のNIKKE実UIと可逆往復 | 未確認 | 開発版更新後の利用者確認で実施 |

関連試験88件はすべて通過した。内訳はHostの操作デモ・安定判定57件、Desktopの記録workspace 3件、候補route 11件、Probeの観測判定9件、architecture 8件。新規試験はAIが停止中の2クリック保存、操作ごとのbefore束縛、遅い画面変化、対象外入力抑止、静止WGCの反復観測、PNG読戻し・改変拒否を含む。Phase最終のfull regressionはこの修理の個別確認では実行しない。

正規入口`install-development-app.ps1`による開発版更新は成功した。通常起動後のNIKKE実UI確認は未確認として保持する。

## 新方式のNIKKE実UIで確認した残存欠陥

K-ZNRR6Hでは「2操作とも画面が変わった」と回答されたが、13:52開始の`demo:8c39ba807b834615a7d5031109c13905`は2操作ともUndeterminedだった。K-4ZYJWJで利用者が同じ記録「アークからロビー」を再確認し、実UIも2行とも「判定できず」と回答した。表示と保存内容は一致し、最初の回答をMoved成立の根拠には使わない。

入力原本のクリック時刻は13:52:29.876と13:52:33.383、間隔は約3.51秒。beforeはframe 37（ロビー）と57（アーク）へ別々に束縛され、保存画像には最後のロビーも残っている。入力取得の欠落は解消した。解析結果は最初の操作のafterがframe 47／53／57、次の操作が69／74／78で、どちらも安定待ちTimedOut。

原因は解析器が保存frameを1秒間隔へ間引くこと。最初のafterはframe 49から57まで1.44秒、次のafterは72から78まで1.18秒の画像が残っている。既存の8×8輝度指紋と同じ計算による最終画像との差は、49が4.71875、72が4.640625で、既存の意味安定比較の視覚差条件6未満に入る。しかし49と72を解析対象から捨てるため、最終画面が安定し始めた取得時刻を遅らせていた。指紋差だけでMovedとは裁定せず、既存の認識と安定判定による再解析で確認する。

同じ取得間隔と読み込み→遷移後画面を使うfocused再現は、修理前にMoved期待に対してUndeterminedで失敗した。保存frameの間引きを除去し、既存の1秒・2観測の安定条件、因果区間、遅い変化の検出は維持する。修理後のTimeline試験7件は通過した。

診断Probe`demonstration-timeline-analysis`は原本schema 0.4のPNG・入力・取得時刻を読み、SQLiteの読み取り専用接続から独立DBへsnapshotを取って新しい診断sessionを作る。元のsessionと画像は変更しない。live capture・入力出力を持たず、通常のFoundry Local／OCR／認識compositionと`DemonstrationTimelineAnalyzer`を通す。再解析の成功を新しい実UI受入の代わりにはしない。

再解析は`probe-output/demonstration-timeline-analysis-20261003-140239-312/report.json`で2操作ともMoved。before／afterは37→57と57→78、安定区間は1,444msと1,177ms。22枚の保存画像を解析し、LiveCapture／SendInput／Computer Use／外部AI APIはすべて0。修理前後で元画像と操作時刻を維持したまま、既存判定器で往復が成立した。関連試験はTimeline 7件と、それ以外の記録・安定判定51件の合計58件が通過した。修正版の実UI確認は開発版更新後に実施する。

修理は`a1b7e90`としてmainへpush。K-SFMP7Zで通常終了の回答を受け、Host processの終了を確認してから正規installerで開発版を更新した。Launcherからの通常起動、Input Studioのwindow titleと応答、install済みHost DLLとRelease buildのSHA-256一致、Hostのsource revisionが`a1b7e90`であることを確認した。次は修正版でのNIKKE記録と候補マクロ作成の実UI確認。
