# Game Operator capability release 契約

`CapabilityRelease` は公開 release の各 capability を、既存の規約 gate と環境根拠へ束縛する。

- Observe Only は release 設定と `GamePolicyGate` の Observe 許可が必要。
- Teach と Supervised はそれぞれの release 設定と Assist 許可が必要。
- Verified は release 設定、Auto 許可、`VerifiedEnvScope` と対象環境の完全一致がすべて必要。
- release 設定、規約、Verified 環境根拠のどれかが欠ける場合は公開しない。既存の ObserveOnly、TeachSupervised、GamePolicyGate、VerifiedEnvScope の実装は再実装しない。
