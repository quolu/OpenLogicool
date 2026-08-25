# Discovery Grounding contract

意味候補と保存actionを、current frame上の入力座標へ固定する現行契約。Phase 9 G0の厳密一致gateは失効した。

## 接地述語

- local VLMは意味ラベル候補だけを返す。VLMが生成した座標はdispatchへ渡さない。
- affordance labelはOCR wordとvisual lineをUnicode正規化し、軽い編集距離と位置許容差で比較する。OCR engineのline分割、文字体系混入、句読点差、大小差を完全一致条件にしない。
- 正規化後の期待ラベルが空なら `Unknown`。空文字列を一致として扱わない。
- 保存anchor／actionと文字・位置が軽く類似する候補を同じIDへ再同定する。後のOCRがより自然なら表示文字列を更新し、旧文字列とevidenceを残す。単一frameのOCR差だけで保存候補を誤り扱いしない。
- 同一物理OCR矩形を複数spanが指す場合は一候補へまとめる。複数の保存候補が残る時は、使える保存actionが無い状態として目的に必要な一件だけAI discoveryへ渡す。
- VLMの重複labelは一件へ畳み、補正flagを記録する。通常の途中切れ、別schema、散文はfailureとして返す。icon-only／画像controlは同一frameの局所visual patchへ束縛して保存し、生VLM座標へfallbackしない。
- 接地はOCR文字の位置を証明するだけで、操作可能なcontrolであることを証明しない。入力後10秒の`Compare`がaffordance性とoutcomeを受け持つ。

## frame束縛

- proposalはframe ID、crop、transform revision、capture backend、recognizer version、model ID／variantへ束縛する。
- dispatch時のwindow identityとtransform revisionが変わった時は再観測する。固定経過時間だけで静止画面の保存actionを拒否しない。
- window rect／client rect、DPI、WGC content sizeの変換は対象environmentで実測し、API成功だけを座標成立の証拠にしない。

## ローカルAI境界のmachine test

- AI adapterが受理できるnetwork endpointはloopback (`127.0.0.0/8`、`::1`) だけ。in-process SDKにはendpoint設定を持たせない。
- 外部AI API credentialの設定、保存、読出し経路が存在しないことをarchitecture testで固定する。
- 推論中のlistener／connectionを観測し、frame、crop、OCR、embedding、prompt、responseがloopback外へ送られないことをadmission probeで固定する。
- probe内の自己申告counterだけで外部送信0を成立扱いにしない。
- local model binary取得とSTEP 0 Web Reference取得は推論data planeから分離し、取得中と推論中を別観測する。
