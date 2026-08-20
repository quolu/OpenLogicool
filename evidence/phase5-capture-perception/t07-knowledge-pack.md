# t07-knowledge-pack — Knowledge Pack schema と import 検証

## 実施

- `KnowledgePackDocument` と state の最小schemaを Contracts に追加した。state は stable ID、anchor参照、success condition参照、Semantic Action参照を持つ。
- `KnowledgePackValidator.Import` は manifest と全section contentを同時に検証する。固定section集合、pack内相対path、SHA-256、stateとScreen Graphの一対一ID対応を満たさないpackを拒否する。
- import 結果は常に `Untrusted` とし、pack側が `Verified` を指定したScreen Graph node／edgeも `Candidate` に正規化する。
- manifest fixtureへ入れ子contractの `schemaVersion` を追加し、契約文書をPhase 5実装に更新した。

## 最終試験

```text
dotnet test tests/OpenLogicool.Conformance.Tests/OpenLogicool.Conformance.Tests.csproj --no-restore --nologo --logger "console;verbosity=normal"
```

結果: **18/18 green**（0 failed、0 warning、0 error）。新規試験は次を確認した。

- import後の `Untrusted` 維持とScreen Graphの `Candidate` 降格
- data-only固定集合外の `script` section、およびpack外を指すpathの拒否
- states と Screen Graph node の stable ID集合不一致の拒否
- enum外 trust の拒否
- section content のSHA-256不一致の拒否
- manifest fixtureの閉じたJSON contract形状

`git diff --check` も成功した。

## 変更ファイル

- `src/OpenLogicool.Contracts/Perception/PerceptionContracts.cs`
- `src/OpenLogicool.Perception/KnowledgePackValidator.cs`
- `tests/OpenLogicool.Conformance.Tests/OpenLogicool.Conformance.Tests.csproj`
- `tests/OpenLogicool.Conformance.Tests/KnowledgePackConformanceTests.cs`
- `fixtures/contracts/knowledge-pack-manifest.sample.json`
- `docs/contracts/knowledge-pack-manifest.md`
- 本証跡

## 範囲外

Packのzip／directory配布形式、署名、各sectionの詳細schema、candidateのlive検証によるverified昇格は、このToDoの範囲外である。
