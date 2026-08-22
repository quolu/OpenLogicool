# NIKKE は SendInput 合成キー入力を受理しない（実測・確認済み）

- 取得日: 2026-08-22
- 確度: 確認済み（実機実測）
- 出典: dogfooding 中の実測（本書が一次記録）

## 実測

環境: `ui --resident`（fast path＋watchdog 同居）・NIKKE 本体（`c:\nikke\nikke\game\nikke.exe`）前面・
NIKKE 用 workspace（`ws-nikke-G600`: G6→Key:Esc・G11→Key:A ほか）適用済み（起動ログで path 一致適用を確認）。

- G600 G6 押下 → 動作チェック trace「G600 の G6 を押した → Key:Esc を送りました」（送出成立）
- G600 G11 押下 → 「G600 の G11 を押した → Key:A を送りました」（送出成立）
- **ゲーム内反映: なし**（オーナー実測。Esc メニューも A も無反応）

同じ送出はメモ帳等の通常アプリでは受理される（EXP-IN-01 standard=Delivered、
`probe-output/sendinput-accept-standard-20260816-014738-596.json`）。

## 判定

- fast path（検出→profile 解決→送出）は正常。**NIKKE 側が注入入力を弾いている**（anti-cheat による
  合成入力フィルタと推定。LLKHP の injected flag 等の判別が一般的な手口）。
- 計画の製品境界「Input API 成功をゲーム内成功と扱わない」の実例。SendInput 経路（方式B変種）は
  anti-cheat 付きゲームには届かない場合がある——Supported 表示の行条件として扱う。

## 対応の方向

route 決定（[docs/g600-route-assessment-2026-08-15.md](../../docs/g600-route-assessment-2026-08-15.md) §5:
B変種主経路・A補完）の **方式A（G600 onboard 直書き）が該当ケース**。onboard に書いた割当は
device がハードウェアとして送るため anti-cheat から物理キーボードと区別できない
（LGS 時代の割当が NIKKE で効いていた実績と同根）。採否はオーナー裁定待ち（2026-08-22 提案済み）。
注意点: onboard 送出中は常駐の同キー SendInput との二重入力を排他すること・G13 は対象外。
