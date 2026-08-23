# G13 LCD プリセット表示・設定 delivery

- 実施日: 2026-08-23
- 対象: G13 Native LCD campaign Phase 2機能中核／Phase 3設定面の一部
- 判定: **確認済み**（Phase 2 Exit全体は未判定）

## 1. 成立した製品経路

- `WorkspaceDocument`へG13 LCD設定を追加し、画像またはテキストを960-byte framebufferとしてrevisionへ保存する。
- Input StudioのG13ペインから「画像を選ぶ」「テキストを表示」「共通表示に戻す」を操作できる。
- `ResidentInputHost`は既存のapp-first判定を再利用し、path／package一致したworkspaceのLCD設定をG13 LCD workerへ渡す。
- 対象workspaceにLCD設定がない時、Unknown時、共通設定時はWindows画像へ戻す。
- 画像path自体には依存せず、変換済みframebufferを保存するため、元画像を移動または削除しても表示できる。
- 特定アプリが共通設定と同じprofileを参照していた場合は、編集前にアプリ専用workspaceへ分岐する。これによりアプリのLCD変更が共通設定を上書きしない。

## 2. 実機・実データ確認

1. Windows画像をresident hostからG13へ送り、G13 LCD上の表示を確認した。
2. 指定されたNIKKE画像をbitmap converterからG13へ送り、G13 LCD上の表示を確認した。
3. 実DBのNIKKE行を`ws-nikke-G13`／`ws-nikke-G600`へ分離し、NIKKE workspaceに画像frame 960 bytes、共通workspaceにLCD設定なしが保存されていることをexportで照合した。
4. 一時DBでTaskBarHeroをNIKKE workspaceへ関連付け、前面へ移した。resident logで次を確認した。
   - G13実機1台、G600実機1台、G13 LCD runtime started
   - `foreground state: path 一致`
   - `profile switch: G13 -> 'ws-nikke-G13'`
   - handled shutdownとG600 leftover restore成立
5. 実画面でG13 LCD設定欄、画像選択、未保存表示、右ペイン下端の保存、保存完了を確認した。

## 3. focused test

| 対象 | 結果 |
|---|---:|
| Devices.G13 | 28 green |
| Profiles | 25 green |
| Persistence | 29 green |
| Desktop | 81 green |
| Host | 116 green |
| Architecture | 6 green |

主な固定事項:

- image／text設定の960-byte検証と全device profileへの伝播
- SQLite revisionの再open復元
- textのWPF描画結果
- app一致、Unknown、共通設定のframe選択
- G600側だけがpath一致した場合も同じworkspaceのG13 LCD設定を選ぶこと
- 共通profileを再利用するアプリを専用workspaceへ分岐し、共通設定を巻き込まないこと

## 4. 未判定

Phase 2 Exit全体に残るのは、LCD更新中のfocused latency gateと、実機抜差し後の再表示である。
今回の依頼であるプリセット単位の画像／テキスト設定と前面連動は成立しているが、campaign全体のExitとは分けて扱う。
