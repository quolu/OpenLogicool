#pragma once
#include <stdint.h>
constexpr uint8_t WDTO_15MS = 0;
constexpr uint8_t WDTO_2S = 7;
inline void wdt_enable(uint8_t) {}
inline void wdt_disable() {}
extern int firmwareTestWatchdogFeeds;
inline void wdt_reset() { ++firmwareTestWatchdogFeeds; }
