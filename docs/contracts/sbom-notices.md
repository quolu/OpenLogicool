# SBOM / Third-Party Notices 契約

`SbomNotices` は t06 の `PackageIdentity` と artifact bytes から、component 行、SHA-256 artifact hash、同梱名 `sbom.json`／`THIRD-PARTY-NOTICES.md` を組み立てる pure builder である。

- component は name、version、license、notice source を必須とする。
- artifact file name と component name/version の重複は拒否する。
- artifact hash は SHA-256 であり、署名・timestamp・Authenticode の成功を意味しない。`SignatureCreated` は常に false。
- package 方式、autostart、update manifest が未確認なら、その根拠状態を `PackageIdentity` のまま bundle に保持する。

公開 artifact を作る時は、lock file と同じ version の package に同梱された license text を `THIRD-PARTY-NOTICES.md` へ展開する。現在の開発 bundle は package identity の公開採択前であり、署名済みと表示しない。
