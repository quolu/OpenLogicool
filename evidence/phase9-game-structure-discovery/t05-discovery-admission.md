# t05 Discovery Admission 実測記録

取得日: 2026-08-24
対象: Phase 9 G0 / EXP-GS-01 / EXP-GS-04
状態: **GameLab成立。NIKKE lobbyの実画面確認待ち。**

## 結論

- vision runtimeは **Microsoft Foundry Local 0.10.3**、modelは **Qwen3-VL-2B-Instruct CUDA** を選ぶ。
- local VLMは画面内の操作候補を意味ラベルとして列挙する用途だけに使う。VLMが返した座標はdispatchへ渡さない。
- click座標は同一frameの `Windows.Media.Ocr` が返した文字矩形へ、意味ラベルが一意一致した場合だけ固定する。
- 一意一致しない候補、icon-only候補、schema外応答は `Unknown` とし、入力しない。別provider／cloud／生座標へfallbackしない。
- GameLabではpointer移動、frame-bound click、Escape、F13 key receipt、wheel +120/-120が受信側またはbefore/after画像で成立した。
- AI推論目的の外部送信0、外部AI API call 0、外部AI API費用0。OpenAI API keyを含む外部AI credentialは使わない。
- 製品adapterはIP literalのloopback HTTPだけを受理し、proxy／redirectを無効にした単発Responses requestだけを送る。timeout、provider failure、schema外応答は `Unknown` で終了し、自動retry／別provider fallbackをしない。

## 実測環境

| 項目 | 値 |
|---|---|
| OS | Windows 11 25H2 / build 26200 |
| CPU | AMD Ryzen 9 9950X3D |
| RAM | 61.6 GB |
| GPU | NVIDIA GeForce RTX 5090 / 32607 MiB / driver 610.62 |
| local runtime | Microsoft Foundry Local 0.10.3 |
| model cache | `%USERPROFILE%\.foundry\cache\models` |
| capture | Windows Graphics Capture |
| geometry recognizer | `Windows.Media.Ocr` |
| input route | standard integrity `SendInput` |

## ローカルprovider比較

同じ3 frameへ、操作可能なgame controlのlabelと1000基準pointをJSONで返す同一prompt、temperature 0を与えた。3 frameはGameLab main menu、event popup、操作候補のないblank negativeである。

| model | CUDA model取得量 | warm応答 | 意味ラベル | blank棄却 | 生座標 |
|---|---:|---:|---|---|---|
| Qwen3.5-2B | 3.1 GB | 1.635 s | 3/3 | 成立 | 不成立 |
| Qwen3-VL-2B-Instruct | 2.1 GB | 1.941 s | 3/3 | 成立 | 不成立 |

Qwen3-VL-2B-Instructは同じ意味認識結果を約1 GB小さい取得量で満たし、画像理解用modelであるため採用した。この3 frameだけで一般game精度は主張しない。応答はJSON code fenceを含んだため、製品adapterではfence除去後のschema検証を必須とし、不正応答は `Unknown` にする。

### 生座標を不採用にした実測

| frame | OCRで固定した中心の目安 | Qwen3.5 point1000 | Qwen3-VL point1000 | 判定 |
|---|---|---|---|---|
| main menu / OpenEvent | `[402, 916]` | `[340, 840]` | `[245, 645]` | 両方とも実button外 |
| main menu / OpenRewards | `[584, 915]` | `[500, 840]` | `[445, 645]` | 両方とも実button外 |
| event popup / ClosePopup | OCR矩形中心 | `[430, 800]` | `[448, 684]` | 実button外 |

したがって、VLM座標→clickの直結は禁止する。採用経路は `同一frameのVLM意味ラベル → 一意なOCR文字矩形 → frame transform固定 → screen point` だけである。
一致述語と鮮度窓の正本は `docs/contracts/discovery-grounding.md`。GameLab probeの人間指定targetはadmission routeの検証だけに使い、zero-seed構造発見の証拠へ算入しない。

## GameLab入力実測

probe: `discovery-admission-smoke`
result: `probe-output/discovery-admission-smoke-20260824-081417-131.json`

1. seed 5のGameLab main menuをWGC captureした。
2. OCRが `OpenEvent` を `(245, 426, 77, 15)` で一意検出した。
3. frame transformからscreen point `(627, 778)` を求め、`SetCursorPos`後の`GetCursorPos`一致を確認した。
4. left click後に `state.main-menu.event-popup` を再観測した。
5. Escape後に `state.main-menu` へ戻った。
6. F13 key receipt、wheel `+120`、wheel `-120` をGameLabのreceiver-side JSONLで確認した。SendInput側はF13 down/upを一括送信し、GameLab側はWPF KeyDown receiptで受理を判定した。
7. `ExternalAiTransmissionCount=0`、`ExternalAiApiCostUsd=0` をprobe出力へ記録した。

OCRはuser profile language `ja`、`MaxImageDimension=10000`。英字scene labelへ混入するCJK OCR noiseを期待ラベルの文字体系で除き、上下に重なるword矩形からvisual lineを再構成したうえで完全一致した。親scene `state.main-menu`を子scene `state.main-menu.event-popup`へ部分一致させないこともfocused smokeで確認した。

### WPF test fieldのcapture条件

