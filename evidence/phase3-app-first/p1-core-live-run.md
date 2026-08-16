# p1-core 実機実測証跡（2026-08-16・オーナー実機手番）

構成: 実機 G13/G600 同時配線・temp DB・profile 3件（live-default-G13: G1→F14／live-default-G600: G9→F15／live-app-G600: G9→F16）・関連付け4件（両種別の既定＋explorer.exe path＋Store メモ帳 package）。`run --trace --duration-ms 900000` の console log が原本（probe-output/fastpath-live-check-20260816.log に封入）。

## 成立項目（すべて log 実測）

1. **trace（test field・t06）**: G600 G9→F15、G13 G1→F14 とも down/up 完全対で emitted。無割当 control（G600 の G1/G2 クリック）は「割当なし no-op」として観測——観測系が割当の有無を偽らない
2. **path 一致切替（t02/t03）**: explorer.exe 前面で `profile switch: G600 -> 'live-app-G600'（path 一致: c:\windows\explorer.exe）`→ 以後の G9 が F16 へ変化
3. **往復切替（t03・Alt+Tab 相当）**: 一致 app→関連付けなし app→一致 app の往復で、`既定`／`path 一致`／`package 一致` の理由付き switch log が毎回出て、抜け・二重なし
4. **package 一致切替（t02）**: Store 版メモ帳前面で `package 一致: Microsoft.WindowsNotepad_8wekyb3d8bbwe`——手打ち path では一致不能な Store app redirect 罠の根治を実機確認
5. **foreground state 遷移 log（t04）**: 「一致 app」⇔「既定 app（identity 識別済み・関連付けなし）」の遷移時のみ log・継続中は沈黙
6. **誤 Unknown なし（t04）**: 昇格 process（タスクマネージャ）前面でも identity は取得され「既定 app」と正しく判定——Unknown を誤発火しない

## 未実測（正直な残余）

- **Unknown Application への実機遷移**: identity 取得不能（保護 process 等）の実トリガーは未再現。ロジックは focused test で固定済み。NIKKE（anti-cheat 保護の可能性）実測時に観測できたら追記
- **NIKKE launcher→本体遷移**: 次回ゲーム起動時のついでに確認（Exit 条件3 の最終材料）
