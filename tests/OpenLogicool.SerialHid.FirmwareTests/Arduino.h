#pragma once

#include <stdint.h>
#include <stddef.h>
#include <deque>
#include <vector>

extern uint32_t firmwareTestMillis;
inline uint32_t millis() { return firmwareTestMillis; }
extern uint8_t MCUSR;
constexpr uint8_t WDRF = 3;

struct FirmwareTestSerial {
  uint32_t baudRate = 115200;
  bool portOpen = true;
  std::deque<uint8_t> incoming;
  std::vector<uint8_t> outgoing;
  void begin(unsigned long) {}
  uint32_t baud() const { return baudRate; }
  bool dtr() const { return portOpen; }
  int available() const { return static_cast<int>(incoming.size()); }
  int read() { const auto value = incoming.front(); incoming.pop_front(); return value; }
  size_t write(const uint8_t* bytes, size_t length) {
    outgoing.insert(outgoing.end(), bytes, bytes + length);
    return length;
  }
};
extern FirmwareTestSerial Serial;
struct FirmwareTestUsb {
  bool connected = true;
  bool configured() const { return connected; }
};
extern FirmwareTestUsb USBDevice;
