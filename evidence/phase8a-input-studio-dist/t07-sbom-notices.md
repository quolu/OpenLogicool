# t07 SBOM / notices 証跡

## 実施

- t06 の `PackageIdentity` を保持したまま、SBOM component、artifact SHA-256、同梱 `sbom.json`／`THIRD-PARTY-NOTICES.md` を組み立てる pure builder を追加した。
- component に name、version、license、notice source を必須化し、重複を拒否した。
- `SignatureCreated` は常に false とし、signature・timestamp・Authenticode は作成しない。
- Third-Party Notices は lock file の同一 version の package license text を公開 artifact 作成時に展開する対象を明示した。

## focused verification

| command | result |
| --- | --- |
| `dotnet test tests/OpenLogicool.Packaging.Tests/OpenLogicool.Packaging.Tests.csproj --nologo --filter 'FullyQualifiedName~SbomNoticesTests' --logger 'console;verbosity=minimal'` | 3/3 green |
