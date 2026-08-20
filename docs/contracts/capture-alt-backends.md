# Capture 代替 backend の選択契約

CAP-004 のため、Windows Graphics Capture (WGC) window 以外の capture 経路を、失敗時の黙った fallback として使わない。

## Phase 5 の選択

Desktop Duplication と GDI BitBlt による可視 desktop 領域は、probe で一回の frame 取得が成立している。しかし、利用者が指定する display／領域の選択、条件別の利用可否、継続 capture のいずれも製品としては確認済みでない。

このため両経路はこの Phase では製品 backend に採用しない。capture の選択でこれらを要求された場合、製品は非対応である理由を利用者へ表示する。WGC window が失敗、最小化、停止、または非対応条件になっても、Desktop Duplication／GDI BitBlt への自動切替は行わない。

## 利用者への見え方

- Desktop Duplication: 「非対応: probe で frame 取得は確認済みだが、このリリースの製品 backend としては採用していない」
- 可視 desktop 領域（GDI BitBlt）: 「非対応: probe で frame 取得は確認済みだが、このリリースの製品 backend としては採用していない」
- WGC window の失敗: 選択した WGC window の fault として表示し、backend は変えない。

これは probe の成功を製品対応へ読み替えないための境界である。静止画面で frame が届かないことは WGC の変化駆動供給による正常状態であり、代替 backend へ切り替える根拠にはしない。

## 根拠

- `docs/probes/capture-backend-matrix-2026-08-15.md`: GDI BitBlt と Desktop Duplication は display 全体で一回の frame 取得を確認済み。
- `docs/probes/wgc-frame-supply-2026-08-15.md`: WGC の静止 window は正常に frame 供給が停止しうる。Desktop Duplication の静止 desktop 対照実験は未実施。
- `docs/development-plan.md` §6.9 と CAP-004: backend 選択と失敗理由を記録し、切替を利用者へ明示する。
