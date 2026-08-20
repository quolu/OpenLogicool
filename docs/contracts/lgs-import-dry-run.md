# LGS XML import dry-run 契約

`LgsXmlDryRun.Analyze` は LGS 9.04.49 profile XML を読み、変換候補と未対応行を別々に返す pure API である。

- `original="true"` の割当は LGS 既定のため未対応へ分類し、取り込まない。
- 単一 `keystroke` の利用者変更だけを変換候補として表示する。
- `script` を含む macro と `target@path` は未対応へ分類する。値を実行・パス解決せず、結果にも命令として渡さない。
- XML の DTD は拒否する。
- この API は profile 保存、device API、LGS 操作を持たず、dry-run だけを行う。
