# STEP 0 Web Reference Policy調査

- 取得日: 2026-08-24
- 対象: AI Game Structure Discovery前段のWeb調査
- 確度: 確認済み

## 結論

Web調査は外部情報を操作権限へ変換せず、出典付き仮説として保存する。保存形式はMarkdownに統一するが、sourceの利用条件により本文保存範囲を変える。

- FullTextAllowed: 明示license、公式API、利用者所有資料等
- SummaryOnly: 短い根拠、構造化要約、URL、取得時刻だけ
- LinkOnly: URLと取得判定だけ
- Blocked: robots、規約、認証、network、parse等で停止

GameWithはSummaryOnlyを既定とする。全文HTML、画像、全文Markdownを永続化せず、Markdown参照カードとしてtitle、URL、取得時刻、短い根拠、mechanic／rule／daily／reset候補、矛盾、game内検証状態を保持する。

## 根拠

### GameWith利用規約

- URL: [GameWith利用規約](https://gamewith.jp/terms)
- 2026-08-24取得
- 第9条は知的財産権侵害、通常利用を超える負荷、機能の複製・修正・転載・翻訳・解析、商業目的利用を禁止している。
- 第10条は本サービスの知的財産権がGameWithまたは許諾者に帰属し、通常利用が知的財産の使用許諾を意味しないとする。

したがって、公開製品がGameWith本文を自動で全文Markdown化して永続ミラーする仕様は採用しない。

### GameWith robots.txt

- URL: [GameWith robots.txt](https://gamewith.jp/robots.txt)
- 2026-08-24取得
- Sitemap指定だけでDisallowは観測されなかった。

robotsの許可は著作権または利用規約上の保存・再利用許諾ではない。source policyはrobotsと利用条件を別々に判定する。

### Microsoft MarkItDown

- URL: [Microsoft MarkItDown](https://github.com/microsoft/markitdown)
- 2026-08-24取得
- 各種fileをLLM向けMarkdownへ変換する軽量utilityである。

変換可能性と保存許諾は別問題である。OpenLogicoolの製品runtimeは出力形式としてMarkdownを採用するが、第三者本文の保存可否をMarkItDownの能力から推論しない。

## 製品契約への反映

1. Web本文、検索snippet、Markdown、OCRは非信頼入力とする。
2. Web Reference Factはcandidateであり、画面観測なしにverifiedへ昇格しない。
3. zero-seed acceptanceはWeb Reference 0件で実行する。
4. 通常journeyはSTEP 0を先に実行し、探索候補の優先順位づけだけに使う。
5. 取得不能と規約拒否を空の成功へ丸めず、LinkOnly／Blockedとして表示する。
