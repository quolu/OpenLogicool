# Foundry Local 0.10.3 vision wire contract

取得日: 2026-08-24
確度: 高（Microsoft公式sampleとWindows実機のdaemon log／応答で一致）

## 一次資料

- Microsoft Foundry Local C# Responses vision sample: https://github.com/microsoft/Foundry-Local/blob/main/samples/cs/foundry-local-web-server-responses-vision/Program.cs
- Microsoft Foundry Local C# samples index: https://github.com/microsoft/Foundry-Local/blob/main/samples/cs/README.md
- Microsoft Foundry Local repository: https://github.com/microsoft/Foundry-Local

## 確定事項

Foundry Local 0.10.3のvision入力は、local web serverの`/v1/responses`へstreaming requestとして送る。公式sampleはOpenAI標準の`image_url`ではなく、Foundry固有の`image_data`と`media_type`を使う。

```json
{
  "model": "qwen3-vl-2b-instruct-cuda-gpu:2",
  "stream": true,
  "input": [
    {
      "type": "message",
      "role": "user",
      "content": [
        { "type": "input_text", "text": "..." },
        {
          "type": "input_image",
          "image_data": "<base64 PNG bytes>",
          "media_type": "image/png"
        }
      ]
    }
  ]
}
```

応答は`text/event-stream`で、`response.output_text.delta`の`delta`を順に連結する。`response.failed`は成功へ変換せず、provider failureとして扱う。

## 実測した誤経路

| 経路 | 実測結果 | 判定 |
|---|---|---|
| `/v1/chat/completions`＋`image_url` data URI | PNG base64を約14,668 text tokenとして処理し57.670秒 | 不採用 |
| `/v1/responses`でmessage `type`なし | `Request must contain at least one Chat request message` | 不採用 |
| 正しいimage schemaのnonstreaming | `input_ids size ... exceeds max length` | 0.10.3では不採用 |
| 公式sample同型のstreaming Responses | Qwen3-VL-2B-Instruct CUDAがGameLab画像を701msで処理 | 採用 |

## OpenLogicoolでの境界

- Foundry endpointはIP literalのloopback HTTPだけを許可する。
- proxy、redirect、cookieを無効にする。
- 外部AI API keyを保持しない。
- timeout、HTTP failure、`response.failed`、schema外応答は `Unknown`。retry、別provider、cloud、OCR-only成功扱いへfallbackしない。
- local VLMの生座標は使わず、同一frameのWindows OCRへexact／unique一致した文字矩形だけをgroundingに使う。
