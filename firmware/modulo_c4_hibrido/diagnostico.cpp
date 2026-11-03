/*
 * Modulo C4 hibrido v3.2: bounded, cooperative AIO Assist transport.
 * Seed/key transform adapted from Ludwig V., arduino-psa-diag (2020-2025),
 * https://github.com/ludwig-v/arduino-psa-diag
 * Original PSA algorithm: Wouter Bokslag and Jason F. (prototux).
 * GPL-3.0-or-later; see COPYING. Distributed WITHOUT ANY WARRANTY.
 * Compatibility subset, not a general-purpose PSA diagnostic adapter.
 */
#include <Arduino.h>
#include <string.h>
#include "diagnostico.h"
#include "src/mcp2515_local/mcp2515.h"

namespace {
MCP2515 diag(10, 1000000);
constexpr uint16_t CAPACITY = 256, LINE_CAPACITY = 365;
char line[LINE_CAPACITY];
uint8_t data[CAPACITY];
uint16_t lineSize, length, position, outputPosition;
bool dropping, ready, connected, failed, txPending, keepAlive, requestPending;
bool outputActive, flowWaiting, receiving, sending;
uint8_t sequence, blockSize, blockSent, waitFrames, unlockStage;
uint32_t separationUs;
uint32_t lastHost, lastChar, lastProgress, lastKeepAlive, lastTx, nextTxUs;
enum { IDLE, SESSION, SEED, KEY };

bool busy() {
  return txPending || requestPending || outputActive || flowWaiting || receiving || sending;
}
int nibble(char c) {
  if (c >= '0' && c <= '9') return c - '0';
  if (c >= 'A' && c <= 'F') return c - 'A' + 10;
  return -1;
}
void resetTransaction() {
  length = position = outputPosition = 0;
  requestPending = outputActive = flowWaiting = receiving = sending = false;
  unlockStage = IDLE;
}
void fail() {
  // Latch the diagnostic side only. No automatic retry of a write/unlock.
  failed = true; connected = false; keepAlive = false;
  resetTransaction(); txPending = false;
  diag.setConfigMode(); // abort pending automatic controller transmissions
  Serial.println(F("7F0000"));
}
bool send(const can_frame &frame) {
  if (failed || !connected || txPending) { fail(); return false; }
  diag.clearTXInterrupts();
  if (diag.sendMessage(MCP2515::TXB0, &frame) != MCP2515::ERROR_OK) {
    fail(); return false;
  }
  txPending = true; lastTx = millis();
  return true;
}
bool single(const uint8_t *payload, uint8_t size, bool expect = true) {
  can_frame f = {};
  f.can_id = 0x764; f.can_dlc = size + 1; f.data[0] = size;
  memcpy(f.data + 1, payload, size);
  if (!send(f)) return false;
  requestPending = expect; lastProgress = millis();
  return true;
}
void emit(uint16_t size) {
  length = size; outputPosition = 0; outputActive = true;
}
long transform(uint8_t msb, uint8_t lsb, const uint8_t *secret) {
  // The PSA transform consumes a signed 16-bit word, including the app key.
  // Explicit sign extension makes AVR and host tests produce identical keys.
  const uint16_t word = ((uint16_t)msb << 8) | lsb;
  const long value = (word & 0x8000) ? (long)word - 65536L : word;
  long result = (value % secret[0]) * secret[2] - (value / secret[0]) * secret[1];
  if (result < 0) result += secret[0] * secret[2] + secret[1];
  return result;
}
uint32_t seedKey(uint16_t pin, uint32_t seed) {
  const uint8_t a[] = {0xB2, 0x3F, 0xAA}, b[] = {0xB1, 0x02, 0xAB};
  // Reference layout for seed A B C D: high uses A D; low uses B C.
  const uint16_t hi = transform(pin >> 8, pin, a) | transform(seed >> 24, seed, b);
  const uint16_t lo = transform(seed >> 16, seed >> 8, a) | transform(hi >> 8, hi, b);
  return ((uint32_t)hi << 16) | lo;
}
void response(uint16_t size) {
  // Tester-present replies do not complete a different pending transaction.
  if (size == 1 && data[0] == 0x7E) return;
  if (!requestPending) return;
  lastProgress = millis();
  if (size == 3 && data[0] == 0x7F && data[2] == 0x78) {
    emit(size); return; // retain wait state; never resend automatically
  }
  requestPending = false;
  if (unlockStage == SESSION && size == 2 && data[0] == 0x50 && data[1] == 0xC0) {
    const uint8_t cmd[] = {0x27, 0x83};
    unlockStage = SEED; single(cmd, sizeof(cmd)); return;
  }
  if (unlockStage == SEED && size == 6 && data[0] == 0x67 && data[1] == 0x83) {
    const uint32_t seed = ((uint32_t)data[2] << 24) | ((uint32_t)data[3] << 16) |
                          ((uint32_t)data[4] << 8) | data[5];
    const uint32_t key = seedKey(0xD91C, seed);
    const uint8_t cmd[] = {0x27, 0x84, (uint8_t)(key >> 24), (uint8_t)(key >> 16),
                          (uint8_t)(key >> 8), (uint8_t)key};
    unlockStage = KEY; single(cmd, sizeof(cmd)); return;
  }
  if (unlockStage != IDLE) {
    if (unlockStage == KEY && size == 2 && data[0] == 0x67 && data[1] == 0x84) {
      keepAlive = true; lastKeepAlive = millis();
    } else keepAlive = false;
    unlockStage = IDLE;
  }
  emit(size);
}
bool configure() {
  if (diag.reset() != MCP2515::ERROR_OK ||
      diag.setBitrate(CAN_500KBPS, MCP_8MHZ) != MCP2515::ERROR_OK) return false;
  if (diag.setFilterMask(MCP2515::MASK0, false, 0x7FF) != MCP2515::ERROR_OK ||
      diag.setFilterMask(MCP2515::MASK1, false, 0x7FF) != MCP2515::ERROR_OK) return false;
  for (uint8_t i = 0; i < 6; ++i)
    if (diag.setFilter((MCP2515::RXF)i, false, 0x664) != MCP2515::ERROR_OK) return false;
  return diag.setListenOnlyMode() == MCP2515::ERROR_OK;
}
void command() {
  line[lineSize] = 0;
  lastHost = millis();
  if (!strcmp(line, "!INFO")) {
    Serial.println(F("C4-HIBRIDO 3.2 CS9=125k CS10=500k SPI=1MHz XTAL=8MHz")); return;
  }
  if (!strcmp(line, "V")) {
    // 2.0 is the wire protocol version required by the existing AIO Assist.
    Serial.println(F("2.0")); return;
  }
  if (!strcmp(line, "!STATUS")) {
    Serial.print(F("DIAG ready=")); Serial.print(ready);
    Serial.print(F(" connected=")); Serial.print(connected);
    Serial.print(F(" fault=")); Serial.print(failed);
    Serial.print(F(" busy=")); Serial.println(busy());
    climateStatus(); return;
  }
  if (!strcmp(line, "S")) {
    keepAlive = false; Serial.println(F("OK")); return;
  }
  if (!strcmp(line, ">764:664")) {
    if (busy()) { fail(); return; }
    // A fresh connection explicitly clears a diagnostic fault.
    failed = false; connected = false;
    ready = configure();
    if (!ready || diag.setNormalOneShotMode() != MCP2515::ERROR_OK) { fail(); return; }
    connected = true; keepAlive = false; resetTransaction();
    Serial.println(F("OK")); return;
  }
  if (failed || !connected || busy()) { Serial.println(F("7F0000")); return; }
  if (!strcmp(line, "KK")) {
    keepAlive = true; lastKeepAlive = millis(); Serial.println(F("OK")); return;
  }
  if (!strcmp(line, ":D91C:83:C0")) {
    const uint8_t cmd[] = {0x10, 0xC0};
    unlockStage = SESSION; single(cmd, sizeof(cmd)); return;
  }
  const bool read = !strcmp(line, "21FE") || !strcmp(line, "21B0") ||
                    !strcmp(line, "2182") || !strcmp(line, "2180") ||
                    !strcmp(line, "21B1") || !strcmp(line, "21B2");
  const bool write = lineSize == 364 && !strncmp(line, "3BB1", 4);
  if (!read && !write && strcmp(line, "1081")) { Serial.println(F("7F0000")); return; }
  for (uint16_t i = 0; i < lineSize; i += 2) {
    const int hi = nibble(line[i]), lo = nibble(line[i + 1]);
    if (hi < 0 || lo < 0) { Serial.println(F("7F0000")); return; }
    data[i / 2] = (hi << 4) | lo;
  }
  length = lineSize / 2;
  if (length <= 7) { single(data, length); return; }
  can_frame f = {}; f.can_id = 0x764; f.can_dlc = 8;
  f.data[0] = 0x10 | (length >> 8); f.data[1] = length;
  memcpy(f.data + 2, data, 6);
  position = 6; sequence = 1; waitFrames = blockSent = 0;
  if (send(f)) {
    flowWaiting = requestPending = true; lastProgress = millis();
  }
}
void receive(const can_frame &f) {
  if (f.can_id != 0x664 || !f.can_dlc || f.can_dlc > 8) return;
  const uint8_t kind = f.data[0] >> 4;
  if (kind == 3 && flowWaiting) {
    if (f.can_dlc < 3) { fail(); return; }
    const uint8_t status = f.data[0] & 15;
    if (status == 1 && ++waitFrames <= 3) { lastProgress = millis(); return; }
    if (status != 0) { fail(); return; }
    const uint8_t st = f.data[2];
    if (st <= 0x7F) separationUs = (uint32_t)st * 1000;
    else if (st >= 0xF1 && st <= 0xF9) separationUs = (st - 0xF0) * 100;
    else { fail(); return; }
    blockSize = f.data[1]; blockSent = 0; waitFrames = 0;
    flowWaiting = false; sending = true; nextTxUs = micros() + separationUs;
    lastProgress = millis(); return;
  }
  if (!requestPending || sending || flowWaiting || outputActive) return;
  if (kind == 0) {
    const uint8_t size = f.data[0] & 15;
    if (receiving || !size || size > 7 || size + 1 > f.can_dlc) { fail(); return; }
    memcpy(data, f.data + 1, size); response(size); return;
  }
  if (kind == 1) {
    if (receiving || f.can_dlc != 8) { fail(); return; }
    length = ((uint16_t)(f.data[0] & 15) << 8) | f.data[1];
    if (length <= 7 || length > CAPACITY) { fail(); return; }
    memcpy(data, f.data + 2, 6); position = 6; sequence = 1;
    receiving = true; lastProgress = millis();
    can_frame fc = {}; fc.can_id = 0x764; fc.can_dlc = 3;
    fc.data[0] = 0x30; fc.data[2] = 5; // request 5ms between consecutive frames
    send(fc); return;
  }
  if (kind == 2 && receiving) {
    const uint16_t remaining = length - position;
    const uint8_t take = remaining < 7 ? remaining : 7;
    if ((f.data[0] & 15) != sequence || f.can_dlc < take + 1) { fail(); return; }
    memcpy(data + position, f.data + 1, take);
    position += take; sequence = (sequence + 1) & 15; lastProgress = millis();
    if (position == length) { receiving = false; response(length); }
  }
}
} // namespace

