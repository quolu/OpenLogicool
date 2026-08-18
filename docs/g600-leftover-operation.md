# G600 出荷割当の残置無害化（B変種）

Input Studio が G600 を管理している間、本体の side 割当（例: G9→`1`）が legacy keyboard へ漏れるのを止める。方式は Phase 2 で実証済みの B変種: F3 の G9〜G20（通常層＋G-Shift 層）を中間 usage F13〜F24 へ書き、raw 0x80 は Input Studio が読む。

## いつ書くか

| 時点 | 動作 |
|---|---|
| `run` / `ui --resident` 開始（G600 が配線されている） | apply（残置） |
| handled shutdown | restore（baseline へ戻す） |
| foreground 切替・workspace 保存 | 書かない（MAP-010） |
| LGS / G HUB / Options+ 実行中 | 書かない（二重入力のまま・想定動作） |

F3 のみ。F4/F5・slot 切替中の他 slot は対象外。

## 作法

- write 前に現在の F3 を `{db と同じディレクトリ}/g600-onboard-baseline-f3.bin` へ保存する
- fresh open → settle 2s → SET_FEATURE → close → fresh open → settle 2s → GET_FEATURE
- handle を再利用しない。byte 一致まで最大 8 回再送
- 既に中間 usage で baseline があるときは書き直さない
- 既に中間 usage なのに baseline が無いときは何も書かず停止（`g600-restore-retry` で戻す）

正: [rag/openlogicool/g600-write-protocol-2026-08-15.md](../rag/openlogicool/g600-write-protocol-2026-08-15.md)

## 実機確認（オーナー手番）

LGS を止めてから:

```
OpenLogicool.Host leftover status
OpenLogicool.Host leftover apply
```

G9 を押して本体の「1」が出ないこと、Input Studio の割当だけが届くことを確認する。戻すとき:

```
OpenLogicool.Host leftover restore
```

常駐中の旧 exe は再起動しないと新経路に乗らない。
