# t07 resident macro automation worker

- UI手動作成／再生とG13/G600物理button queueは同じ`HostMacroAutomationIntents`／`IProductMacroExecutionEngine`へ接続した。
- `run`と`ui --resident`のFastPathPumpはmacro tokenを有界queueへ渡し、専用workerがFIFOで逐次実行する。fast pathはAI／capture／SQLite／UIを待たない。
- product executorはProbe subprocessを使わず、Windows WGC、Windows OCR、Foundry Local、Nano、10秒Compare、durable attempt、Structure／Learning Route storeをHost process内で直接合成する。
- resident出力がSerial HIDなら既存Nano sessionを借用し、二重COM openをしない。SendInput resident／非resident UIだけ専用Nano sessionを開く。
- AI監視なしはFoundry CLI／modelの成立を要求せず保存routeだけを再生する。AI監視ありだけWindows vendor adapterで実endpointとloaded multimodal modelを解決する。
- AI監視なしは`allowAiDiscovery=false`をWindows known-first境界へ渡し、保存座標を解決できない場合もAI providerへ到達せず停止する。Stayed evidenceはdurableに残すがStructure／routeは変更しない。
- macro runtimeのStructure／Learning Route／Learned Profile／Run Journalは同期操作ごとにfresh SQLite connectionを開き、awaitを跨いで同じ`SqliteConnection`を別threadへ持ち越さない。
- 物理button起動の失敗は`Faulted` stateとしてGame Operatorへ通知する。終了時はactive macroのcancel完了を待ってからNano session／queueを破棄する。
- route edgeへoperation parameterを保存し、統合macro内でもclick／hover／key／scroll／dragを各edgeの操作として再生する。AI discoveryは保存actionなし／10秒非遷移ごとに再度呼べる。
- focused test: Host macro coordinator／worker／Foundry adapter／Purpose runtime／route operation 14件 green、Host build green（2026-08-26）。
