# Unknown branch append

`UnknownBranchAppend.Append` は既存の Verified `PlaybookVersion` を入力に取り、未知 branch の node と edge を ParentVersionId 付きの新 Version だけへ加える。

- 旧 Version の node／edge を書き換えない。
- branch edge は新しい未知 node を終点とし、空でない branch condition を持つ。
- 生成は既存の `PlaybookCorrection` を通すため、Version と graph の妥当性検証は一箇所に保つ。