void diagnosticSetup() {
  Serial.begin(115200);
  // No startup chatter on the AIO Assist protocol port.
  ready = configure(); failed = !ready;
}
void diagnosticPoll() {
  if (outputActive) {
    // Drain before reading CAN again: do not drop a final reply arriving
    // immediately after an NRC78 in the same hardware receive batch.
    for (uint8_t n = 0; n < 8 && outputPosition < length && Serial.availableForWrite() >= 2; ++n) {
      const uint8_t value = data[outputPosition++];
      const char hex[] = "0123456789ABCDEF";
      Serial.write(hex[value >> 4]); Serial.write(hex[value & 15]);
    }
    if (outputPosition == length && Serial.availableForWrite() >= 1) {
      Serial.write('\n'); outputActive = false;
    }
  }
  if (connected && !failed) {
    if (txPending) {
      const uint8_t interrupts = diag.getInterrupts();
      if (interrupts & 0x04) {
        diag.clearTXInterrupts(); txPending = false;
        if (sending) nextTxUs = micros() + separationUs;
      }
      else if (millis() - lastTx >= 100) { fail(); return; }
    }
    if (diag.getErrorFlags() & (MCP2515::EFLG_TXBO | MCP2515::EFLG_TXEP |
        MCP2515::EFLG_RXEP | MCP2515::EFLG_RX0OVR | MCP2515::EFLG_RX1OVR)) {
      fail(); return;
    }
    for (uint8_t n = 0; n < 4 && !outputActive; ++n) {
      can_frame f = {};
      const MCP2515::ERROR result = diag.readMessage(&f);
      if (result == MCP2515::ERROR_NOMSG) break;
      if (result != MCP2515::ERROR_OK) { fail(); return; }
      receive(f);
      if (failed) return;
    }
  }
  // Bound each pass so the climate receive buffers are serviced frequently.
  for (uint8_t n = 0; n < 24 && Serial.available(); ++n) {
    const char c = Serial.read();
    lastChar = millis();
    if (c == '\r') continue;
    if (c == '\n') {
      if (dropping) Serial.println(F("7F0000"));
      else if (lineSize) command();
      lineSize = 0; dropping = false;
    } else if (!dropping) {
      if (lineSize >= LINE_CAPACITY - 1) { dropping = true; lineSize = 0; }
      else line[lineSize++] = c;
    }
  }
  if ((lineSize || dropping) && millis() - lastChar > 1000) {
    lineSize = 0; dropping = false; Serial.println(F("7F0000"));
  }
  if (failed || !connected) return;
  if (sending && !txPending && (int32_t)(micros() - nextTxUs) >= 0) {
    can_frame f = {}; f.can_id = 0x764;
    f.data[0] = 0x20 | sequence;
    const uint16_t remaining = length - position;
    const uint8_t take = remaining < 7 ? remaining : 7;
    f.can_dlc = take + 1; memcpy(f.data + 1, data + position, take);
    if (!send(f)) return;
    position += take; sequence = (sequence + 1) & 15;
    nextTxUs = micros() + separationUs; lastProgress = millis();
    if (position == length) sending = false;
    else if (blockSize && ++blockSent >= blockSize) { sending = false; flowWaiting = true; }
  }
  if (requestPending && millis() - lastProgress > 10000) { fail(); return; }
  if (keepAlive && !busy() && !lineSize && !dropping && millis() - lastKeepAlive >= 1000) {
    const uint8_t cmd[] = {0x3E};
    if (single(cmd, sizeof(cmd), false)) lastKeepAlive = millis();
  }
  if (!busy() && !lineSize && millis() - lastHost > 60000) {
    keepAlive = false; connected = false; diag.setListenOnlyMode();
  }
}
