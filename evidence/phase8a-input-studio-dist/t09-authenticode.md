# t09 Authenticode（H）証跡

## 実施

- 席は取らない。親が H を記録する。
- 2026-08-21 に `Cert:\CurrentUser\My` と `Cert:\LocalMachine\My` の CodeSigningCert を読んだ。該当証明書は 0 件。
- 署名・timestamp・自己署名は作らない。自己署名を Supported と表示しない。
- 公開 claim の根拠は **未確認** のまま残す。
- 既存 `SbomNotices` は `SignatureCreated=false` を構造で固定しており、署名をここで追加しない。

## 根拠水準

- **未確認**: Authenticode 署名、timestamp、公開 artifact の署名検証。
- **非対応にしない**: 証明書が来たら同じ契約で署名する。今回は未確認として閉じる。

## focused verification

| command | result |
| --- | --- |
| `Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert` | 0 件 |
| `Get-ChildItem Cert:\LocalMachine\My -CodeSigningCert` | 0 件 |
| `dotnet test tests/OpenLogicool.Packaging.Tests/OpenLogicool.Packaging.Tests.csproj --nologo --filter FullyQualifiedName~SbomNoticesTests` | SignatureCreated=false を含む既存 focused green |
