# Daily two-cycle contract

`DailyTwoCycle.Record` は既存 GameLab の daily reset を実装し直さず、day1 と day2 相当の session 結果を記録する。
両者は同じ `VerifiedEnvScope`、連続した virtual day、異なる session ID を持ち、day2 は day1 の成功 action path を
完全に replay しなければならない。

`DailyTwoCycleReport.DayOneVerified` は常に `false` である。初日の成功は翌日相当の別 session replay が成立しても、
この contract から Verified へ昇格しない。
