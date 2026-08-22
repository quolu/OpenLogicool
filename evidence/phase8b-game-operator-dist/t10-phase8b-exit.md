# t10 Phase 8B Exit 証跡

## 実施

- full regression 1回: `dotnet test OpenLogicool.sln --nologo --maxcpucount:1`
- 通しで `InputStudioIsolationTests` が `using Xunit;` 欠落により Host.Tests を compile できなかった。t10 で直した
- [docs/phase8b-exit-assessment.md](../../docs/phase8b-exit-assessment.md) を Exit 5条件×根拠4値で書いた
- Public Gate／Shared Distribution Gate の未確認行を成立扱いにしない
- 公開 claim は Game Operator Preview
- H の実 game Verified live は未確認のまま残す

## focused verification（通し前の Host.Tests 修正）

| command | result |
| --- | --- |
| `dotnet test tests/OpenLogicool.Host.Tests --filter FullyQualifiedName~InputStudioIsolationTests` | 5/5 green |

## full regression

`dotnet test OpenLogicool.sln --nologo --maxcpucount:1` — 失敗 0、合格 697。
