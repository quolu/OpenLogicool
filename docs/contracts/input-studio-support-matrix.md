# Input Studio support matrix 契約

## 公開 claim

公開 claim は **Partial LGS Replacement** である。canonical LGS inventory に未確認または未対応の行が一つでもある限り、`LGS Parity` は表示しない。

## 表示規則

- `Supported` は reference machine で実機受入済みの capability だけに使う。
- `StrongInference`、`Unverified`、`Unsupported` は `Supported` と同じ意味に扱わず、その状態を表示する。
- G600 は B変種（G9〜G20 の F13〜F24 remap）を主 route、A方式（slot 切替）を補完として表示する。
- G600 onboard profile は F3/F4/F5 の3 slot に限定し、F6 read 不可は `Unsupported` と表示する。

## 製品面

`InputStudioSupportMatrix` は公開面の pure data source である。I/O、device write、LGS 実行は行わない。
