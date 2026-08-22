#include "OpenLogicoolHid.h"

namespace openlogicool {
namespace {

constexpr uint8_t kKeyboardReportId = 1;
constexpr uint8_t kMouseReportId = 2;

const uint8_t kReportDescriptor[] PROGMEM = {
    0x05, 0x01,        // Usage Page (Generic Desktop)
    0x09, 0x06,        // Usage (Keyboard)
    0xA1, 0x01,        // Collection (Application)
    0x85, kKeyboardReportId,
    0x05, 0x07,        // Usage Page (Keyboard/Keypad)
    0x19, 0xE0,        // Usage Minimum (Keyboard Left Control)
    0x29, 0xE7,        // Usage Maximum (Keyboard Right GUI)
    0x15, 0x00,        // Logical Minimum (0)
    0x25, 0x01,        // Logical Maximum (1)
    0x75, 0x01,        // Report Size (1)
    0x95, 0x08,        // Report Count (8)
    0x81, 0x02,        // Input (Data, Variable, Absolute)
    0x75, 0x08,        // Report Size (8)
    0x95, 0x01,        // Report Count (1)
    0x81, 0x01,        // Input (Constant)
    0x19, 0x00,        // Usage Minimum (Reserved/no event)
    0x2A, 0xDF, 0x00,  // Usage Maximum (0xDF)
    0x15, 0x00,        // Logical Minimum (0)
    0x26, 0xDF, 0x00,  // Logical Maximum (0xDF)
    0x75, 0x08,        // Report Size (8)
    0x95, 0x06,        // Report Count (6)
    0x81, 0x00,        // Input (Data, Array, Absolute)
    0xC0,              // End Collection

    0x05, 0x01,        // Usage Page (Generic Desktop)
    0x09, 0x02,        // Usage (Mouse)
    0xA1, 0x01,        // Collection (Application)
    0x85, kMouseReportId,
    0x09, 0x01,        // Usage (Pointer)
    0xA1, 0x00,        // Collection (Physical)
    0x05, 0x09,        // Usage Page (Button)
    0x19, 0x01,        // Usage Minimum (Button 1)
    0x29, 0x05,        // Usage Maximum (Button 5)
    0x15, 0x00,        // Logical Minimum (0)
    0x25, 0x01,        // Logical Maximum (1)
    0x95, 0x05,        // Report Count (5)
    0x75, 0x01,        // Report Size (1)
    0x81, 0x02,        // Input (Data, Variable, Absolute)
    0x95, 0x01,        // Report Count (1)
    0x75, 0x03,        // Report Size (3)
    0x81, 0x01,        // Input (Constant)
    0x05, 0x01,        // Usage Page (Generic Desktop)
    0x09, 0x30,        // Usage (X)
    0x09, 0x31,        // Usage (Y)
    0x09, 0x38,        // Usage (Wheel)
    0x15, 0x81,        // Logical Minimum (-127)
    0x25, 0x7F,        // Logical Maximum (127)
    0x75, 0x08,        // Report Size (8)
    0x95, 0x03,        // Report Count (3)
    0x81, 0x06,        // Input (Data, Variable, Relative)
    0xC0,              // End Collection
    0xC0,              // End Collection
};

struct KeyboardReport {
  uint8_t modifiers;
  uint8_t reserved;
  uint8_t keys[6];
};

struct MouseReport {
  uint8_t buttons;
  int8_t x;
  int8_t y;
  int8_t wheel;
};

static_assert(sizeof(KeyboardReport) == 8, "keyboard report must be 8 bytes");
static_assert(sizeof(MouseReport) == 4, "mouse report must be 4 bytes");

}  // namespace

OpenLogicoolHid_::OpenLogicoolHid_()
    : descriptor_(kReportDescriptor, sizeof(kReportDescriptor)) {
  HID().AppendDescriptor(&descriptor_);
}

bool OpenLogicoolHid_::Apply(
    uint8_t modifiers,
    const uint8_t normalKeys[6],
    uint8_t mouseButtons) {
  KeyboardReport keyboard = {modifiers, 0, {0, 0, 0, 0, 0, 0}};
  for (uint8_t index = 0; index < 6; ++index) {
    keyboard.keys[index] = normalKeys[index];
  }
  const MouseReport mouse = {static_cast<uint8_t>(mouseButtons & 0x1F), 0, 0, 0};

  if (HID().SendReport(kKeyboardReportId, &keyboard, sizeof(keyboard)) < 0) {
    return false;
  }
  if (HID().SendReport(kMouseReportId, &mouse, sizeof(mouse)) < 0) {
    return false;
  }
  return true;
}

bool OpenLogicoolHid_::AllUp() {
  const KeyboardReport keyboard = {0, 0, {0, 0, 0, 0, 0, 0}};
  const MouseReport mouse = {0, 0, 0, 0};
  const int keyboardResult = HID().SendReport(kKeyboardReportId, &keyboard, sizeof(keyboard));
  const int mouseResult = HID().SendReport(kMouseReportId, &mouse, sizeof(mouse));
  return keyboardResult >= 0 && mouseResult >= 0;
}

OpenLogicoolHid_ OpenLogicoolHid;

}  // namespace openlogicool
