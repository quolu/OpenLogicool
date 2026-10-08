# IMEモード状態と起動時の押下判定

出典: [Microsoft一次資料](raw/ime-key-state-20261009.md)、Windows native実測。取得日: 2026-10-09。確度: 今回の停止原因・再現試験・修理後の試験成功と実機再開は確認済み。他の配列・IMEでの発生頻度は未確認。

Bot起動時に、実入力0件の時点からHeldCount=1となり、約9分入力がなくても解放されなかった。コンパス検出・HP観測は継続する一方、送出は0件だった。

実環境では仮想キー0xF3のGetAsyncKeyState上位bitが立ち、MapVirtualKeyWはscan 0x29を返し、逆変換も0xF3へ戻った。SDKでは同じ値をVK_DBE_SBCSCHARとVK_OEM_AUTOが共有する。スキャンコードへの変換成立だけでは、起動時に実キーが保持されている根拠にならない。

修理はIMEモード用0xF0〜0xFBを起動時の押下取込みから外す。起動後のRaw Inputによるdown/up追跡は変更せず、実際のキー操作は従来どおり一時停止させる。無入力時間で押下を強制解除する処理は追加しない。

実測と試験は[evidence](../../evidence/bot-monitor-20261009/acceptance.md)を参照。

別途発生したNanoの無応答は、Windows標準の機器再起動で物理的なUSB抜き挿しをせず復旧した。0xF3のOS状態がdownのままでも、修正版BotはHeldCount=0で再開し、Space送出とHP観測を継続した。[復旧後の実測](../../evidence/nano-software-recovery-20261009/acceptance.md)。
