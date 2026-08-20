# Perception corpus partition（Phase 5 t08）

探索 frame は出典を持つ `CorpusArtifact` として記録する。`CorpusPartition` は development、calibration、acceptance を別集合に保ち、recognizer の開発／校正へ渡す `TrainingCorpus` は acceptance field を持たない。acceptance は `AcceptanceCorpus` として凍結評価だけに渡す。一つの実 game 成功や acceptance 結果を一般対応・学習済みの根拠にしない。
