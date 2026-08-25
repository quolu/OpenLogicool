# t02 逐次route追記と失敗step修復

- goal、決定的route ID、route cursorを所有する`PurposeDirectedExplorationRuntime`を追加した。
- 初回は`Moved` edgeだけをappend-only Learning Routeへ逐次追記する。
- `Stayed`／`Undetermined`は`LearningContinues`として保持し、同じstepだけを修復する。
- 修復成功時は当該edgeだけを差し替え、正常stepと旧revisionを保持する。
- 保存route再生はroute revisionを書き直さない。
- focused test 3件green。
