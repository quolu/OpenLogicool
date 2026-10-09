#include <cstdio>
#include <cstdlib>
#include "Arduino.h"
#include "OpenLogicoolHid.h"

uint32_t firmwareTestMillis = 1000;
uint8_t MCUSR = 0;
int firmwareTestWatchdogFeeds = 0;
FirmwareTestSerial Serial;
FirmwareTestUsb USBDevice;
static int releases = 0;
static int applies = 0;

namespace openlogicool {
OpenLogicoolHid_::OpenLogicoolHid_() : descriptor_(nullptr, 0) {}
bool OpenLogicoolHid_::Apply(uint8_t, const uint8_t[6], uint8_t) { ++applies; return true; }
bool OpenLogicoolHid_::ApplyMouseDelta(uint8_t, int8_t, int8_t, int8_t) { return true; }
bool OpenLogicoolHid_::AllUp() { ++releases; return true; }
OpenLogicoolHid_ OpenLogicoolHid;
}

// 配布するsketchそのものをfake時計・serial・HID境界で動かす。
#include "../../firmware/OpenLogicool.SerialHid/OpenLogicool.SerialHid.ino"

static void Check(bool condition, const char* detail) {
  if (!condition) { std::fprintf(stderr, "%s\n", detail); std::exit(1); }
}

static FrameView Send(MessageKind kind, uint16_t sequence, const std::vector<uint8_t>& payload = {}) {
  uint8_t bytes[kMaxFrameLength] = {};
  const auto length = EncodeFrame(kind, sequence, payload.data(), static_cast<uint16_t>(payload.size()), bytes, sizeof(bytes));
  Serial.outgoing.clear();
  for (uint16_t index = 0; index < length; ++index) Serial.incoming.push_back(bytes[index]);
  loop();
  FrameView result = {};
  FaultCode fault = FaultCode::InternalFault;
  uint16_t correlated = 0;
  uint8_t offending = 0;
  Check(DecodeFrame(Serial.outgoing.data(), static_cast<uint16_t>(Serial.outgoing.size()), result, fault, correlated, offending), "応答frameが不正");
  return result;
}

static void Connect() {
  firmwareTestMillis += 1000;
  USBDevice.connected = true;
  setup();
  Check(Send(MessageKind::Hello, 1, {1, 0, 0, 15, 0}).kind == MessageKind::Ready, "HELLO失敗");
  Check(Send(MessageKind::AllUp, 2).kind == MessageKind::Ack, "ALL_UP失敗");
}

int main() {
  Connect();
  const auto idleReleases = releases;
  firmwareTestMillis += 200;
  loop();
  Check(releases == idleReleases, "解放済み入力を再送した");
  Check(Send(MessageKind::Heartbeat, 3).kind == MessageKind::Ack, "無入力200ms後の接続が失われた");
  Check(Send(MessageKind::Heartbeat, 9).kind == MessageKind::Fault, "不正な連番を受理した");
  Check(Send(MessageKind::Heartbeat, 4).kind == MessageKind::Ack, "正しい次の連番を拒否した");

  for (const uint8_t heldIndex : std::vector<uint8_t>{0, 1, 6, 7}) {
    Connect();
    std::vector<uint8_t> payload(8, 0);
    payload[heldIndex] = heldIndex == 0 ? 2 : heldIndex == 7 ? 1 : 0x68;
    if (heldIndex == 6) {
      for (uint8_t index = 1; index <= 6; ++index) payload[index] = static_cast<uint8_t>(index + 3);
    }
    Check(Send(MessageKind::SetState, 3, payload).kind == MessageKind::Ack, "保持入力失敗");
    const auto before = releases;
    firmwareTestMillis += 149;
    loop();
    Check(releases == before, "150ms前に解放した");
    firmwareTestMillis += 1;
    loop();
    Check(releases == before + 1, "保持中の入力を150msで解放しなかった");
    const auto fault = Send(MessageKind::Heartbeat, 4);
    Check(fault.kind == MessageKind::Fault && fault.payload[0] == static_cast<uint8_t>(FaultCode::SequenceViolation), "期限切れの保持sessionを暗黙再開した");
  }

  Connect();
  Check(Send(MessageKind::SetState, 3, {0, 0x68, 0, 0, 0, 0, 0, 1}).kind == MessageKind::Ack, "chord失敗");
  Check(Send(MessageKind::SetState, 4, std::vector<uint8_t>(8, 0)).kind == MessageKind::Ack, "解放失敗");
  const auto releasedApplies = applies;
  firmwareTestMillis += 1000;
  loop();
  Check(Send(MessageKind::Heartbeat, 5).kind == MessageKind::Ack, "全解放後の接続が失われた");
  Check(applies == releasedApplies, "入力を再送した");

  USBDevice.connected = false;
  loop();
  USBDevice.connected = true;
  loop();
  Check(Send(MessageKind::Heartbeat, 6).kind == MessageKind::Fault, "USB再接続後の旧sessionを受理した");
  Serial.baudRate = 1200;
  Serial.portOpen = false;
  const auto feeds = firmwareTestWatchdogFeeds;
  loop();
  Check(firmwareTestWatchdogFeeds == feeds, "書込み用resetをwatchdog給餌で妨げた");
  std::printf("session|ok|idle200|idle1000|held150|sequence|usb-reset|no-replay\n");
}
