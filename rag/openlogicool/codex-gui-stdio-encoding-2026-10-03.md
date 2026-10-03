# Codex App ServerのGUI起動とUTF-8 stdio

出典: [Microsoft ProcessStartInfo.StandardOutputEncoding](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.standardoutputencoding?view=net-10.0)、[OpenAI App Server](https://learn.chatgpt.com/docs/app-server)、Windows native実測。
取得日: 2026-10-03。
確度: 確認済み（コンソールなしの修理前後測定、導入後の製品GUI実行）。

`ProcessStartInfo.StandardOutputEncoding`の既定はnull。明示しない子process出力の復号を、JSONLのUTF-8契約の代わりに環境へ任せない。プロパティはprocess開始前に指定し、実際の子processの出力で確認する。

OpenLogicoolのPowerShell 7／公式codex.ps1経路では、コンソールを持つ診断実行は成功した一方、GUI実行ではUTF-8の日本語をCP932として読み、JSONの引用符まで破損した。コンソールなしのnative親から製品の同じ起動設定を呼ぶ小さな試験でも再現した。

標準入力・出力・エラーのencodingをBOMなしのUTF-8へ明示した修理後は、同じ日本語JSONが完全一致した。外部wrapperや利用者のWindows localeは変更していない。不正JSONは原文を記録し、補正・再送せず停止する。

実測と変更箇所は[障害記録](../../docs/incidents/2026-10-03-nano-click-verification.md)、小さな測定結果は`probe-output/codex-gui-encoding-{before,after}-20261003.json`。受信した実thread履歴は公開repoへ含めない。

一次資料の短い原文は[raw資料](raw/codex-stdio-encoding-2026-10-03.md)へ分離した。
