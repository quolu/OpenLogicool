# t02-playbook-graph

- plan: `phase4-durable-lab`
- implementer worktree 受入（親が diff 実読＋指定 test 再実行）
- Playbook graph: 前提・状態・Semantic Action・期待結果・分岐。到達不能 node は例外
- Run 開始で VersionId を pin。訂正は ParentVersionId 付き新 version
- focused test: Playbooks 19 / Architecture 4 / Domain 8、Release 全 green
- journal / Attempt / GameLab は未着手
