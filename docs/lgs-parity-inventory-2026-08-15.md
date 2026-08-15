# LGS 9.04.49 parity inventory（Deliverable 0A / 2026-08-15）

Input Studio が代替すべき LGS の機能台帳。**一次資料は LGS 自身の profile XML スキーマ**（LGS UI の画面を人が総なめする方法より、機能の網羅性・正確性ともに優る）。UI 側の確認は本台帳の空欄を埋める作業として後続で行う。

- 取得元: `%LOCALAPPDATA%\Logitech\Logitech Gaming Software\`（read-only 参照。ファイル内容は個人設定を含むため repo へ収載しない）
- 実測 profile 3本: 既定プロファイル（170 macro / 307 assignment）、ゲーム A（144/126）、ゲーム B（136/116）
- namespace: `http://www.logitech.com/Cassandra/2010.7/Profile`、macro 系は `.../2010.1/Macros/*`

## 1. LGS の設定モデル（XML から確定）

```
profiles
└ profile            @name @guid @launchable @lastplayeddate @gameid @gkeysdk @gpasupported @lock
  ├ description
  ├ target           @path            ← アプリ関連付け（実行ファイル絶対パス）
  ├ signature        @value @name @key @executable
  ├ macros
  │ └ macro          @guid @name @color @hidden @devicecategory
  │   ├ keystroke    → key@value（複数可）＋ modifier@value
  │   ├ mousefunction → do@task
  │   ├ multikey / script / hotkeys / function / natural
  ├ assignments
  │ └ assignment     @contextid @macroguid @shiftstate @original @backup
  └ （device 設定）  mode@shiftstate[@color] / dpitable@defaultindex@syncxy@shiftindex
                     / dpi@x@y@enabled / reportrate@rate / powermode@value
                     / movement@acceleration@speed / backlight@devicemodel / pointer@devicemodel
```

### 確定した重要な意味

| 概念 | LGS での表現 | Input Studio の対応方針 |
|---|---|---|
| **アプリ関連付け** | `target@path`（実行ファイル絶対パス1本） | ApplicationIdentity（full path / package / window matcher、§6.4）。LGS より広い。**移行時は path → ApplicationIdentity の1:1 変換で足りる** |
| **割当先（contextid）** | `G1`〜`G29`（G13 系）と `Button1`〜`Button20`（G600 系）の2語彙 | 実測した raw report の control ID（[probes/g600-input-map-2026-08-15.md](probes/g600-input-map-2026-08-15.md)）と対応付ける。**G600 の Button1〜20 は F3–F5 profile の G1–G20 と同順**（[probes/g600-profile-decode-2026-08-15.md](probes/g600-profile-decode-2026-08-15.md)）で、実測の bit 順とも一致する |
| **層（layer）** | `shiftstate` 1〜6 を実測（assignment 549 件が全 6 値に分布） | BindingRevision の layer。**LGS は最大6層**を持つ。G-Shift 単独より多く、M1/M2/M3 × G-Shift の組み合わせと推定（要 UI 確認） |
| **macro の再利用** | macro は guid を持ち、assignment が `macroguid` で参照する多対1 | SemanticAction の stable ID と同じ構造。移行で意味を落とさない |
| **`original` / `backup` 属性** | assignment ごとに真偽 | LGS 自身が「既定割当」と「利用者変更」を区別している。移行時に既定のままの割当を持ち込まない判断材料になる |

### macro task 語彙（実測・全17種）

`leftclick` `rightclick` `middleclick` `back` `forward` `dpiup` `dpidown` `defaultdpi` `dpishift` `dpicycling` `gshift` `modeswitchg600` `modeswitch` `copy` `paste` `cut` `cycle_brightness`

**F3–F5 profile 解読で得た mouseCode 語彙（0x01–0x05, 0x11–0x17）と完全に対応する。** LGS の UI 語彙と device firmware の語彙が1対1で繋がることが、read-only の2経路から独立に確認できた。

## 2. Input Studio が満たすべき parity 項目

| # | LGS 機能 | 根拠 | Input Studio 対応 |
|---|---|---|---|
| 1 | ゲーム別プロファイルの自動切替 | `target@path` + `lastplayeddate` | APP-006（foreground app 切替） |
| 2 | キーストローク割当（修飾キー付き） | `keystroke`/`key`/`modifier` | 必須 |
| 3 | マウス機能割当 17 種 | 上記 task 語彙 | 必須。うち device 側 code が判明済み |
| 4 | 多層割当（shiftstate 1〜6） | assignment 実測 | layer モデルで対応 |
| 5 | macro の共有・命名・色 | `macro@name@color@guid` | SemanticAction |
| 6 | DPI テーブル・既定 index・shift index | `dpitable`/`dpi` | F3–F5 の DPI 領域と対応済み |
| 7 | レポートレート | `reportrate@rate`（実測 500） | profile byte 24（=0x02）の候補。**要 write 実験で確定** |
| 8 | バックライト色（層ごと） | `mode@color`（#ff0000 等、shiftstate ごと） | F3–F5 の LED RGB と対応済み |
| 9 | ポインタ速度・加速 | `movement@acceleration@speed` | OS 設定との責任分界を要決定 |
| 10 | power mode | `powermode@value` | 未調査 |
| 11 | script（Lua）| `script` 要素（本環境では空） | **製品境界の判断が要る**（任意 script 実行は KP-002 の禁止事項と衝突しうる） |
| 12 | G13 LCD 表示 | 本 XML に現れず | 別経路（output report ID 3×992 bytes、[probes/enumerate](../probe-output/enumerate-2026-08-15.json)）。未調査 |

