# t03 Nano基本入力の共通port

## 結論

Hover、Click、KeyTap、Scroll、Dragを、現在Observationへの束縛とNano Serial HID一回dispatchを共通化した製品portへ接続した。SendInput、Computer Use、別route、blind retryは実装していない。

## 実装

- `NanoGameInteractionActions`
  - Observation ID、frame sequence、transform revision、target windowを照合する。
  - target bounds、candidate、locatorを検証してからだけdeviceへ渡す。
  - semantic operationのdispatchは一回だけ。
  - device faultは`DispatchFailed` receiptとして明示し、再試行しない。
- `SerialHidNanoGameInputDevice`
  - Hover: pre-observation座標へrelative pointerを収束。
  - Click: pre-observation座標へ移動し、left down／up。
  - KeyTap: 有限key列をdownし、逆順up。
  - Scroll: targetへ移動しvertical wheelを一回送信。horizontalはNano 1.1.0非対応を明示。
  - Drag: startへ移動、left down、destinationへ移動、left up。移動fault時もfinallyでupを送る。
- `WindowsGameInteractionCoordinateMapper`
  - normalized WGC座標からWindows screen座標への変換をOS固有ファイルへ隔離。

## focused検証

- `NanoGameInteractionActionsTests`: 3件green
  - 5操作が各1回だけdispatch
  - stale targetでdevice call 0
  - device faultで再試行0、`DispatchFailed`
- Host projectはWindows OCR／WGC／Nano adapterを含めてbuild成功。
- 既存`SerialHidEmitterTests`／`SerialHidRelativePointerTests`: 19件green。
- 変更対象の`git diff --check`通過。

## 未検証

実Nanoでの各物理入力とgame effectは`t07-basic-live`で個別に証拠化する。horizontal scrollは現firmwareで非対応であり、vertical Scrollの成立と混同しない。
