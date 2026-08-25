# t03 実SQLite再起動AI 0再現

- 一回目のconnectionで決定的purpose routeをSQLiteへ保存した。
- connectionを閉じ、同じDBを新しいconnectionとpurpose runtimeで再openした。
- game／environment／goalから同じroute IDを解決し、保存edge 2件を順番どおりhintへ渡した。
- replay中のLearning Route追記は0、既存revisionは1件のまま。
- focused testはin-memory 3件＋実SQLite再open 1件、計4件green。
- Windows file lockを避けるtest DBは`Pooling=false`でconnection寿命を明示した。