## 3. 移行（LGS import）への含意

- **XML は完全な一次資料**であり、LGS UI を操作せずに全 profile を機械的に読める。import 機能は XML パーサとして実装でき、LGS の稼働・停止に依存しない。
- ただし **import データを信頼された命令として扱わない**（AGENTS.md 裁定4）。`script` 要素、`target@path`、macro 名は外部入力として検証する。
- `original="true"` の assignment は LGS 既定であり、利用者の意思ではない。import 時に既定割当をそのまま持ち込むと、Input Studio 側の既定と二重になる。**取り込むのは `original="false"` だけ**を初期方針とする。

## 4. 未確認（UI 確認または write 実験が要る）

- shiftstate 1〜6 の正確な意味（M1/M2/M3 × G-Shift の対応表）。UI 照合（§5）でモード別割当ビュー（G600 モード選択ダイヤル・G13 M1/M2/M3 キー）は確認済みだが、G-Shift 層の明示的なビュー切替は今回巡回した画面には現れなかった——**強い推定のまま**
- `reportrate` と profile byte 24 の対応（write 実験で確定。UI 照合で LGS の選択肢は 125〜1000 の8段、現在値 500 が XML と一致）
- `powermode`、`natural`、`multikey`、`function` 要素の意味
- ~~G13 LCD 関連設定の格納場所（XML に無い）~~ → **解決**: `settings.json` の `/lcd/devices/...`（§5）

## 5. UI 照合の結果（2026-08-15・画面代行で実施・確認済み）

LGS 9.04.49 の実画面を G600/G13 の全主要画面を巡回し、台帳12項目と突合した。**profile XML が一次資料として成立する判定は維持**（割当・マクロ・DPI・レポートレート・モード色は XML と UI が一致）。UI にあって profile XML に現れない機能は以下の6面で、いずれも格納先は `settings.json` または device 直接操作。

| # | UI にあって XML に無い機能 | 格納先／性質 | Input Studio への含意 |
|---|---|---|---|
| 1 | LED 効果（サイクル／パルス・速度・スリープ） | `settings.json` `sync_effect_settings`（本機は未設定＝None、強い推定） | onboard LED は静的色のみ（F3–F5 実証済み）。効果は host 制御機能 |
| 2 | firmware 表示・アップデート（G600 のみ。G13 は非サポート明記） | device 直接操作 | 対象外（製品境界） |
| 3 | プロファイル運用策（デフォルト＝fallback・固定＝persistent・サイクルホットキー） | `settings.json` `/profiler/persistentProfile` 等（実測確認） | APP-006 の app レベル設定に対応が要る |
| 4 | G13 LCD アプレット（POP3/RSS/タイマ/時計の選択・表示オプション・ライブプレビュー） | `settings.json` `/lcd/devices/...`（実測確認） | LCD 対応時は applet モデルが前提 |
| 5 | G13 onboard プロファイル転送（プロファイルバーから device スロットへドラッグ、スロット5枠） | device write 面 | **G13 にも onboard 保存が存在**。Input Studio の onboard 対応は G600 だけでなく G13 も設計対象になり得る（未調査・write 凍結対象） |
| 6 | 入力解析（キープレス／ヒートマップ記録） | 分析機能（保存先未確認） | 対象外（分析系。必要なら別 deliverable） |

付随の実測確認:
- ポインタ設定画面: DPI レベル数選択・スライダ・X/Y 軸分離・加速 checkbox・プロファイル別有効化 checkbox——XML の `dpitable`/`dpi@x@y`/`movement@acceleration` と対応
- クイックマクロ録画（設定「一般」タブ）と遅延記録オプション: `settings.json` `/profiler/enableQuickMacroDelays`（実測確認）。録画結果は profile XML の macro になる
- G13 ジョイスティック: 4方向＋押込が割当面に存在。速度は `settings.json` `/profiler/joystickMouseSpeed`（実測確認）
- 照合中に host profile XML への意図しない書込が無いことを、照合後の XML byte 突合で確認した（mode 色ほか全値一致。差分は LGS 再保存による属性順と `lastplayeddate` のみ）
