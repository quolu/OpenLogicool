#pragma once

#include <Arduino.h>

namespace openlogicool {

constexpr uint16_t kFirmwareRecoveryResetMilliseconds = 1000;

class FirmwareRecoveryTimer {
 public:
  void Reset() {
    armed_ = false;
    armedAt_ = 0;
  }

  void Arm(uint32_t now) {
    if (armed_) return;
    armedAt_ = now;
    armed_ = true;
  }

  bool ShouldReset(uint32_t now) const {
    return armed_ &&
        static_cast<uint32_t>(now - armedAt_) >= kFirmwareRecoveryResetMilliseconds;
  }

 private:
  bool armed_ = false;
  uint32_t armedAt_ = 0;
};

}  // namespace openlogicool
