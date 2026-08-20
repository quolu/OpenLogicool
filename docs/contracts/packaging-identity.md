# Packaging identity 契約

現在の開発配布 layout は unpackaged であり、`OpenLogicool.Host.exe`、`OpenLogicool.Watchdog.exe`、runtime dependencies を同じ application 配下に置く。

- 公開配布の MSIX／Sparse Package／MSI は、EXP-DIST-01 の clean VM 実測まで未決定・未確認である。未実測を Supported と表示しない。
- autostart と update manifest も未確認で、現在の unpackaged layout はどちらも構成しない。
- install と update は device write を開始しない。
- Dynamic Lighting の background control を公開する前には、package identity と AppExtension を含む方式を実測して公開方式を決定する。
