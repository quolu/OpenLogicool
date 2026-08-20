# Shadow compare

`ShadowCompare` は利用者の semantic action ID と planner の proposal を比較する pure な観測口である。

- `VerifiedRunAction` の semantic action ID が利用者操作と ordinal 一致する時だけ一致とする。
- Teach proposal は比較できないため不一致として返す。
- planner の呼び出しと比較だけを行い、dispatch、SendInput、承認、Playbook 書換え、本番 provider を持たない。
