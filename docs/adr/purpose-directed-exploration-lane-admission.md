# 目的指向の逐次探索を統括レーンで実施する

## Decision

目的指向の逐次探索campaignは、契約、逐次永続化、再起動再現、実game受入、最終回帰、公開可能範囲の裁定が多段に連鎖し、最終裁定の検証可能な証跡を必要とするため統括レーンで実施する。

工程の正本はLattice plan `purpose-directed-exploration`、目的と受入条件の正本は`docs/purpose-directed-exploration-campaign-plan.md`とする。実装は共有dirty treeを保護する同一親が直列に行い、別作業所有の3差分と既存未追跡probe出力を読まず、変更せず、commitへ含めない。
