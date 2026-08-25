# Game Policy Gate contract

`GamePolicyRecord` は実 ToS を解釈せず、review表示状態とObserve／Assist／Explore／Autoごとの明示許可を記録する。
`GamePolicyGate.Evaluate` はその record を唯一の入力として mode 可否を返す。

schema `0.1.0` はObserve／Assist／Auto、`0.2.0` はExploreを加えた4 modeを表現する。`0.1.0`へExploreを混ぜたrecordは拒否し、旧schemaの意味を後から変えない。

mode可否は`AllowedModes`だけで決める。review statusは利用者への表示情報であり、許可済み通常操作を無効化しない。
許可リスト外のmodeだけを拒否する。AI、OCR、Web情報から`AllowedModes`を変更しない。

gate は SendInput の API 結果、Playbook の import 元、dispatch delegate を受け取らない。入力 API の受理は規約許可の
根拠にならず、import Playbook も同じ `GamePolicyRecord` の gate を通過しなければならない。
