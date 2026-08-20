# EXP-AI-01 evaluation harness

`EvalHarness.Measure` は Phase 5 で凍結した corpus item と対応する `PlannerContext` を入力に、注入された
`IFrozenProposalEvaluator` の proposal を測定する。harness 自体は provider client、credential、prompt、
dispatch を持たない。

既知 item は action key の一致から正確さを、unknown item は proposal が返らないことから棄却率を集計する。
各 response の latency と cost を合算し、cancel 済みなら後続 case を評価しない。結果は測定 report だけであり、
acceptance corpus を prompt 調整 API に渡す口はない。
