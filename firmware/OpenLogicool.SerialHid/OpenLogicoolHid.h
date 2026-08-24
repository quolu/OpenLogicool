#pragma once

#include <Arduino.h>
#include <HID.h>

namespace openlogicool {

class OpenLogicoolHid_ {
 public:
  OpenLogicoolHid_();

  bool Apply(uint8_t modifiers, const uint8_t normalKeys[6], uint8_t mouseButtons);
  bool ApplyMouseDelta(uint8_t mouseButtons, int8_t deltaX, int8_t deltaY, int8_t wheel);
  bool AllUp();

 private:
  HIDSubDescriptor descriptor_;
};

extern OpenLogicoolHid_ OpenLogicoolHid;

}  // namespace openlogicool
