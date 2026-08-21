# Authenticode 契約

公開 artifact、installer、update manifest の Authenticode 署名と timestamp は、コード署名証明書があるときだけ行う。

- 証明書が無い間の根拠は **未確認** である。未確認を Supported と表示しない。
- 自己署名、テスト証明書、無署名を署名済みとして扱わない。
- `SbomNoticeBundle.SignatureCreated` は署名を作らない経路では常に false である。
- timestamp も証明書と同じ門であり、証明書が無い間は未確認のまま残す。
