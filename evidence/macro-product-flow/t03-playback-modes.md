# t03 再生モード契約

- AI監視なしは保存済みrouteを同じPurpose runtimeで再生し、正常なMovedではAIを呼ばず、route revisionを追加しない。
- AI監視なしでStayed／Undeterminedになった時は、そのstepで停止し、AI再探索とroute更新を行わない。
- AI監視ありは従来どおり保存actionを先に実行し、10秒観測後の非遷移時だけ同じstepをAI再探索する。成功時は失敗stepだけを差し替えたappend-only revisionを保存し、正常stepと旧revisionを保持する。
- focused test: `PurposeDirectedExplorationRuntimeTests` 9件 green（2026-08-26）。
