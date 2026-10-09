# 書込みresetの一次資料

取得日: 2026-10-09。確度: 一次資料の確認済み。旧sketchとの相互作用の実測・推論はコンパイル記事へ分離する。

[Arduino公式CDC.cpp](https://raw.githubusercontent.com/arduino/ArduinoCore-avr/master/cores/arduino/CDC.cpp)からの原文抜粋。

```cpp
wdt_enable(WDTO_120MS);
```

[SparkFun公式Pro Micro guide](https://learn.sparkfun.com/tutorials/pro-micro--fio-v3-hookup-guide/troubleshooting-and-faq)からの原文抜粋。

> resetting twice quickly will get the Pro Micro to enter bootloader mode for eight seconds.
