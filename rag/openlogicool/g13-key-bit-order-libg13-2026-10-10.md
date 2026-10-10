# G13 input report の bit 順（公開実装 libg13）

- 取得日: 2026-10-10
- 出典: ecraven/g13（libg13）の `g13_keys.cc` にある `G13_KEY_SEQ` の定義。https://github.com/ecraven/g13
- 確度: 高（公開されている一次コード。実機での個別確認とは別）

## 内容

`G13_KEY_SEQ` は、G13 の USB メッセージの bit とボタンの対応を、bit 順に並べた一覧。定義の注記は、各項目の位置が特定の bit に対応するので、項目を足したり抜いたりしないよう求めている。

| byte | bit0 → bit7 |
| --- | --- |
| 3 | G1 G2 G3 G4 G5 G6 G7 G8 |
| 4 | G9 G10 G11 G12 G13 G14 G15 G16 |
| 5 | G17 G18 G19 G20 G21 G22 UNDEF1 LIGHT_STATE |
| 6 | BD L1 L2 L3 L4 M1 M2 M3 |
| 7 | MR LEFT DOWN TOP UNDEF3 LIGHT LIGHT2 MISC_TOGGLE |

## OpenLogicool の実測台帳との対応

- byte3〜5 の G1〜G22、byte6 の M1〜M3、byte7 bit0 の MR は、[実測台帳](../../docs/probes/g13-input-map-2026-08-15.md)の対応と一致する。
- byte6 bit0 の BD は、LCD の列の左にある丸いボタン。OpenLogicool の `LCD_AUX`。
- byte6 bit1〜4 の L1〜L4 は、LCD の下に4つ並ぶボタンで、左から順。OpenLogicool の `LCD1`〜`LCD4`。
- byte7 bit3 の TOP は、スティックの押込み。OpenLogicool の `STICK_PRESS`（実機で確認済み）。
- byte7 bit1 の LEFT と bit2 の DOWN は、スティックの脇の2つのボタン。OpenLogicool の台帳では未確認で、control には入れていない。
- byte5 bit7 の LIGHT_STATE は、台帳の「idle で 1」の bit に当たる。

## 使い方

- 図へ LCD の列のボタンを置く時の並びの根拠にした（`InputStudioFigures.G13ControlName`）。
- スティックの脇の2つのボタンを control に足す時は、この並びを仮説にして、実機で単独押下を記録して確かめる。
