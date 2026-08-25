# Phase 11 Learning Console Exit判定

判定日: 2026-08-24

## 結論

**Phase 11 Exit成立。**

本書はPhase 11当時のhistorical acceptanceである。実ゲームdispatchは後続のPhase 12 Supervised Visual Macro Runnerで成立済み。`Confirmed`／destination一致を要求した旧記述は現行操作gateではなく、現在は10秒の`Moved`を進行条件とする。

Game Operatorは、保存済みGame Structureから人が読める操作列を作り、利用者が修正し、理由付きの不変revisionとして保存し、検証付きVisual Macroへ変換できる。Visual Macroは操作前後の画面を決定的に監査し、`Confirmed`以外では継続を許さない。

この判定は「学習コンソールと検証付きマクロ生成」までであり、NIKKE全日課の無人完遂、実ゲームへの生成macro dispatch、AI provider選定を含まない。

## 受入matrix

| # | 条件 | 判定 | 根拠 |
|---:|---|---|---|
| 1 | 保存済みScreen Graphのedge列を人間向けstepとして閲覧 | 確認済み | `HostLearningRouteIntents`が構造node／edgeを画面用語へ投影 |
| 2 | source、target、primitive、期待destination、根拠、riskをstep単位で表示 | 確認済み | `LearningRoutePanel`の詳細ペインとDesktop focused test |
| 3 | 追加、削除、並べ替え、差替え、理由付き新revision保存 | 確認済み | Desktop workspaceと実SQLite Host scenario |
| 4 | 保存と元に戻すを右下へ固定 | 確認済み | Windows実画面でfooter右端の配置を目視 |
| 5 | 旧revisionを保持し元案と比較可能 | 確認済み | append-only SQLite storeとrevision履歴test |
| 6 | 不連続、retired、destination不明、別environment、禁止riskを拒否 | 確認済み | validator／compiler focused test |
| 7 | valid routeをVisual Macro化し、Supervised／Verifiedを分離 | 確認済み | `VisualMacroCompiler` focused test |
| 8 | local observationから操作前stateと期待destinationをAIなしで監査 | 確認済み | `VisualMacroAuditor` focused test |
| 9 | `Confirmed`以外で継続せずblind retryしない | 確認済み | auditorの`CanContinue`契約と全分岐test |
| 10 | Phase 10 NIKKE知識をgame固有コードでなくimport可能データ化 | 確認済み | `fixtures/knowledge/nikke-daily-phase10.v1.json`とimport test |
| 11 | 変更直結Windows focused testと実SQLite scenarioがgreen | 確認済み | Persistence 2、Playbooks 11、Desktop 2、Host 5の計20件green |
| 12 | 公開claimを成立範囲へ限定 | 確認済み | 本書の結論と下記claim境界 |

## Windows実画面

WindowsネイティブbuildからGame Operatorの「学習した操作」タブを直接表示し、3ペイン、編集操作、指示欄、監査状態、右下の`元に戻す`／`検証付きマクロを生成`／`保存`を目視した。

初回目視で、暗色背景上の見出しが黒く読みにくいことと「構造探索」タブが幅不足で省略される欠陥を確認した。`LearningRoutePanel`へtheme色を明示し、各タブへ最小幅を与えて再表示し、可読性とタブ全文表示を確認した。目視専用の一時起動分岐は確認後に除去し、製品契約へ残していない。

## 検証

- `dotnet build src/OpenLogicool.Host/OpenLogicool.Host.csproj --no-restore --nologo`: 成功
- Persistence focused test: 2件成功
- Playbooks focused test: 11件成功
- Desktop focused test: 2件成功
- Host focused test（実SQLite scenarioを含む）: 5件成功
- `git diff --check`: 問題なし
- CI追加、cross-platform matrix、無関係な全test反復: 実施しない（Windows専用・Phase 11計画どおり）

## claim境界

成立した公開claimは次だけとする。

> Game Operatorは、探索済みゲーム構造から操作ルートを可視化・訂正・版管理し、画面状態による継続判定を持つ検証付きマクロへ変換できる。

次は未確認であり、このExitへ含めない。

- 生成したVisual MacroのNIKKE実ゲームdispatch
- NIKKE全日課の無人完遂
- AI修復を含む長時間自律運転
- 一般gameでの成功保証
- 外部AI APIまたはprovider

## 円卓

Peertableの改修はオーナー指示により別sessionへ分離した。本Phaseの本体実装、Windows focused test、実画面確認、Exit裁定はOpenLogicool repo内だけで完結し、Peertable／Dotagentを変更していない。
