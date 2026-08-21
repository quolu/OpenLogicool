# Install lifecycle 契約

install、update、rollback、repair、uninstall の各操作は device write を開始しない。

- rollback と uninstall では、LGS 復帰が必要な場合に既存 `leftover restore` 経路を要求する。
- Packaging は device API や restore 実装を持たず、既存の G600 leftover restore を再実装しない。
- clean 環境での install/update/rollback/repair/uninstall 実測は後続の配布受入で確認する。ここではその事前条件を focused test として固定する。
