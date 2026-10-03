# 別々に学習した往復マクロの画面ID不一致

## 結論

確認済み。保存済みの「アークを開く」「ロビーへ戻る」を製品GUIから2手順に統合し、通常起動と別Host processへの再起動後の両方で、Nano USB HIDによるロビー→アーク→ロビーを確認した。表示は両方「目的を完了しました。 step 2 CODEX Moved AI 0回 版 1」。再生によるrouteの追記はなく、元の2本・旧Structure Event・利用者の記録原本も不変。

今回の受入は統合障害の修理。Phase 14 Exit、配布完了、利用者デモからのマクロ作成工程の成立には読み替えない。

## 原因の実測

`GameInteractionStructureLearner.EnsureNode`は、`GameSceneSemanticComparer.SignatureId`の完全一致だけで既存nodeを選んでいた。signatureは認識状態、学習済みstate候補、OCRを含むaffordance集合をhash化する。そのため、初回に未知だった画面が次回に学習済みとなるだけでもhashが変わる。OCRの読み取り結果や学習済み操作の追加も観測版の差になる。

初回のアーク到着はNovel・state候補なし、次の「戻る」開始はKnown・アークの学習済みstate候補あり。保存画像の8×8輝度signatureを直接比較すると、同じアーク2枚の平均絶対差は0.046875、ロビーとの平均絶対差は37.578125だった。表示名はどちらも`0NIKKE`だったが、表示名を同一性の根拠には使っていない。

| 境界 | 保存された識別子 |
| --- | --- |
| アーク到着 | `state:c615df2c4f4a4b9ab418f9ffc3ae78d7`、観測`observation:window:codex:7892:45:25` |
| 戻る開始 | `state:eff05e12ad244c8fb14bdac791d67f9e`、観測`observation:window:codex:7892:4:2` |

`StructurePlaybookSynthesizer`が「Structure edge列が連続していません。」と拒否するのは正しい。修理前はこの検証に渡す画面identityが分かれていた。

## 修理

- `StructureSceneIdentityResolver`を画面構造の所有モジュールであるExplorationへ追加。新規学習は、signature完全一致に加えて、既存edgeに結び付いた保存観測から同一画面のnodeを再利用する。画面の比較は既存の安定判定と同じ輝度差6未満を使い、captureの有効性・backend・画像寸法を照合する。画像signatureがない観測は意味構造の根拠を要求する。
- 既存マクロを統合する時は、直前edgeのafterと次edgeのbeforeが同一であることを保存証拠から確認し、正規の`StructureKnowledgeController`へ`MergeNodes`を追記する。nodeを原本から消さず、旧IDは退役したvariantとして残し、現在投影のedgeを正規IDへ帰属させる。旧eventと旧route revisionは書き換えない。
- 複数境界をつなぐ場合も各nodeの証拠を直接比較する。近い画像の連鎖だけで別画面まで統合しない。連続性検証は従来どおり必須で、実際のprojectorによる訂正後の構造で合成を検証してから保存する。
- 観測IDがprocess再起動で再使用された場合は、そのedgeが作られたrevisionより前の保存観測を読む。後の再生観測で過去の証拠を置き換えない。

## 導入と実UI受入

起動中の開発版を通常終了し、`scripts/install-development-app.ps1`で更新。標準Launcherから起動した製品UIで、保存済み2本を順に統合して保存した。元の2本を再学習したり、DBを手で補正したりしていない。

統合名は「アークを開いてロビーへ戻る（Nano確認）」、2手順・版1。画面IDの訂正は1回のmergeだけで、元のrouteの版2もそのまま残った。

| 実行 | 根拠の状態 | 結果 |
| --- | --- | --- |
| Host PID 46832、`exploration:f1a9e81affc24ac09fd05b1d5f7a0042` | 確認済み | AIなし再生。2クリックとも`NanoSerialHid`、両方Moved、完了。証拠ディレクトリ`20261003-202901-355` |
| 通常終了・再起動後のHost PID 4924、`exploration:d4b11958e38b48b3b9a12c214c93b4ad` | 確認済み | 同じ保存versionをAIなし再生。2クリックとも`NanoSerialHid`、両方Moved、完了。証拠ディレクトリ`20261003-203224-600` |

いずれも実NIKKE画面のロビー復帰と、前面に出したGame Operatorの完了表示を照合した。ゲームへのComputer Use入力・SendInput・fallback・自動retryは0。Computer Use skillは製品GUIの操作とゲーム画面の読み取りに使用した。

正本DBをSQLite backupで保存してからGUI検証し、旧route JSON4件と旧Structure Event36件の一致、`demonstration_sessions`／`demonstration_events`の全行一致を確認した。統合routeはrevision総数1・最新版1、同じedge2件で不変。

## 試験と証拠

Windows nativeの関連試験はHost 317件、Exploration 64件、Playbook合成・連続性7件、計388件が成功。追加した実録fixture試験では、Novel→Knownのアーク同定とnode再利用・統合、別画面の拒否、証拠不足の拒否、観測ID再使用時の過去証拠保持を確認した。修理と無関係な全体regressionは再実行していない。

公開可能な結果は[実UI検証結果](../../probe-output/nano-macro-composition-20261003.json)、回帰fixtureは[実録の画面同定データ](../../fixtures/game-structure/nikke-ark-scene-identity.v1.json)。fixtureは保存観測から画像のローカルパスとAI応答を除いた派生物で、実録の識別子・認識状態・画面輝度signatureを保持する。実画像は`%LOCALAPPDATA%/OpenLogicool/macro-evidence/`、変更前のDBと比較用の実データは非公開の`artifacts/diagnostics-tools/macro-composition-20261003/`に保持する。
