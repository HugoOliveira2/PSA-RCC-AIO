// Teste da montagem: botao D2, indicador PNP ativo LOW em D4, CAN CS9.
// Nao inclui EEPROM e nao chama sendMessage. CS10 fica desativado.
// A habilita ACK apenas para bancada isolada; E volta a escuta passiva.
#include <SPI.h>
#include <stdio.h>
#include "src/mcp2515_local/mcp2515.h"

MCP2515 benchCan(9, 1000000);
const uint8_t BUTTON = 2, LED = 4, QUEUE_SIZE = 24;
struct BenchFrame { uint32_t ms; can_frame frame; };
BenchFrame queue[QUEUE_SIZE];
uint8_t head = 0, queued = 0;
bool canReady = false, ackMode = false;
bool rawButton = HIGH, stableButton = HIGH;
uint32_t buttonChanged = 0, presses = 0;
uint32_t rx = 0, dropped = 0, overflows = 0, readErrors = 0, lastStatus = 0;
// -1 follows the debounced button; 0/1 force indicator off/on.
int8_t ledMode = -1;
bool ledOn = false;

void updateBenchLed() {
  ledOn = ledMode < 0 ? stableButton == LOW : ledMode == 1;
  digitalWrite(LED, ledOn ? LOW : HIGH);
}
void pollBenchButton() {
  const bool value = digitalRead(BUTTON);
  if (value != rawButton) { rawButton = value; buttonChanged = millis(); }
  if (value != stableButton && millis() - buttonChanged >= 40) {
    stableButton = value;
    if (stableButton == LOW) ++presses;
    Serial.print(F("# BOTAO "));
    Serial.print(stableButton == LOW ? F("PRESSIONADO") : F("SOLTO"));
    Serial.print(F(" toques=")); Serial.println(presses);
  }
  updateBenchLed();
}
void setup() {
  // Preload HIGH before output mode: avoid an active-low LED startup pulse.
  digitalWrite(LED, HIGH); pinMode(LED, OUTPUT);
  pinMode(BUTTON, INPUT_PULLUP);
  rawButton = stableButton = digitalRead(BUTTON);
  digitalWrite(9, HIGH); pinMode(9, OUTPUT);
  digitalWrite(10, HIGH); pinMode(10, OUTPUT);
  SPI.begin(); Serial.begin(500000);
  Serial.println(F("BANCADA MONTAGEM C4 v1: D2=botao GND, D4=PNP 5V ativo LOW."));
  Serial.println(F("# CAN CS9 125k, cristal 8MHz, SPI 1MHz. Inicio em escuta."));
  Serial.println(F("# EEPROM preservada. Nenhum comando CAN e transmitido."));
  canReady = benchCan.reset() == MCP2515::ERROR_OK &&
    benchCan.setBitrate(CAN_125KBPS, MCP_8MHZ) == MCP2515::ERROR_OK &&
    benchCan.setListenOnlyMode() == MCP2515::ERROR_OK;
  if (!canReady) Serial.println(F("# FALHA CAN; teste de botao/indicador continua."));
  Serial.println(F("# B=indicador segue botao; 1=acende; 0=apaga."));
  Serial.println(F("# A=ACK BANCADA ISOLADA; E=escuta. Sem envio de quadros."));
  Serial.println(F("# ms,id,ext,rtr,dlc,dados_hex; sem atribuicao de emissor"));
  updateBenchLed();
}
void loop() {
  pollBenchButton();
  if (canReady) {
    for (uint8_t n = 0; n < 8; ++n) {
      can_frame frame = {};
      const MCP2515::ERROR result = benchCan.readMessage(&frame);
      if (result == MCP2515::ERROR_NOMSG) break;
      if (result != MCP2515::ERROR_OK) {
        ++readErrors; benchCan.clearInterrupts(); break;
      }
      ++rx;
      if (queued == QUEUE_SIZE) { ++dropped; continue; }
      const uint8_t tail = (head + queued) % QUEUE_SIZE;
      queue[tail].ms = millis(); queue[tail].frame = frame; ++queued;
    }
    if (benchCan.getErrorFlags() & (MCP2515::EFLG_RX0OVR | MCP2515::EFLG_RX1OVR)) {
      ++overflows; benchCan.clearRXnOVRFlags();
    }
  }
  if (Serial.available()) {
    const char c = Serial.read();
    if (c == '0' || c == '1') {
      ledMode = c - '0'; updateBenchLed();
      Serial.println(ledOn ? F("# INDICADOR FORCADO LIGADO") : F("# INDICADOR FORCADO DESLIGADO"));
    } else if (c == 'B' || c == 'b') {
      ledMode = -1; updateBenchLed(); Serial.println(F("# INDICADOR SEGUE BOTAO"));
    } else if (c == 'A' || c == 'a' || c == 'E' || c == 'e') {
      if (!canReady) Serial.println(F("# CAN indisponivel; conferir SPI/alimentacao."));
      else {
        const bool wantAck = c == 'A' || c == 'a';
        const MCP2515::ERROR result = wantAck ? benchCan.setNormalMode() : benchCan.setListenOnlyMode();
        if (result != MCP2515::ERROR_OK) {
          canReady = false; Serial.println(F("# FALHA ao mudar modo CAN."));
        } else {
          ackMode = wantAck;
          Serial.println(ackMode ? F("# ACK ATIVO: somente bancada isolada, sem comandos CAN.") :
                                   F("# ESCUTA PASSIVA: sem ACK e sem comandos CAN."));
        }
      }
    }
  }
  if (queued) {
    const BenchFrame &item = queue[head];
    const can_frame &f = item.frame;
    const bool ext = (f.can_id & CAN_EFF_FLAG) != 0;
    const bool rtr = (f.can_id & CAN_RTR_FLAG) != 0;
    char text[60];
    int size = snprintf(text, sizeof(text), "%lu,%lX,%u,%u,%u,",
      (unsigned long)item.ms, (unsigned long)(f.can_id & (ext ? CAN_EFF_MASK : CAN_SFF_MASK)),
      (unsigned int)ext, (unsigned int)rtr, (unsigned int)f.can_dlc);
    if (!rtr)
      for (uint8_t i = 0; i < f.can_dlc; ++i)
        size += snprintf(text + size, sizeof(text) - size, "%02X", f.data[i]);
    text[size++] = '\n';
    if (Serial.availableForWrite() >= size) {
      Serial.write((const uint8_t*)text, size);
      head = (head + 1) % QUEUE_SIZE; --queued;
    }
  }
  if (millis() - lastStatus >= 1000) {
    // Compact status fits the Uno serial TX buffer; the CAN logger never waits.
    char text[64];
    const int size = snprintf(text, sizeof(text), "# rx=%lu ov=%lu sd=%lu re=%lu B=%u L=%u A=%u ef=%02X\n",
      (unsigned long)rx, (unsigned long)overflows, (unsigned long)dropped, (unsigned long)readErrors,
      (unsigned int)(stableButton == LOW), (unsigned int)ledOn, (unsigned int)ackMode,
      canReady ? (unsigned int)benchCan.getErrorFlags() : 255U);
    if (size > 0 && size < (int)sizeof(text) && Serial.availableForWrite() >= size) {
      Serial.write((const uint8_t*)text, size); lastStatus = millis();
    }
  }
}
