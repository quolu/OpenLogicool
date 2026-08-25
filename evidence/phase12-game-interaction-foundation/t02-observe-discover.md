# t02 ObserveとDiscoverTargetsの製品接続

## 結論

ObserveとDiscoverTargetsをProbeから独立した製品runtimeへ接続した。文字付きcontrolだけでなくicon-only controlも、game固有label、state名、target座標、正解routeなしで同じWGC frameのnormalized boundsへ束縛できる。

## 実装

- `ProductGameObservationRuntime`
  - `ObserveAsync`がcapture frameと`ObservationResult`を固定する。
  - `DiscoverTargetsAsync`は直前のObservationだけを受理する。
  - source、backend、sequence、transform、freshness、windowが不一致なら明示停止する。
- `WindowsWgcGameFrameSource`
  - WGC専用adapter。
  - cursorをcaptureへ含めない。
  - frame取得不能はtimeout理由を返し、別capture routeへfallbackしない。
- `FoundryLocalVisionClient.ProposeControlsAsync`
  - 文字付きとicon-only controlを厳密な`controls` JSONで取得する。
  - normalized bounds外、追加property、異なるschemaを拒否する。
  - provider failureを既存label経路へfallbackしない。
- `FoundryLocalControlDiscoveryProvider`
  - control boundsを`AffordanceCandidate`とclick proposalへ変換する。
  - VLM自己confidenceは採用せず、candidate confidenceを未校正の0.5に固定する。
- `WindowsGameOcrRecognizer`／`WindowsGameFramePngEncoder`／`FoundryControlTargetDiscoveryAdapter`
  - OS依存OCRとPNG encodeを独立ファイルに隔離する。
  - OCR領域はVLM control内にある場合だけ追加evidenceにし、icon-only候補を捨てない。

## focused検証

- `ProductGameObservationRuntimeTests`: 3件green
  - 同一frame束縛
  - 古いObservation拒否
  - 別window candidate拒否
- `FoundryLocalVisionClientTests`＋`FoundryLocalControlDiscoveryProviderTests`: 16件green
  - text／icon control
  - strict bounds／schema
  - provider failure時fallback 0
- `OpenLogicool.AI.Tests`全33件green、failure 0、skip 0。
- 変更対象の`git diff --check`通過。

## 未検証

WGC、Windows OCR、Foundry Localを実NIKKEで一巡するWindows native live証拠は後続`t07-basic-live`で取得する。このToDoでは実装と変更直結focused testだけを成立とする。
