# Observe Only

`ObserveOnly` は `INextActionPlanner` から `NextActionProposal` を取得して利用者へ返すだけの
Playbooks API である。

- Attempt、RunJournal、dispatch delegate、InputEmitter、PlaybookVersion を参照しない。
- proposal は観測結果であり、外部入力の実行や Playbook version の書換えを発生させない。
- Teach／Supervised の承認・dispatch はこの API の責務外であり、別の明示経路だけが担う。
