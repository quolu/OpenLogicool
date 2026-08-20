# Verified environment scope

`VerifiedEnvScope` は Verified の根拠を得た環境 ID を保持する pure value である。

- `AppliesTo` は環境 ID の ordinal 完全一致だけを受理する。
- したがって `gamelab:*` で Verified になった根拠は、異なる `game:*` の実 game 環境へ継承されない。
- 本口は input、capture、provider、永続化を参照しない。
