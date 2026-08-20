# Daily recovery contract

`DailyRecovery.Plan` は二日 cycle の day2 session と既知 action path から、既存 resume/fault 境界へ渡す
再開候補を作る。Interrupted、ManualIntervention、ForegroundLost、CaptureLost、OutcomeUnknown の全てを
同じ pure な plan として扱う。

この型は fault を再実装せず、foreground/capture を監視せず、dispatch や input を実行しない。daily reset も
持たず、初日の `DayOneVerified` は変更しない。
