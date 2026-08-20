# Teach／Supervised contract

`TeachSupervised.Request` は注入された `INextActionPlanner` から Teach proposal を一件だけ受け取り、
`PendingTeachStep` として返す。provider client は選定・生成しない。

`Approve` は利用者の明示 `approvalId` を受けて `ApprovedTeachStep` を返す。それ以前の型は承認待ちであり、
この module は dispatch delegate、InputEmitter、device API、SendInput を持たない。実入力の実行はこの口の外にある
既存 dispatch 境界だけが扱う。
