# Frozen metrics

`FrozenMetricRunner` は `AcceptanceCorpus` の各 artifact に一件の固定評価 case を要求する。training corpus は API に現れず、acceptance の結果で recognizer や閾値を調整しない。

- Known でない期待を Known にした件、Unknown を Known にした件、再開不可の case を dispatch した件は、それぞれ 0 件だけを合格とする。
