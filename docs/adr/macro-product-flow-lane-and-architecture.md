# ADR: Macro Product Flowのレーンと責務境界

- Status: Accepted
- Date: 2026-08-26

## Decision

AI作成、2再生mode、AI修復、合成、G13／G600割当、物理trigger、実UI／実game受入が多段に連鎖するため統括レーンとする。

Learning Route revisionを唯一のmacro正本、Visual Macroを実行projection、Workspace actionのmacro tokenをdevice割当参照、FastPathPumpの非blocking queueを物理trigger境界とする。既存Input Studioの3ペインと通常bindingを変更せず、Game Operatorの既存tab構造と右Inspectorへ最小追加する。

同一repo・同一UI／contract面を依存順に変更するためwriterは親一人に直列化し、終端のread-only独立監査だけ円卓へ委ねる。

## Rejected

- 別Macro Editorアプリ
- 新しい独自macro schema／DB
- AI監視あり／なしのrunner複製
- macroをSendInput tokenへ変換する実装
- fast pathから同期的にautomationを呼ぶ実装
- Input Studio全体のViewModel／DI framework移行

