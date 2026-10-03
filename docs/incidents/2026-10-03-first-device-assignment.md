# 初回起動でG13・G600の物理押下による割当ができない

## 結論と現在地

初回割当の入力経路を修理し、開発版へ導入した。関連試験97件、空DBでのWindows native起動・正常終了、通常LauncherからのInput Studio起動を確認した。オーナーはApproval Box K-74CRW4で「G600・G13とも割当・保存できた」と回答し、実物のボタンによる割当も確認済みになった。

今回の割当不具合は修理完了。次はNIKKEの操作デモ確認（Phase 14のt08）へ戻る。Phase Exitや配布完了は宣言しない。

## 症状と原因

オーナー報告は「Aを録った後にG600のボタンを押しても反応なし」。G13でも割当できないとの報告がある。

起動前のDBにはprofileがなかった。`ResidentInputHost.Start`は実機を列挙してもprofileのない種別を`instancesByKind`から除外し、その入力sourceをpumpへ渡していなかった。UIの物理押下による割当確定はpumpのtraceを受け取るため、未設定時には押下が届かなかった。後から保存してもruntime・sourceを追加しないため、初回保存後の即時反映も成立しなかった。

両デバイスを列挙したsourceと空のruntime集合を使う最小再現では、G13・G600とも`FastPathFaultException`で失敗した。

## 修理

- 接続した実機の入力観測と、保存したprofileによる送出を分けた。未設定の実機はtrace・observerへ押下を渡すが出力しない。仮のprofileや永続設定は生成しない。
- 初回保存で、列挙済みの実機に実際のprofileからruntimeを作る。割当を決めた押下の後のupでは出力せず、次のdownから新しい割当を使う。
- 初回保存で増えた種別を前面アプリの切替対象へ加える。G600の管理開始時は、fast pathの外で既存の残置sessionを適用してから送出を開始する。以降のprofile変更・前面切替では本体へ書かない。
- 未割当のtrace表示から、出力していないのに「送りました」と表示する文言を除いた。

未知の実機ID、input drop、emitter faultで停止する既存契約と、押下時の出力だけを対応するupで解放する契約は維持した。

## 起動を妨げていたG600本体の残置

通常DBにはNIKKEの空profileが保存されていた。更新版の通常起動は、G600が中間usageで抑止済みなのに復元元ファイルがないため`RefuseAppliedWithoutBaseline`で停止した。ファイル消失の原因は未解明であり、PCリセット等を原因として断定しない。

復旧前に踏んだ別の欠陥として、probeのG600 feature選択条件が「長さが0より大きい」だけだった。実際に51-byteの別HID collectionを選び、F3を取得できない出力を保存した。製品本体の`G600FeatureHidAccess`と同じ154-byte以上の条件へ、probe内の11箇所を修理した。

修理後の二度読みは同じG600・firmware 7702・154-byte F3を取得し、F3は一致した。F6の読取り不能は既知の制約。

復元元はMigration Safety Gateの保存済みbackup `probe-output/mig01-backup-20260815/g600-backup-run1.json`。checkout上のJSONはLFだが、取得当時のCRLFへ戻すとmanifestのSHA-256 `f1a5a762520a7f21f041b10695a0d90fc77f26c521927f38b79f83d50cd6cf2b`と一致した。JSONのreport内容は変更していない。

現状のF3と復元元の差はG6〜G8・サイドボタンのcellだけで、DPI・左右クリック等は一致した。復旧前F3を保存した上で、正規`g600-restore-retry --report 0xF3`で復元し、attempt 1でfresh openのreadbackが154 bytesすべて一致した。F4・F5・slotは書いていない。その後、通常起動で154-byteの復元元ファイルが作られ、Input Studioが応答することを確認した。

## 検証と根拠

| 項目 | 結果 | 根拠 |
| --- | --- | --- |
| 未設定時のG13・G600押下観測、初回保存後のA送出 | 確認済み | 新しい再現2件は修理前に失敗、修理後に合格 |
| 入力・所有解放・切断・trace・observer | 確認済み | Input focused 28件 |
| Host入力source・出力session・前面切替・LCD選択 | 確認済み | Host focused 9件＋18件 |
| キー録画状態・キー変換 | 確認済み | Desktop focused 34件 |
| 依存境界 | 確認済み | Architecture 8件 |
| 修正版probeの実機選択 | 確認済み | build 0 errors / 0 warnings、二度読みF3一致 |
| G600 F3復旧 | 確認済み | attempt 1、fresh readback全量一致 |
| 空DBでのresident UI起動・終了 | 確認済み | `ui --resident --db <empty.db> --duration-ms 2500`、exit 0 |
| 開発版の導入・通常起動 | 確認済み | 正規install script、Host/Input DLLのRelease buildとのSHA-256一致、ウィンドウ応答あり |
| 実機押下でキーの割当・保存を確定 | 確認済み | Approval Box K-74CRW4でオーナーがG600・G13両方の成功を回答 |
| 保存内容の再読取り | 確認済み | workspace revision 3にG600 G11→A、G13 G12→Space。両profileにbindingが1件ずつ存在 |

機器選択失敗の記録は[read 1](../../probe-output/assignment-startup-20261003-032545/g600-before-read-1.json)、修理後の二度読みは[read 3](../../probe-output/assignment-startup-20261003-032545/g600-before-read-3.json)・[read 4](../../probe-output/assignment-startup-20261003-032545/g600-before-read-4.json)、復旧は[restore結果](../../probe-output/g600-restore-retry-20261003-033243-338.json)、保存内容は[workspace export](../../probe-output/assignment-startup-20261003-032545/confirmed-workspace.json)。確認用空DBと復元用の一時JSONはローカルに保持し、commitしない。

## 成立した操作

録画したキーを離した後に実機ボタンを押して割当を確定し、保存する操作が両デバイスで成立した。オーナーが実際に保存した内容はG600のG11→A、G13のG12→Spaceであり、exportでも一致した。

G600の左右クリック（G1/G2）は画面操作と区別できない既存仕様のため、物理押下による確定対象ではない。必要なら画面のデバイス図から割り当てる。
