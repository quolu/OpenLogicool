# Proposal reject gate

`ProposalReject` は AI proposal を dispatch 前に照合する Playbooks の pure gate である。

- schema validation に失敗した proposal は `Schema` として拒否する。
- Verified Run action が `PlannerContext` の許可集合または `SemanticActionCatalog` に無い場合は
  `Catalog` として拒否する。
- precondition state が現在 state と異なる場合は `State` として拒否する。
- catalog の `RiskClass` が dispatch context の期待 risk と異なる場合は `Risk` として拒否する。

この gate は dispatch、InputEmitter、device API、永続化 API を持たず、判定だけを返す。従って
拒否された proposal は外部入力へ到達できない。catalog と risk を照合できない Teach action も
`Catalog` として拒否し、Supervised の承認経路が追加されるまで dispatch しない。
