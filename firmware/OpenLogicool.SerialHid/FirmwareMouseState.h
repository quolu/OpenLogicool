#pragma once

#include <Arduino.h>

namespace openlogicool {

class FirmwareMouseState {
 public:
  void Reset() { buttons_ = 0; }
  void CommitButtons(uint8_t buttons) { buttons_ = static_cast<uint8_t>(buttons & 0x1F); }
  uint8_t Buttons() const { return buttons_; }

 private:
  uint8_t buttons_ = 0;
};

}  // namespace openlogicool
