# t05-map004-surface 完了証跡

- 実装: implementer（sonnet×medium）委譲・統括が diff 実読と focused test 再実行で受入
- binding 衝突の全件列挙: WorkspaceCompiler が (DeviceKind, ControlId, LayerId) 重複を全件集計し、1行1衝突で列挙して拒否（拒否の正本は従来どおり Domain 検証・表示の網羅化のみ担当）
- unknown capability 警告: ConfirmedButtons（G13: G1/G2/G20/STICK_PRESS・G600: 全20）外の control への binding・selector に「未確認 control（Experimental）」警告。G13 M1/M2/M3 は実測台帳上「強い推定」のため警告対象（確認済み扱いの根拠なしを実読確認）
- 検証: Profiles 22件（+4）・Host 10件・Architecture 4件 green（worker＋統括の両方で実行）
