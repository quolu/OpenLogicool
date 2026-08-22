# t01 protocol v1契約の証跡

取得日: 2026-08-23

## 固定した契約

- 2-byte magic、1-byte version／kind、16-bit little-endian sequence／length、最大32-byte payload、16-bit little-endian CRC。
- CRC-16/CCITT-FALSE（`123456789`のcheckは`0x29B1`）。
- message 7種、kind別固定payload、fault code 9種。
- SET_STATEはmodifier＋通常key最大6＋mouse buttonの完全snapshot。
- 同方向edge群ごとのcheckpoint、duplicate ownership参照数、matching ACK後だけcommit。
- wrong up、unsupported usage、6KRO超過は送信前fault。自動再送・SendInput fallbackなし。
- development-planにDEV-014を追加し、NFR-002を選択Emitterの成立確認へ一般化。

byte-level本文は`firmware/OpenLogicool.SerialHid/protocol-v1.md`、共有fixtureは同directoryの`protocol-v1-golden-vectors.json`、不変Decisionは`docs/adr/serial-hid-protocol-v1.md`。

## 実測

`dotnet test tests/OpenLogicool.Input.Tests/OpenLogicool.Input.Tests.csproj --logger "console;verbosity=minimal"`

- 失敗: 0
- 合格: 129
- スキップ: 0
- C# codecが共有golden vector 7件をbyte一致でencode／decode。
- checksum、version、length、unknown kind、payload canonical form、sequence wrap、sequence 0予約をpure testで確認。
- chord＋mouseの単一snapshot、finite sequence 4 checkpoint、duplicate ownership、matching ACK、stale ACK、wrong up、6KRO超過、unsupported usage、prepared snapshotの不変性をpure testで確認。

`dotnet test tests/OpenLogicool.Architecture.Tests/OpenLogicool.Architecture.Tests.csproj --logger "console;verbosity=minimal"` は5件green。Inputの既存依存方向を変更していない。

peertable room OpenLogicoolへ設計反証を#901、具体byte案を#902で依頼した。独立read-only監査でsequence 0、予約usage、可変prepared snapshotの3件を検出し、codec検証・不変snapshot・再現testへ反映した。
修正後の再監査はPASS、残存P0/P1/P2は0件。最大42-byte frameと16-bit演算はATmega32U4で実装可能と判定した。

未追跡の`probe-output/ui-test-scenario-20260822-094519-943.json`は対象外であり、変更・追加していない。
