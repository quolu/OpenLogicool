# ADR: Serial HID live input suppression and latency measurement

- Date: 2026-08-23
- Status: accepted
- Scope: `t08-g13-g600-live-smoke`

## Decision

- G600のonboard抑止はoutput routeごとに分ける。SendInput routeは従来どおりG9〜G20をF13〜F24へ置換し、Serial HID routeはG6〜G20の通常層／G-Shift層をno-output cellへする。Serial HID使用中にlegacy F13〜F24を残さない。
- 現在payloadがOpenLogicool既知の抑止状態なら保存済みbaselineを維持して目的routeのpayloadを再構築する。LGS等の外部clean payloadなら、その実値で古いbaselineを置き換える。route切替でbaselineを抑止済みpayloadへ汚染しない。
- G13／G600 live sourceはqueue投入後に`AutoResetEvent`をsignalする。`FastPathPump`はsource signalとprofile変更／stop signalを`WaitAny`し、live pathで`Thread.Sleep(1)` pollingを使わない。signalを持たないfake／recorded sourceだけは既存互換の1ms pollingを維持する。
- NFR-002のp99受入は、同じmonotonic clockでinput timestampからSerial HID matching ACK完了までを200 edge測る専用probeが所有する。物理live smokeの少数edgeは実Raw Input経路、layer／profile、二重配送、fault／復帰を確認し、latency値は記録するがp99標本とは扱わない。

## Reason

Resident hostがSerial HID routeでもF13〜F24 remapを適用していたため、G600 G9はlegacy F13とSerial HID F17を同時配送した。route名だけを条件にwrite自体を止めるとG600 legacy配送が残るため、Serial HID専用のno-output payloadが必要だった。

また、空queue時の`Thread.Sleep(1)`はWindowsのscheduler量子により約15.6msまで延び、ACK自体が速くてもdispatch p99を悪化させた。live sourceが既にqueue投入点を所有するため、その境界でsignalするのが最小の根治となる。

## Verification

- G600 G9〜G12のSerial HID live actionは、期待したF17〜F20だけをWindowsの非injected eventとして観測し、legacy F13〜F16漏れ0。
- G13／G600単体、同時入力、両layer、foreground profile、保存後再起動、board抜線fault、no fallback、明示再起動復帰が成立。
- event駆動化後の200 edge測定はp50 1.747ms、p95 2.522ms、p99 3.425ms、max 12.902ms。processed／trace／hardware eventはいずれも200、drop／wrong release／stuck／injectedは0。
- post-fix物理spotはG13 G1とG600 G9のdown/up各1回を観測し、4 edge中3件が4.478ms以下、G13初回だけ11.109ms。少数標本をp99合否へ流用せず、最大値として記録する。

## Evidence boundary

確認済みなのはRaw Input以降の製品経路、Serial HID matching ACK、Windows low-level hookでの非injected event、G600 legacy漏れなし、resident fault／明示復帰である。対象game内成功とhard-kill releaseは本Taskへ含めず、`t09-hard-kill-and-game-observation`が所有する。
