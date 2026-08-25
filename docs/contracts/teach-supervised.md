# Teach／Supervised contract

`TeachSupervised.Request` は注入された `INextActionPlanner` から Teach proposal を一件だけ受け取り、
`TeachStepProposal`として返す。provider clientは選定・生成しない。

このmoduleは利用者承認gate、dispatch delegate、InputEmitter、device API、SendInputを持たない。実入力の受付は
10の基盤機能がcurrent frame／window／transform、Nano capability、明示Game Policyを確認して扱う。
