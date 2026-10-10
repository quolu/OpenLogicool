# 同梱書体

画面の英数字の見出しとキーキャップの文字に使う書体。どちらも SIL Open Font License 1.1 で、著作権表示とライセンス文は各ファイルの name テーブルにも入っている。

| ファイル | 書体 | 版 | 著作権表示 | 出どころ |
| --- | --- | --- | --- | --- |
| Oxanium-SemiBold.ttf／Oxanium-Bold.ttf | Oxanium | 2.000 | Copyright 2019 The Oxanium Project Authors | https://github.com/sevmeyer/oxanium （fonts/ttf） |
| JetBrainsMono-Bold.ttf | JetBrains Mono | 2.305 | Copyright 2020 The JetBrains Mono Project Authors | https://github.com/JetBrains/JetBrainsMono （fonts/ttf） |

- ファイルは配布元のものをそのまま置く（改変しない）。
- 日本語は Windows に入っている BIZ UDPゴシックを使い、日本語書体は同梱しない。
- 書体の指定は `Theme.cs` の `Display`／`Mono` だけが持つ。