この環境ではGPU renderingのWPF client領域をWGCで取得すると、非client title barだけ取得できてclientが白くなった。GameLabは実gameではなく決定論的test fieldなので、`RenderOptions.ProcessRenderMode = SoftwareOnly` を設定した。設定後は同じWGC経路でclient描画を取得でき、操作後の画面遷移も観測できた。製品のcapture backendをSoftwareOnlyへ変更するものではない。

## 製品adapterとGameLab read-only実測

Foundry Local 0.10.3の実際のvision wire contractを公式C# sampleと実測で確定した。`/v1/chat/completions`へOpenAI標準の`image_url` data URIを送った試行は、画像を約14,668 text tokenとして扱い57.670秒を要したため不採用とした。`/v1/responses`でも`type: message`を欠くrequestは `Request must contain at least one Chat request message`、非streamingは長さ計算errorになった。黙ってCLIへfallbackせず、公式sampleどおり次の一経路だけを実装した。

- endpoint: `http://127.0.0.1:<daemon-port>/v1/responses`
- mode: `stream: true` のSSE
- message: `type: message`／`role: user`
- image: Foundry固有の `type: input_image`／`image_data`／`media_type: image/png`
- model ID: `qwen3-vl-2b-instruct-cuda-gpu:2`
- product boundary: IP literal loopbackだけ、`SocketsHttpHandler.UseProxy=false`、redirect／cookie無効、外部AI keyなし

`live-discovery-observe` をGameLab seed 5へ実走した最終証跡は `probe-output/live-discovery-observe-20260824-115641-763.json`。

| 観測 | 結果 |
|---|---|
| WGC frame | 706×473 BGRA8、PNG SHA-256を証跡化 |
| Windows OCR | `ja`、26ms、`OpenEvent`／`OpenRewards`を各1矩形で取得 |
| local VLM | 701ms、input 421 token／output 24 token |
| VLM labels | `OpenEvent`／`OpenRewards` |
| grounding | 2件とも同一frameのOCR wordへexact／unique一致 |
| network | Probe／Foundry daemonのListen／Establishedを10ms間隔で観測。接続先は `127.0.0.1`だけ、non-loopback 0 |
| input | Observe Only、dispatch 0 |

初回実走ではprompt内のschema説明をmodelが逐語コピーし、`visible text label`を候補として返した。形式適合だけでは画面根拠にならないため、このplaceholderをschema境界で拒否し、probe成功条件を「local VLM完了かつ同一frame OCRへ一意groundingされた候補1件以上」に修正した。修正後の上記実測で成立した。

## Data Flow

- frame、crop、OCR、prompt、responseは利用者端末内だけで処理する。
- Foundry Localのlocal web serviceを使う場合もloopbackだけとし、外部AI providerへ転送しない。
- model binaryの取得通信と、AI inference dataの送信は別data classである。前者は明示install／update、後者は常に0。
- STEP 0の通常Web資料取得はAI推論経路ではなく、source policyとprovenanceを持つReference取得経路である。

## NIKKE policy admission

2026-08-24取得の現行公式EULA §7(b)／§7(c)のautomation禁止と本人アカウントのBAN riskを記録した。セキュリティrisk 0を受入条件とし、この条件を満たせない方式は採用しない。課金／資源消費／戦闘／競争／対人影響もG0 safe sliceでは0とする。オーナーは本人アカウントBAN riskだけを承知して開発継続を裁定したため、Observe／Assist／ExploreをNIKKE lobby safe sliceに限って許可し、Autoは拒否する。技術的成立をpublisher許可の証拠にはしない。記録は `docs/game-policy/nikke-global-jp-2026-08-24.md`。

## 未成立と次の実測

- NIKKE lobbyのcapture継続、visual grounding、pointer移動、非課金・非消費・非戦闘の可逆click、Escape、scrollは未確認。
- NIKKE実測が成立するまでEXP-GS-04全体とt05を完了扱いにしない。
- icon-only affordanceは現時点でUnsupported。別の決定論的local region proposalが実証されるまで入力しない。

NIKKE実測では本体process identity、window mode、locale、window size／DPI、WGC pixel format、HDR、overlay、OCR recognizer language／MaxImageDimension、Qwen variant／CUDA／warm latency、推論中のFoundry listenerがloopbackだけであること、NIKKE＋WGC＋model同時VRAMを記録する。GameLabのSoftwareOnly設定と3-frame結果をNIKKEへ移して成立扱いにしない。

## 一次資料

- Microsoft Learn, Foundry Local SDK reference: https://learn.microsoft.com/en-us/azure/foundry-local/reference/reference-sdk-current
- Microsoft Learn, inference SDK integration: https://learn.microsoft.com/en-us/azure/foundry-local/how-to/how-to-integrate-with-inference-sdks
- Microsoft Foundry Local C# samples: https://github.com/microsoft/Foundry-Local/blob/main/samples/cs/README.md
- Microsoft Foundry Local C# Responses vision sample: https://github.com/microsoft/Foundry-Local/blob/main/samples/cs/foundry-local-web-server-responses-vision/Program.cs
- Qwen official model card, Qwen3-VL-2B-Instruct: https://huggingface.co/Qwen/Qwen3-VL-2B-Instruct
