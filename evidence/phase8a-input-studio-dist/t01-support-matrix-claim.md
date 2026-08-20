# t01 Support matrix claim 証跡

## 実施

- 公開 claim を `Partial LGS Replacement` に固定し、LGS Parity を使用しない。
- Desktop の pure matrix に確認済みの G600 B変種 route、A方式の補完 route、F3/F4/F5 の3 slot 制約、F6 read 不可を記録した。
- LGS inventory の script／LCD applet／power mode と reference machine 外は `Unverified` のままにした。
- release note に確認済み・制約・未確認を分けて記載した。

## 根拠水準

- **確認済み**: Windows 11 build 26200 / x64 の reference machine、G600 B変種 remap、A方式 slot 切替。
- **非対応**: F6 profile read。
- **未確認**: 全 LGS parity、LGS script／LCD applet／power mode、Windows 10／ARM64／別 GPU 構成。

## focused verification

| command | result |
| --- | --- |
| `dotnet test tests/OpenLogicool.Desktop.Tests/OpenLogicool.Desktop.Tests.csproj --nologo --filter 'FullyQualifiedName~InputStudioSupportMatrixTests' --logger 'console;verbosity=minimal'` | 4/4 green |
