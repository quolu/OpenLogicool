# Planner proposal contract

`PlannerContext` は目標、許可された semantic action 集合、履歴要約、proposal 数と費用の budget を運ぶ。
`NextActionProposal` は mode に対応する action、precondition、expected outcome と stability window、stop、
frame と transform の validity を一つの提案として運ぶ。

`PlannerProposalSchema.Validate` は dispatch 前の製品 schema 検証口である。最上位と全ネストの
schema version は `0.1.0` だけを受理し、未知版・空の必須 field・負または非正の budget／期限／
stability・mode と action の不一致を拒否する。既定値への丸めや schema version の fallback はしない。
