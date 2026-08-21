# Active Run update hold

`ActiveRunUpdateHold` は配布側が update を開始できるかと、update 後に Run を再開できるかを判定する pure contract である。

- active Run があるときは `HeldForActiveRun` を返す。呼び出し側はこの決定で `InstallLifecycleAction.Update` を開始しない。
- active Run が無いときだけ `Allowed` を返す。
- resume は Run が既に pin した artifact version と、installed artifact version が ordinal 完全一致のときだけ `Compatible` である。
- version が異なるときは `Incompatible` であり、互換性の推測、自動 migration、別 version への切替は行わない。

この型は active Run の状態遷移、Playbook version pin、`InstallLifecycle` の操作列を保持・再実装しない。既存の各責務から得た事実だけを入力にする。
