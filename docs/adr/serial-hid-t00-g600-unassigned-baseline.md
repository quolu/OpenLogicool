# G600 onboard の未割当マウスボタンは baseline を保持する

日付: 2026-08-23  
状態: 採用

## Decision

G600 onboard payload では、未割当 G2〜G5 の通常層・G-Shift 層を baseline のまま保持する。明示割当がある層だけ cell を書く。未割当 G6〜G20 は両層とも `00 00 00` にして、旧割当と本体面切替を残さない。

apply と fresh verify が成立しても実ボタンへ反映されない場合があるため、成功結果は USB 挿し直し案内を含める。device write は既存 `G600EvidenceWrite` だけを使い、別経路を追加しない。

## Basis

旧実装は未割当 G2〜G5 にも明示的な無動作 cell を生成し、baseline の右クリック・中クリック等を上書きしていた。旧 commit `5042a8e` の focused test で再現し、修理後は Host focused test 85件と独立差分監査で受入を確認した。詳細は `evidence/serial-hid-output/t00-close-g600-dogfood-fix.md` に保持する。
