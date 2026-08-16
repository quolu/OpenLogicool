# t02-app004-identity 完了証跡

- 実装 commit: 9b61c56（origin/main へ push 済み）・実装は implementer（sonnet×medium）委譲、統括受入
- 照合順序: package family name 一致 → 正規化 path 一致 → 既定（AppProfileResolver.Resolve・pure）
- ForegroundApplicationIdentity（path/package/pid/process 開始時刻・取得不能は null 保持）、migration 005（matcher_kind 列）、CLI `associate package:<familyName>`・`apps` の [pkg:] 表示
- 実装中に GetPackageFamilyName の CharSet.Unicode 欠落（package 名が先頭1文字に切り詰め）を実機実走で検出・修正済み
- 検証: build 0 error・Host 10/10・Persistence 16/16・Profiles 18/18・Architecture 4/4 green（worker 実行＋統括再実行の両方）・実機 apps 実走で Store app 4件が [pkg:] 付きで列挙（notepad redirect 罠の根治確認）
