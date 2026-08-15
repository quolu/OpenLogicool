# Gate manifest v1

各 gate の実験条件と結論を、実証物と切り離さずに記録する JSON 形式。schema は
`fixtures/gate-manifests/gate-manifest.v1.schema.json` を正とする。

| field | 必須 | 意味 |
|---|---:|---|
| `gateId` | yes | gate の stable ID。 |
| `schema` | yes | `gate-manifest-v1`。 |
| `experimentEnvironment.windowsBuild` | yes | 実験時の Windows build。 |
| `experimentEnvironment.firmware` | yes | 対象 hardware の firmware、または hardware 非使用時の理由。 |
| `experimentEnvironment.driver` | yes | 対象 driver、または driver 非使用時の理由。 |
| `experimentEnvironment.lgsState` | yes | LGS の状態、または LGS 非使用時の理由。 |
| `contractRevision` | yes | 実験が照合した contract revision。 |
| `fixtureIds` | yes | 使用した fixture の stable ID または repo-relative path。 |
| `result.outcome` | yes | この実験の `Passed` / `Failed`。 |
| `result.summary` | yes | 結果の短い説明。 |
| `result.evidenceRefs` | yes | 実測・assessment への repo-relative 参照。 |
| `verificationStatus` | yes | `Supported` / `Experimental` / `Unsupported` / `Unverified` の判定。 |
| `unverifiedScope` | yes | この結果では確認していない範囲。空配列は全範囲を実測した場合だけ用いる。 |

## 意味規則

- `Unverified` を `Supported` と表示してはならない。`result.outcome` が `Passed` でも、その実験の範囲外は `unverifiedScope` に残す。
- ある方式の実験が `Failed` なら、別方式へ fallback して成功として記録してはならない。別方式は別 manifest として、独立した環境・evidence・結果を記録する。
- hardware を用いない実験でも環境 field を省略しない。`not applicable` と理由を明記し、何を実測していないかを読めるようにする。
