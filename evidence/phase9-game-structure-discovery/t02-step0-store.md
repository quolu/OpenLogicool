# t02 STEP 0 store evidence

- 実装commit: `1b3ecf3`
- 永続化: `IWebReferenceStore` と SQLite migration 7/8 を追加し、source、document revision、fact、contradiction、research run、除外、再取得要求を再起動後に復元する。
- append-only: 通常APIはappendだけを公開し、documentはsource内でrevision一意、document／fact／contradictionの親鎖は直前revisionだけを許可する。
- 参照整合性: fact／contradiction／research attemptのsource、document、fact、contradiction参照を保存前に検証する。
- SummaryOnly: wire型が短い根拠、構造化要約、候補factだけを持ち、raw HTML／全文Markdownを保存できない。
- 削除: previewとtombstoneがdocument、fact、contradictionの全IDとpayload bytesを表示する。fact↔contradictionとrevision descendantsを相互再帰closureで求め、transaction内でpayloadを物理削除して墓標だけを残す。
- export: 削除済みpayloadを含まず、全append-only revisionと制御記録をversioned bundleとして返す。
- schema: 列とJSON payloadの双方をfail-closedで検証し、未知versionを黙って読み飛ばさない。

## 検証

- focused: `dotnet test tests/OpenLogicool.Persistence.Tests/OpenLogicool.Persistence.Tests.csproj --filter 'FullyQualifiedName~SqliteWebReferenceStoreTests|FullyQualifiedName~SqliteMigrationRunnerTests' --no-restore`
  - 16件成功、失敗0
- related: Persistence全体、Conformance全体、Architecture全体を順に実行し、すべてexit 0。
- 独立反証: Fable系read-only監査で初回6件、再監査3件、最終1件を修正し、限定最終確認は残存P0/P1/P2なし。
- `git diff --check`: error 0（既存のWindows改行変換warningのみ）。
