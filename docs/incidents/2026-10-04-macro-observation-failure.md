# 日課再生の操作後観測エラーを別の契約エラーで隠していた欠陥

## 実際の停止

利用者が候補版4の「日課をこなすぞ」をAI監視なしで再生し、不成立を報告した。12:45開始の保存画像とjournalを照合すると、最初の2操作は記録と同じ「前哨基地の防衛」→「殲滅の案内」を開き、Movedを保存した。3操作目はDispatchArmedまで残り、その後のoutcomeは保存されていない。製品UIには「after Observationまたは安定窓がproposal契約を満たしません。」と表示されていた。

Nanoのクリックが最初から届かない状態とは異なる。3操作目の入力結果、最初に発生した取得・解析エラー、以降の48操作は未確認。旧実装が元の失敗を保存していないため、このrunの元の原因を画像鮮度や利用者の前面切替と断定しない。

## 再現した原因と修理

操作後の観測が0件の時、`ProductGameExplorerRuntime`が操作前のsceneを操作後として学習へ渡していた。Coordinatorは新frameを要求して契約エラーを出し、実際の取得・解析エラーと停止位置がUIへ届かなかった。操作後の観測欠落をnullのまま扱い、入力receiptと実エラーを持つOutcomeUnknownをdurableに保存するよう修理した。観測を捏造せず、停止した手順を通常の停止結果として返す。

Coordinatorは静止WGCの同一frameから得たOutcomeUnknownにもsequence増加を要求していた。同一frameの再観測は未判定として保存し、Movedの増加条件と安定条件は維持する。

別の再現欠陥も確認した。保存画像を製品のWindows OCRとdiscoveryへ通すと、鮮度1001msのframeでmatcherが取得状態をStaleへ変更し、元のAvailable Observationとの束縛検証が失敗した。discoveryは入力Observationの取得状態を保持するよう修理した。鮮度の実測値1001msは保持し、入力前の1000ms条件も変更しない。この経路が利用者の3操作目で発生したことは未確認。

## 検証と現在地

- 確認済み: 欠落観測と同一frame未判定の試験は修理前に失敗し、修理後に通過。実CoordinatorとLearnerを通し、再起動復元でOutcomeUnknown、操作後IDはnull、架空のObservation eventなしを確認。
- 確認済み: 保存済みの最終画像を製品OCR/discoveryへ通したWindows native測定で、鮮度1001msの束縛エラーを再現。修理後は取得状態と元の鮮度を保持して解析できた。AI呼出しとゲーム入力は0。
- 確認済み: 関連focused 110件、最終全22テストプロジェクト1,435件通過。
- 確認済み: 正規開発版installerで導入し、通常終了・ランチャーから再起動、Game Operatorのマクロ一覧に日課51手順・版4が残ることを確認。Host PIDは36592。導入先のHost／Exploration／Contracts assemblyとRelease出力のSHA-256が一致。
- 確認済み: 原本298ファイル、session 8行、demonstration event 193行、route revision 10行、workspace／mapping／関連付け各4行は修理前と不変。
- 未確認: 更新版の日課全再生・完遂と、利用者が見た停止以外の挙動。利用者へ具体的な挙動を質問し、回答待ち。

今回の診断ではゲームへの再入力を行っていない。元のrunのoutcomeを後から成功として補完せず、Phase Exitを宣言しない。実測概要は[受入記録](../../probe-output/macro-observation-failure-20261004.json)。
