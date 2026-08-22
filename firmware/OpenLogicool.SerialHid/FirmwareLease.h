#pragma once

#include <Arduino.h>

#include "ProtocolV1.h"

namespace openlogicool {

class FirmwareLease {
 public:
  void Reset() {
    armed_ = false;
    lastValidFrameAt_ = 0;
  }

  void Arm(uint32_t now) {
    lastValidFrameAt_ = now;
    armed_ = true;
  }

  bool IsExpired(uint32_t now) const {
    return armed_ &&
        static_cast<uint32_t>(now - lastValidFrameAt_) >= protocol_v1::kLeaseMilliseconds;
  }

 private:
  bool armed_ = false;
  uint32_t lastValidFrameAt_ = 0;
};

}  // namespace openlogicool
