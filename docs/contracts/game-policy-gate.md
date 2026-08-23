# Game Policy Gate contract

`GamePolicyRecord` は実 ToS を解釈せず、確認状態と Observe／Assist／Explore／Auto ごとの許可だけを記録する。
`GamePolicyGate.Evaluate` はその record を唯一の入力として mode 可否を返す。

schema `0.1.0` はObserve／Assist／Auto、`0.2.0` はExploreを加えた4 modeを表現する。`0.1.0`へExploreを混ぜたrecordは拒否し、旧schemaの意味を後から変えない。

review status が Unverified、Changed、InterpretationUnknown の時、Assist、Explore、Auto は許可リストに含まれていても
無効である。Observe は record に明示許可された場合だけ通る。Confirmed でも許可リスト外の mode は拒否する。

gate は SendInput の API 結果、Playbook の import 元、dispatch delegate を受け取らない。入力 API の受理は規約許可の
根拠にならず、import Playbook も同じ `GamePolicyRecord` の gate を通過しなければならない。
