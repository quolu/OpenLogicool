# Game Operator support matrix

`GameOperatorSupportMatrix` は Game Operator の公開 capability を根拠4値で示す Desktop の read-only model である。公開 claim は `Game Operator Preview` に留め、Input Studio の claim や LGS Parity を再利用しない。

## Supported の範囲

- GameLab 内の Durable Automation は crash boundary、停止、修正、再開までの focused crash matrix を根拠に Supported とする。
- proposal の schema、catalog、state、risk の不一致を dispatch 前に拒否する口は Supported とする。AI は Input、device API、SQLite に直接到達しない。
- Data Flow contract は full-screen frame の保存／cloud 送信を既定 OFF とし、evidence crop の送信は Teach での利用者選択と app 単位の明示同意時だけに限る。
- `GamePolicyGate` は未確認・変更済み・解釈不明の policy record で Assist／Auto を拒否する。これは既存 gate の表示であり、規約解釈を実装し直さない。

## 未確認を保持する行

- provider と provider data policy は未選定。
- 実 game の Observe Only と game policy の live 確認は未確認。
- 実 game 用 Verified Autonomous Playbook は t09 の独立 live session 証拠が無いため未確認。

GameLab の Verified 根拠は実 game 環境へ継承しない。したがって matrix は一般的な game 対応、規約許可、実 game の無人実行を主張しない。
