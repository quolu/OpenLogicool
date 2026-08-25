# NIKKE Global / Japan Game Policy Record

> **Superseded historical record**: 本書は2026-08-24のG0／safe-slice実験だけのpolicy evidenceである。一手承認、可逆復帰、戦闘・開始等の固定禁止を現在のコアruntimeへ適用しない。現行の操作受付は`docs/development-plan.md` §0.3／§6.13.4を正とし、利用者がRunへ明示した禁止tagだけが有効である。

取得・確認日: 2026-08-24
確認者: OpenLogicool開発lane
schema: `0.2.0`
game ID: `nikke:global:jp`
publisher: Proxima Beta Pte. Limited / SHIFT UP CORP.
review status: **Confirmed**
allowed modes: **Observe / Assist / Explore**
owner risk acceptance: **2026-08-24、本人アカウントのBAN riskだけを承知して開発継続**

## 判定

| mode | 判定 | 根拠 |
|---|---|---|
| Observe | 許可 | owner risk acceptanceの下、G0の画面観測に限定する |
| Assist | 許可 | ownerが確認した一手だけを送る。無償資源の受取は許可し、課金・資源消費・戦闘・競争操作は禁止 |
| Explore | 許可 | NIKKE lobby safe sliceに限定。無償資源は全件受取対象、課金・資源消費・戦闘は禁止。一手承認・before／after観測・可逆復帰を必須にする |
| Auto | 拒否 | G0では自律連続操作を許可しない。Phase 9の別gateと実測が成立するまで拒否を維持する |

現行EULA §7(b)／§7(c)の禁止文言と本人アカウントのBAN riskは消えない。ownerはこのriskだけを承知して開発継続を裁定した。技術的成立をpublisher許可の証拠にはしない。G0で許可する入力は、ownerが画面を見て承認した可逆な一手だけである。

## risk境界

- セキュリティrisk: **0を受入条件とする**。DLL injection、memory read/write、anti-cheat回避、通信傍受、認証回避を行わない。この条件を満たせない方式は採用しない。
- 金銭／資源／対人影響: 課金、game resource消費、戦闘、競争、他playerとのinteractionを行わない。**無償で受け取れる資源はオーナー裁定により全件取得対象**とし、取得を消費と混同しない。
- 受容するrisk: **owner本人のNIKKEアカウントBANだけ**。このriskを理由にG0開発を停止しない。

## 一次資料

- Terms入口: https://nikke-en.com/termsofservice/gl
- 取得本文: https://nikke-en.com/termsofservice/children/en.html
- 取得時HTTP status: 200
- 取得時body bytes: 429846
- 取得時body SHA-256: `a40d25d9ff5dfd65e28e7803f80f9183a92c415000b5a896514436334ec66b50`
- 関連条項: Player Conduct §7(b)、§7(c)

## 再確認

- 次回確認: Phase 9で別real targetを選ぶ時、またはNIKKE terms変更検出時
- 変更検出時: review statusを`Changed`へ落として利用者へ表示する。現在の操作可否はRunへ明示した`AllowedModes`だけで決める
