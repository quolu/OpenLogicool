# Diagnostic bundle 契約

`DiagnosticBundle` は既存の `diagnostics` CLI と別に、利用者が生成前に確認できる既定 bundle を扱う。

- `Preview` は filesystem へ書かず、生成予定の固定 manifest を返す。
- `Create` は preview の manifest 1件だけをローカルに生成する。
- `Delete` はその preview が示す bundle 1件だけを削除する。
- 既定 bundle は screen、OCR、prompt、journal 本文、crash dump、secret、個人データを探索・収集・保存しない。
- device ID、DB、profile、app identity は既存 diagnostics の表示対象だが、既定 bundle には含めない。
