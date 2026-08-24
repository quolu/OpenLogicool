# Discovery Grounding contract

Phase 9 G0の意味候補を、同一frame上の入力座標へ固定する契約。

## 接地述語

- local VLMは意味ラベル候補だけを返す。VLMが生成した座標はdispatchへ渡さない。
- affordance labelはOCR wordごと、scene labelは矩形が上下に重なるOCR wordをX順に連結したvisual lineごとに比較する。OCR engine自身のline分割だけを根拠にしない。Unicodeのletter／digitと `.` `-` `_` だけを残し、大小を正規化する。観測側は期待ラベルが使用する文字体系（ASCII／non-ASCII）だけを残し、別文字体系のOCR混入noiseを一致へ混ぜない。期待ラベルが両方を含む場合は両方を残す。親scene名を子scene名へ部分一致させない。
- 正規化後の期待ラベルが空なら `Unknown`。空文字列を一致として扱わない。
- 完全一致がframe内で1件ならgroundedとする。完全一致が一意でない時は正規化Levenshtein類似度0.85以上かつ次点の物理候補との差0.15以上だけをfuzzy uniqueとして許可する。既知anchor追跡は正規化長8以上、類似度0.70以上、位置誤差0.08以下かつ次点位置誤差0.12以上だけを許可する。それ以外は `Unknown`。
- 同一物理OCR矩形を複数spanが指す場合は一候補へまとめる。異なる物理矩形が同程度なら必ず `Unknown`にし、個別誤字辞書やgame固有置換表で選ばない。
- VLMの完全一致重複labelは順序を保って一件へ畳み、補正flagを記録する。未閉鎖JSONからの回収は、正しい`labels`配列文法、完全string、同一labelの3回以上の反復、末尾途中切れを全て確認できる時だけ行い、補正flagを記録する。通常の途中切れ、別schema、散文は `Unknown`。icon-onlyはG0では `Unknown`。別matcherや生座標へfallbackしない。
- 接地はOCR文字の位置を証明するだけで、操作可能なcontrolであることを証明しない。一手承認とbefore／after再観測がaffordance性とoutcomeを受け持つ。

## frame束縛

- proposalはframe ID、crop、transform revision、capture backend、recognizer version、model ID／variantへ束縛する。
- OCR完了からdispatchまでの許容ageは250ms。超過、transform revision変更、window identity変更のいずれかで再観測する。
- window rect／client rect、DPI、WGC content sizeの変換は対象environmentで実測し、API成功だけを座標成立の証拠にしない。

## ローカルAI境界のmachine test

- AI adapterが受理できるnetwork endpointはloopback (`127.0.0.0/8`、`::1`) だけ。in-process SDKにはendpoint設定を持たせない。
- 外部AI API credentialの設定、保存、読出し経路が存在しないことをarchitecture testで固定する。
- 推論中のlistener／connectionを観測し、frame、crop、OCR、embedding、prompt、responseがloopback外へ送られないことをadmission probeで固定する。
- probe内の自己申告counterだけで外部送信0を成立扱いにしない。
- local model binary取得とSTEP 0 Web Reference取得は推論data planeから分離し、取得中と推論中を別観測する。
