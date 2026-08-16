# t07-onboarding-core 完了証跡

- 実装: implementer（sonnet×medium）委譲・統括が focused test 再実行と実走（データ有り DB）で受入
- CLI `onboarding`: 共存ソフト実行中検出（LGS/G HUB/Logi Options+・事実表記のみ・検出時は共存注意1行）、device 接続（片側/両側未接続の明示＝Exit 条件4 機能面）、G600 完全 backup 導線（mig01-backup 実在＋21 file・不在時は Migration Safety Gate 案内）、設定の現在地（profile/関連付け/workspace 件数）。read-only・常に exit 0
- pure builder（OnboardingReport.Build）と thin I/O collector を分離。focused test 8件
- 検証: Host 18件（+8）・Architecture 4件 green（worker＋統括の両方）。実走は worker（空 DB・接続2台・backup あり）と統括（データ有り DB）の両方で exit 0
- 未実測: 共存ソフト「検出時」の実走（環境に LGS 等が非稼働のため。focused test で担保）
