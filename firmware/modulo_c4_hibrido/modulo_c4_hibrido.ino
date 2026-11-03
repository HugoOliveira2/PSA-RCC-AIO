// AIO Assist: serial 115200; CAN clima CS9/125k; CAN diagnostico CS10/500k.
// Logs antigos e comandos de uma letra do clima nao entram na serial do AIO.
#include "diagnostico.h"
struct SilentClimateConsole {
  template<typename... T> void print(T...) {}
  template<typename... T> void println(T...) {}
  void begin(unsigned long) {}
  int available() { return 0; }
  int read() { return -1; }
} climateConsole;
// MODULO C4 HIBRIDO v3.2 - corrige ordem dos bytes e sinal no seed/key PSA.
// D2: contato seco para GND, com condicionamento/pull-up.
// D4: BC327 PNP, emissor em 5 V, coletor no pino13; LOW acende.
// Base via 2k2 para D4, com 22k entre base/emissor (apagado no reset).
// Dois MCP2515 8 MHz, SPI 1 MHz. Clima CS9/125k; diagnostico CS10/500k.
// Reutiliza a base EEPROM validada da RT6. H inicia autonomamente com a
// Cactus e ignicao ligadas. Um MCP nao bloqueia o original: manter validacao
// da montagem/estabilidade. Serial reservada ao AIO Assist e !INFO/!STATUS.
#include <SPI.h>
#include <EEPROM.h>
#include "src/mcp2515_local/mcp2515.h"

const uint8_t CS_PIN = 9;
// Codigos do campo de consigna: LO=0, HI=0x16. A escala intermediaria
// e uma tabela PSA; um incremento de codigo nem sempre representa 0,5 C.
const uint8_t TEMP_RAW_LO = 0x00, TEMP_RAW_HI = 0x16;
constexpr uint8_t stepTemperatureRaw(uint8_t raw, bool increase);
constexpr uint8_t stepTemperatureByte(uint8_t value, bool increase);
constexpr uint8_t replacePassengerRaw(uint8_t source, uint8_t desired);
constexpr bool sourceIsAcMax(uint8_t driverByte);
constexpr bool ignitionAllowsControl(uint8_t state);
constexpr uint8_t stepTemperatureRaw(uint8_t raw, bool increase) {
  return raw > TEMP_RAW_HI ? raw :
    (increase ? (raw < TEMP_RAW_HI ? raw + 1 : raw) :
                (raw > TEMP_RAW_LO ? raw - 1 : raw));
}
constexpr uint8_t stepTemperatureByte(uint8_t value, bool increase) {
  return (value & 0xE0) | stepTemperatureRaw(value & 0x1F, increase);
}
constexpr uint8_t replacePassengerRaw(uint8_t source, uint8_t desired) {
  return (source & 0xE0) | (desired & 0x1F);
}
constexpr bool sourceIsAcMax(uint8_t driverByte) {
  // Mapa 350: byte4 bit7=AC_MAX_STATUS. Exige tambem consigna esquerda LO.
  return (driverByte & 0x80) != 0 && (driverByte & 0x1F) == TEMP_RAW_LO;
}
constexpr bool ignitionAllowsControl(uint8_t state) { return state == 1; }
// Verifica extremos, passagem pelo antigo limite e preservacao dos flags.
static_assert(stepTemperatureRaw(0, false) == 0, "LO nao pode sofrer underflow");
static_assert(stepTemperatureRaw(0, true) == 1, "precisa sair de LO");
static_assert(stepTemperatureRaw(1, false) == 0, "precisa atingir LO");
static_assert(stepTemperatureRaw(14, true) == 15, "remover limite antigo");
static_assert(stepTemperatureRaw(21, true) == 22, "precisa atingir HI");
static_assert(stepTemperatureRaw(22, true) == 22, "saturar em HI");
static_assert(stepTemperatureRaw(22, false) == 21, "precisa sair de HI");
static_assert(stepTemperatureRaw(31, true) == 31, "nao reinterpretar reservado");
static_assert(stepTemperatureByte(0xE0, true) == 0xE1, "preservar flags em LO");
static_assert(stepTemperatureByte(0xF6, false) == 0xF5, "preservar flags em HI");
static_assert(replacePassengerRaw(0xEB, 0) == 0xE0, "LO preserva flags Cactus");
static_assert(replacePassengerRaw(0xCB, 22) == 0xD6, "HI preserva flags Cactus");
static_assert(!sourceIsAcMax(0x00), "LO manual nao e AC MAX");
static_assert(sourceIsAcMax(0x80), "MAX com LO precisa ativar ambos lados");
static_assert(sourceIsAcMax(0xC0), "MONO nao esconde flag MAX");
static_assert(!sourceIsAcMax(0x87), "nao forcar LO com consigna esquerda diferente");
static_assert(!ignitionAllowsControl(0), "nao transmitir com ignicao desligada");
static_assert(ignitionAllowsControl(1), "habilitar com ignicao ligada");
static_assert(!ignitionAllowsControl(2), "aguardar fim da partida");
static_assert(!ignitionAllowsControl(3), "estado reservado nao habilita");
const uint8_t BUTTON_PIN = 2, INDICATOR_PIN = 4;
const uint32_t BUTTON_DEBOUNCE_MS = 40;
bool buttonRaw = HIGH, buttonStable = HIGH, buttonReleasedSeen = false;
uint32_t buttonChangedAt = 0;
bool powerSeen = false, installedPaused = false, saveOnShutdown = false;
bool ignitionSessionSeen = false;
uint8_t mainPowerState = 0;
uint32_t lastPowerFrame = 0, lastAutomaticAttempt = 0;
MCP2515 bus(CS_PIN, 1000000);
can_frame current350 = {}, saved350 = {}, test350 = {}, virtual350 = {};
can_frame cactus350 = {}, lastLoaded350 = {};
bool have350 = false, saved = false, active = false, pending = false, fault = false;
bool automatic = false, stateDirty = false;
bool hybrid = false, haveCactus350 = false;
bool acMaxActive = false;
uint8_t passengerBeforeMax = TEMP_RAW_LO, lastSourceDriverByte = 0xFF;
uint8_t previousTemperatureBits = 0;
uint32_t last350 = 0, lastRx = 0, countRx = 0, lastStatus = 0;
uint32_t lastCarRx = 0;
uint32_t lastCactus350 = 0, cactusFrames = 0, ignoredReflections = 0;
uint32_t passiveOverflows = 0, lastLoss = 0;
uint32_t started = 0, lastSent = 0, txStarted = 0;
uint32_t sends = 0, completed = 0, arbitrationEvents = 0;
// Captura RT6 validou pulso 350 byte0/bit5 de ~400ms e indicador
// 227 byte1/bits3..2 (0=apagado, 1=aceso). D2 solicita, D4 segue indicador.
const uint32_t DEFOG_PULSE_MS = 400;
bool defogQueued = false, defogPulse = false, defogAwaiting = false;
uint8_t defogUnderlyingBit = 0, defogLed = 255, defogExpectedLed = 255;
uint32_t last227 = 0, defogStart = 0, lastDefogRequest = 0;
int8_t nvSlot = -1;
uint16_t nvSequence = 0;
// Dois registros nos ultimos 40 bytes. CRC e confirmacao gravada por ultimo.
// Somente S escreve; nenhum envio CAN acontece automaticamente ao carregar.
const uint8_t NV_SLOT_BYTES = 20, NV_RECORD_BYTES = 17;

uint16_t nvCrc(const uint8_t *data, uint8_t size) {
  uint16_t crc = 0xFFFF;
  for (uint8_t i = 0; i < size; ++i) {
    crc ^= (uint16_t)data[i] << 8;
    for (uint8_t bit = 0; bit < 8; ++bit) {
      crc = (crc & 0x8000) ? (crc << 1) ^ 0x1021 : crc << 1;
    }
  }
  return crc;
}

int nvAddress(uint8_t slot) {
  return EEPROM.length() - 2 * NV_SLOT_BYTES + slot * NV_SLOT_BYTES;
}

bool readNv(uint8_t slot, uint8_t *record) {
  const int address = nvAddress(slot);
  for (uint8_t i = 0; i < NV_RECORD_BYTES; ++i) record[i] = EEPROM.read(address + i);
  if (record[0] != 'C' || record[1] != 'L' || record[2] != 'M' ||
      record[3] != 1 || record[6] != 7 || record[16] != 0xA5) return false;
  const uint16_t expected = (uint16_t)record[14] | ((uint16_t)record[15] << 8);
  return expected == nvCrc(record, 14);
}

void loadNv() {
  uint8_t a[NV_RECORD_BYTES], b[NV_RECORD_BYTES];
  const bool validA = readNv(0, a), validB = readNv(1, b);
  if (!validA && !validB) return;
  uint8_t *chosen = a;
  nvSlot = 0;
  if (validB && (!validA || (int16_t)(((uint16_t)b[4] | ((uint16_t)b[5] << 8)) -
                                    ((uint16_t)a[4] | ((uint16_t)a[5] << 8))) > 0)) {
    chosen = b; nvSlot = 1;
  }
  nvSequence = (uint16_t)chosen[4] | ((uint16_t)chosen[5] << 8);
  saved350.can_id = 0x350;
  saved350.can_dlc = 7;
  for (uint8_t i = 0; i < 7; ++i) saved350.data[i] = chosen[7 + i];
  saved = true;
  virtual350 = saved350;
}

bool saveNv(const can_frame &candidate) {
  const uint8_t slot = nvSlot == 0 ? 1 : 0;
  const uint16_t sequence = nvSequence + 1;
  uint8_t record[NV_RECORD_BYTES] = {'C', 'L', 'M', 1,
      (uint8_t)sequence, (uint8_t)(sequence >> 8), 7};
  for (uint8_t i = 0; i < 7; ++i) record[7 + i] = candidate.data[i];
  const uint16_t crc = nvCrc(record, 14);
  record[14] = (uint8_t)crc; record[15] = (uint8_t)(crc >> 8);
  record[16] = 0xA5;
  const int address = nvAddress(slot);
  EEPROM.update(address + 16, 0); // invalida destino, preserva registro anterior
  for (uint8_t i = 0; i < 16; ++i) EEPROM.update(address + i, record[i]);
  EEPROM.update(address + 16, 0xA5); // confirma somente depois do conteudo/CRC
  uint8_t verify[NV_RECORD_BYTES];
  if (!readNv(slot, verify)) return false;
  for (uint8_t i = 0; i < NV_RECORD_BYTES; ++i) if (record[i] != verify[i]) return false;
  nvSlot = slot;
  nvSequence = sequence;
  saved350 = candidate;
  virtual350 = saved350;
  saved = true;
  return true;
}

// Apenas os campos de temperatura aprendidos nas capturas. Nenhum comando
// de ventilador/modo/recirculacao e sintetizado por esta versao.
bool changeSetpoint(uint8_t index, bool increase) {
  if (hybrid && acMaxActive && index == 4) return false;
  const uint8_t oldRaw = test350.data[index] & 0x1F;
  if (oldRaw > TEMP_RAW_HI) return false;
  const uint8_t next = stepTemperatureByte(test350.data[index], increase);
  if (next == test350.data[index]) return false;
  test350.data[index] = next;
  virtual350 = test350;
  stateDirty = true;
  return true;
}

void prepareCactusCorrection() {
  uint8_t desired = virtual350.data[4] & 0x1F;
  const bool maxNow = sourceIsAcMax(cactus350.data[3]);
  if (maxNow != acMaxActive) {
    if (maxNow) {
      passengerBeforeMax = desired;
      climateConsole.println(F("# AC_MAX=1: passageiro tambem LO."));
    } else {
      desired = passengerBeforeMax;
      climateConsole.println(F("# AC_MAX=0: restaurou consigna anterior do passageiro."));
    }
    acMaxActive = maxNow;
  }
  if (maxNow) desired = TEMP_RAW_LO;
  test350 = cactus350; // preserva DLC, motorista, ventilacao, modos e flags
  test350.data[4] = replacePassengerRaw(cactus350.data[4], desired);
  defogUnderlyingBit = cactus350.data[0] & 0x20;
  if (defogPulse) test350.data[0] |= 0x20;
  virtual350 = test350;
  stateDirty = true;
}

void observeCactusDriver(const can_frame &frame) {
  if (frame.data[3] == lastSourceDriverByte) return;
  lastSourceDriverByte = frame.data[3];
  printFrame(F("# CACTUS350 "), frame);
  climateConsole.print(F("# MAX_bit=")); climateConsole.print((frame.data[3] & 0x80) ? 1 : 0);
  climateConsole.print(F(" motorista_raw=")); climateConsole.println(frame.data[3] & 0x1F);
}

bool vehicleReady() {
  return powerSeen && millis() - lastPowerFrame <= 2000 && ignitionAllowsControl(mainPowerState);
}

void updateIndicator() {
  bool on = false;
  if (!fault && vehicleReady() && last227 && millis() - last227 <= 2000) {
    on = defogLed == 1 || (defogLed == 2 && ((millis() / 500) & 1));
  }
  digitalWrite(INDICATOR_PIN, on ? LOW : HIGH);
}

void pollButton() {
  const bool raw = digitalRead(BUTTON_PIN);
  if (raw != buttonRaw) { buttonRaw = raw; buttonChangedAt = millis(); }
  if (millis() - buttonChangedAt < BUTTON_DEBOUNCE_MS || buttonStable == buttonRaw) return;
  buttonStable = buttonRaw;
  if (buttonStable == HIGH) buttonReleasedSeen = true;
  else if (buttonReleasedSeen) {
    buttonReleasedSeen = false;
    requestDefog();
  }
}

void showDefogState() {
  climateConsole.print(F("# IND227="));
  if (last227 == 0 || millis() - last227 > 2000) climateConsole.print(F("sem informacao recente"));
  else if (defogLed == 0) climateConsole.print(F("apagado"));
  else if (defogLed == 1) climateConsole.print(F("aceso"));
  else if (defogLed == 2) climateConsole.print(F("intermitente (nao validado)"));
  else climateConsole.print(F("invalido"));
  climateConsole.print(F(" raw=")); climateConsole.println(defogLed);
}

void startDefogPulse() {
  defogQueued = false;
  if (defogUnderlyingBit) {
    climateConsole.println(F("# D recusado: pedido da fachada ja esta pressionado.")); return;
  }
  defogPulse = true;
  defogStart = millis();
  defogExpectedLed = defogLed == 0 ? 1 : 0;
  defogAwaiting = true;
  test350.data[0] |= 0x20;
  virtual350 = test350;
  stateDirty = true;
  climateConsole.println(F("# D pressionado: bit20 por 400ms; aguarda IND227."));
}

void releaseDefogPulse() {
  if (!defogPulse) return;
  defogPulse = false;
  test350.data[0] = (test350.data[0] & 0xDF) | defogUnderlyingBit;
  virtual350 = test350;
  stateDirty = true;
  climateConsole.println(F("# D solto: pedido voltou ao repouso."));
}

void requestDefog() {
  if (!active || !automatic || fault) {
    climateConsole.println(F("# D recusado: ative H (Cactus) ou A (sem fachada).")); return;
  }
  if (defogPulse || defogQueued || defogAwaiting || millis() - lastDefogRequest < 1000) {
    climateConsole.println(F("# D recusado: aguarde pulso/confirmacao anterior.")); return;
  }
  if (last227 == 0 || millis() - last227 > 2000 || defogLed > 1) {
    climateConsole.println(F("# D recusado: indicador 227 desconhecido/invalido; envie V.")); return;
  }
  lastDefogRequest = millis();
  if (hybrid) {
    // Comecar apos o proximo quadro da Cactus reduz a chance de um quadro
    // original em repouso cortar o pulso de 400ms no segmento compartilhado.
    defogQueued = true;
    climateConsole.println(F("# D agendado para o proximo 350 da Cactus."));
  } else startDefogPulse();
}

bool isRecentReflection(const can_frame &frame) {
  if (!sends || millis() - lastSent >= 100 || frame.can_id != lastLoaded350.can_id ||
      frame.can_dlc != lastLoaded350.can_dlc) return false;
  for (uint8_t i = 0; i < frame.can_dlc; ++i) {
    if (frame.data[i] != lastLoaded350.data[i]) return false;
  }
  return true;
}

void handleTemperatureRequest(uint8_t bits) {
  const uint8_t rising = bits & ~previousTemperatureBits;
  previousTemperatureBits = bits;
  if (!active || !automatic || !rising) return;
  bool changed = false;
  // Ignora comandos contraditorios no mesmo lado. Repeticao do mesmo
  // pulso mantido nao conta como varios cliques: exige retorno a zero.
  if (!hybrid && (bits & 0xC0) != 0xC0) {
    if (rising & 0x80) changed |= changeSetpoint(3, true);
    if (rising & 0x40) changed |= changeSetpoint(3, false);
  }
  if ((bits & 0x30) != 0x30) {
    if (rising & 0x20) changed |= changeSetpoint(4, true);
    if (rising & 0x10) changed |= changeSetpoint(4, false);
  }
  climateConsole.print(F("# AIO364=")); climateConsole.print(bits, HEX);
  climateConsole.print(F(" mudou=")); climateConsole.print(changed);
  climateConsole.print(F(" motorista_raw=")); climateConsole.print(test350.data[3] & 0x1F);
  climateConsole.print(F(" passageiro_raw=")); climateConsole.println(test350.data[4] & 0x1F);
}

uint8_t readTxControl() {
  SPI.beginTransaction(SPISettings(1000000, MSBFIRST, SPI_MODE0));
  digitalWrite(CS_PIN, LOW);
  SPI.transfer(0x03); SPI.transfer(0x30);
  const uint8_t value = SPI.transfer(0);
  digitalWrite(CS_PIN, HIGH);
  SPI.endTransaction();
  return value;
}

void printFrame(const __FlashStringHelper *label, const can_frame &frame) {
  climateConsole.print(label);
  for (uint8_t i = 0; i < frame.can_dlc; ++i) {
    if (frame.data[i] < 16) climateConsole.print('0');
    climateConsole.print(frame.data[i], HEX); climateConsole.print(' ');
  }
  climateConsole.println();
}

void endTest(const __FlashStringHelper *why, bool failed) {
  const uint8_t ef = bus.getErrorFlags();
  const uint8_t tec = bus.errorCountTX(), rec = bus.errorCountRX();
  // Desabilita transmissao inclusive se houver quadro ainda pendente.
  const bool configOK = bus.setConfigMode() == MCP2515::ERROR_OK;
  const bool listenOK = configOK && bus.setListenOnlyMode() == MCP2515::ERROR_OK;
  if (defogPulse) {
    test350.data[0] = (test350.data[0] & 0xDF) | defogUnderlyingBit;
    virtual350 = test350;
  }
  if (acMaxActive) {
    virtual350.data[4] = replacePassengerRaw(virtual350.data[4], passengerBeforeMax);
    acMaxActive = false;
  }
  defogQueued = defogPulse = defogAwaiting = false;
  active = pending = automatic = hybrid = false;
  fault = failed || !listenOK;
  digitalWrite(INDICATOR_PIN, HIGH);
  climateConsole.print(F("# FIM ")); climateConsole.println(why);
  climateConsole.print(F("# carregados=")); climateConsole.print(sends);
  climateConsole.print(F(" concluidos=")); climateConsole.print(completed);
  climateConsole.print(F(" EFLG=")); climateConsole.print(ef, HEX);
  climateConsole.print(F(" TEC=")); climateConsole.print(tec);
  climateConsole.print(F(" REC=")); climateConsole.print(rec);
  climateConsole.print(F(" arbitragens=")); climateConsole.print(arbitrationEvents);
  climateConsole.print(F(" reflexos=")); climateConsole.print(ignoredReflections);
  climateConsole.print(F(" escuta=")); climateConsole.println(listenOK);
}

void beginTest(char command) {
  const bool wantHybrid = command == 'H' || command == 'h';
  if (fault || active || !saved) {
    climateConsole.println(F("# RECUSADO: precisa de base S, sem falha e sem teste ativo."));
    return;
  }
  if (!vehicleReady()) {
    climateConsole.println(F("# AGUARDANDO ignicao ligada confirmada no F6.")); return;
  }
  // Revalidar uma captura longa recente sem qualquer 350 evita conflito com
  // uma fachada ainda emitindo. Um 350 refletido por gateway tambem aborta.
  if ((!wantHybrid && millis() - last350 < 3000) || millis() - lastLoss < 3000 ||
      countRx < 100 || millis() - lastRx > 500 || millis() - lastCarRx > 1000) {
    climateConsole.println(F("# RECUSADO: requer trafego vivo e 3s sem nenhum 350."));
    return;
  }
  if (wantHybrid) {
    if (!haveCactus350 || millis() - lastCactus350 > 1000) {
      climateConsole.println(F("# RECUSADO: H requer 350/DLC8 recente da Cactus.")); return;
    }
    if ((virtual350.data[4] & 0x1F) > TEMP_RAW_HI) {
      climateConsole.println(F("# RECUSADO: base direita fora da faixa LO-HI.")); return;
    }
    hybrid = true;
    prepareCactusCorrection();
  } else {
    hybrid = false;
    test350 = (command == 'I' || command == 'i') ? saved350 : virtual350;
    defogUnderlyingBit = test350.data[0] & 0x20;
  }
  if (command == '+' || command == '-') {
    if (!changeSetpoint(4, command == '+')) {
      climateConsole.println(F("# Sem mudanca: extremo LO/HI ou codigo reservado.")); return;
    }
  }
  virtual350 = test350;
  if (bus.setNormalMode() != MCP2515::ERROR_OK) {
    endTest(F("falha modo normal"), true); return;
  }
  bus.clearTXInterrupts();
  sends = completed = 0;
  active = true;
  automatic = wantHybrid || command == 'A' || command == 'a';
  stateDirty = true;
  pending = false;
  started = millis();
  lastSent = started - 500;
  // Mensagem curta para nao bloquear leitura com o buffer serial cheio.
  printFrame(hybrid ? F("# H CACTUS autonomo ") :
             (automatic ? F("# AUTO AIO 120s ") : F("# TESTE 3s ")), test350);
}

void persistPassengerOnShutdown() {
  saveOnShutdown = false;
  if (fault || !saved) return;
  const uint8_t desired = virtual350.data[4] & 0x1F;
  if (desired > TEMP_RAW_HI || desired == (saved350.data[4] & 0x1F)) return;
  can_frame candidate = saved350;
  candidate.data[4] = replacePassengerRaw(candidate.data[4], desired);
  if (bus.setConfigMode() != MCP2515::ERROR_OK) {
    endTest(F("falha ao persistir ajuste"), true); return;
  }
  const bool stored = saveNv(candidate);
  bus.clearInterrupts(); bus.clearRXnOVRFlags();
  lastLoss = millis(); have350 = false;
  if (bus.setListenOnlyMode() != MCP2515::ERROR_OK || !stored) {
    endTest(F("falha EEPROM no desligamento"), true); return;
  }
  climateConsole.println(F("# Ajuste do passageiro salvo no desligamento."));
}

void installedLifecycle() {
  if (active && !vehicleReady()) {
    endTest(F("ignicao/estado F6: cessou transmissao"), false);
  }
  if (saveOnShutdown && !active) persistPassengerOnShutdown();
  if (fault || active || installedPaused || !saved || !vehicleReady()) return;
  if (!haveCactus350 || millis() - lastCactus350 > 1000 || countRx < 100 ||
      millis() - lastCarRx > 1000 || millis() - lastLoss < 3000) return;
  if (millis() - lastAutomaticAttempt >= 1000) {
    lastAutomaticAttempt = millis();
    beginTest('H');
  }
}

void climateSetup() {
  digitalWrite(INDICATOR_PIN, HIGH); pinMode(INDICATOR_PIN, OUTPUT);
  pinMode(BUTTON_PIN, INPUT_PULLUP);
  buttonRaw = buttonStable = digitalRead(BUTTON_PIN);
  // Botao segurado ao alimentar nao gera toque: exige soltar e apertar.
  buttonReleasedSeen = buttonStable == HIGH;
  pinMode(10, OUTPUT); digitalWrite(10, HIGH);
  pinMode(9, OUTPUT); digitalWrite(9, HIGH);
  SPI.begin();
  climateConsole.begin(500000);
  climateConsole.println(F("MODULO C4 HIBRIDO v3.0: inicio autonomo, D2=botao GND, D4=PNP 5V ativo LOW."));
  climateConsole.println(F("# Faixa de codigos: LO=0, HI=22 (0x16)."));
  loadNv();
  if (saved) printFrame(F("# BASE CARREGADA EEPROM "), saved350);
  else climateConsole.println(F("# EEPROM sem base valida; capturar RT6 e enviar S."));
  if (bus.reset() != MCP2515::ERROR_OK ||
      bus.setBitrate(CAN_125KBPS, MCP_8MHZ) != MCP2515::ERROR_OK) {
    fault = true; climateConsole.println(F("# FALHA inicializacao; nao transmitir."));
  }
  if (!fault) {
    // IDs exatos standard. B6 fornece referencia de trafego vivo do carro.
    // Receber 350 nos dois bancos tambem permite rollover entre buffers.
    const uint16_t ids[6] = {0x350, 0x0B6, 0x350, 0x364, 0x227, 0x0F6};
    bool ok = bus.setFilterMask(MCP2515::MASK0, false, 0x7FF) == MCP2515::ERROR_OK;
    ok = (bus.setFilterMask(MCP2515::MASK1, false, 0x7FF) == MCP2515::ERROR_OK) && ok;
    for (uint8_t i = 0; i < 6; ++i) {
      ok = (bus.setFilter((MCP2515::RXF)i, false, ids[i]) == MCP2515::ERROR_OK) && ok;
    }
    if (!ok || bus.setListenOnlyMode() != MCP2515::ERROR_OK) {
      fault = true; climateConsole.println(F("# FALHA filtros/modo; nao transmitir."));
    }
  }
  climateConsole.println(F("# F6 usado so para estado da ignicao; sem monitor de arrefecimento."));
  climateConsole.println(F("# D simula toque do desembacador; V mostra IND227."));
  climateConsole.println(F("# A ativa temperaturas pela AIO por 120s. I reproduz base por 3s."));
  climateConsole.println(F("# H inicia automaticamente com ignicao e Cactus; sem limite de 120s."));
  climateConsole.println(F("# +/- teste manual cumulativo do passageiro. X encerra."));
  climateConsole.println(F("# S grava EEPROM; L mostra base; X interrompe. Pode desligar apos salvar."));
}

void climateLoop() {
  if (fault) { digitalWrite(INDICATOR_PIN, HIGH); return; }
  // Leitura tem prioridade sobre comandos e temporizadores.
  for (uint8_t drained = 0; drained < 8; ++drained) {
    can_frame frame = {};
    const MCP2515::ERROR result = bus.readMessage(&frame);
    if (result == MCP2515::ERROR_NOMSG) break;
    if (result != MCP2515::ERROR_OK) {
      endTest(F("falha leitura"), true); return;
    }
    ++countRx;
    lastRx = millis();
    if (frame.can_id == 0x0B6) lastCarRx = lastRx;
    if (frame.can_id == 0x0F6 && frame.can_dlc == 8) {
      const uint8_t nextPower = (frame.data[0] >> 3) & 3;
      if (nextPower == 1) ignitionSessionSeen = true;
      if (nextPower == 0 && ignitionSessionSeen) {
        saveOnShutdown = true;
        ignitionSessionSeen = false;
      }
      mainPowerState = nextPower;
      lastPowerFrame = lastRx;
      powerSeen = true;
      if (nextPower == 0) installedPaused = false;
    }
    if (frame.can_id == 0x227 && frame.can_dlc >= 2) {
      const uint8_t led = (frame.data[1] >> 2) & 3;
      const bool changed = defogLed != led || last227 == 0;
      defogLed = led;
      last227 = lastRx;
      if (changed) showDefogState();
      if (defogAwaiting && led == defogExpectedLed) {
        defogAwaiting = false;
        climateConsole.println(F("# D CONFIRMADO pelo comando do indicador da BSI."));
      }
    }
    if (frame.can_id == 0x364 && frame.can_dlc == 6) {
      handleTemperatureRequest(frame.data[0] & 0xF0);
    }
    if (frame.can_id == 0x350) {
      last350 = millis();
      if (active && hybrid) {
        if (frame.can_dlc != 8) { endTest(F("H recebeu 350 fora do formato Cactus"), true); return; }
        if (isRecentReflection(frame)) { ++ignoredReflections; continue; }
        observeCactusDriver(frame);
        if (defogPulse) {
          // O original ja chegou ao carro: nao reaplicar a borda de pressao,
          // evitando transformar um toque em dois se a fonte antecipar envio.
          releaseDefogPulse();
          climateConsole.println(F("# D terminou antes: fonte Cactus reapareceu; sem repetir borda."));
        }
        cactus350 = frame;
        haveCactus350 = true;
        lastCactus350 = last350;
        ++cactusFrames;
        prepareCactusCorrection();
        if (defogQueued) startDefogPulse();
      } else {
        if (active) { endTest(F("outro emissor de 350 apareceu"), true); return; }
        if (frame.can_dlc == 8) {
          observeCactusDriver(frame);
          cactus350 = frame;
          haveCactus350 = true;
          lastCactus350 = last350;
          ++cactusFrames;
        }
      }
      have350 = frame.can_dlc == 7;
      if (have350) current350 = frame;
    }
  }
  const uint8_t flags = bus.getErrorFlags();
  if (flags & (MCP2515::EFLG_TXBO | MCP2515::EFLG_TXEP | MCP2515::EFLG_RXEP)) {
    endTest(F("erro CAN"), true); return;
  }
  if (flags & (MCP2515::EFLG_RX0OVR | MCP2515::EFLG_RX1OVR)) {
    if (active) { endTest(F("overflow durante teste ativo"), true); return; }
    ++passiveOverflows;
    lastLoss = millis();
    have350 = false; // aguarda nova base recente; base ja salva e preservada
    bus.clearRXnOVRFlags();
  }
  installedLifecycle();
  if (fault) { digitalWrite(INDICATOR_PIN, HIGH); return; }
  pollButton();
  updateIndicator();
  if (active && millis() - lastCarRx > 1500) {
    endTest(F("trafego B6 do carro ausente"), true); return;
  }
  if (active && hybrid && millis() - lastCactus350 > 1500) {
    endTest(F("350 da Cactus ausente; cessou correcao"), true); return;
  }
  if (defogPulse && millis() - defogStart >= DEFOG_PULSE_MS) releaseDefogPulse();
  if (defogAwaiting && millis() - defogStart >= 2000) {
    defogAwaiting = false;
    climateConsole.println(F("# D SEM CONFIRMACAO em 2s; nao repetiu pedido automaticamente."));
  }
  if (climateConsole.available()) {
    const char key = climateConsole.read();
    if (key == 'X' || key == 'x') {
      installedPaused = true;
      endTest(F("pausado ate H ou novo ciclo de ignicao"), false); return;
    }
    if (key == 'D' || key == 'd') requestDefog();
    if (key == 'V' || key == 'v') showDefogState();
    if (key == 'S' || key == 's') {
      if (!active && have350 && millis() - last350 < 1500) {
        const can_frame candidate = current350;
        // Escrita EEPROM e bloqueante: pausa recepcao em vez de provocar overflow.
        if (bus.setConfigMode() != MCP2515::ERROR_OK) {
          endTest(F("falha ao pausar para EEPROM"), true); return;
        }
        const bool stored = saveNv(candidate);
        bus.clearInterrupts();
        bus.clearRXnOVRFlags();
        have350 = false;
        lastLoss = millis(); // exige nova janela observada antes de permitir envio
        if (bus.setListenOnlyMode() != MCP2515::ERROR_OK) {
          endTest(F("falha ao voltar a escuta apos EEPROM"), true); return;
        }
        if (!stored) { endTest(F("EEPROM nao conferiu; base anterior preservada se valida"), true); return; }
        printFrame(F("# BASE SALVA EEPROM "), saved350);
      } else climateConsole.println(F("# Sem 350/DLC7 recente para salvar."));
    }
    if (key == 'L' || key == 'l') {
      if (saved) printFrame(F("# BASE EEPROM "), saved350);
      else climateConsole.println(F("# Sem base EEPROM valida."));
    }
    if (key == 'H' || key == 'h') { installedPaused = false; beginTest(key); }
    if (key == 'A' || key == 'a' || key == 'I' || key == 'i' || key == '+' || key == '-') beginTest(key);
  }
  if (active && pending) {
    if (bus.getInterrupts() & 0x04) {
      bus.clearTXInterrupts(); pending = false; ++completed;
    } else if (millis() - txStarted >= 100) {
      endTest(F("TX nao concluiu em 100ms"), true); return;
    }
  }
  if (active && !hybrid && millis() - started >= (automatic ? 120000UL : 3000UL)) {
    if (pending) return; // aguarda conclusao/timeout do ultimo quadro
    endTest(F("fim da janela; voltou a escuta"), false); return;
  }
  const bool due = hybrid ? (stateDirty && millis() - lastSent >= 10) :
      (millis() - lastSent >= 500 || (stateDirty && millis() - lastSent >= 50));
  if (active && !pending && due) {
    lastLoaded350 = test350;
    const MCP2515::ERROR result = bus.sendMessage(MCP2515::TXB0, &test350);
    if (result != MCP2515::ERROR_OK) {
      const uint8_t ctrl = readTxControl();
      const bool queuedOrDone = (ctrl & 0x08) || (bus.getInterrupts() & 0x04);
      // A biblioteca sinaliza FAILTX tambem por MLOA, perda de arbitragem.
      // Isso nao e erro de barramento: o MCP retransmite automaticamente.
      // Aceita somente MLOA com pedido pendente/concluido e sem TXERR/ABTF.
      if ((ctrl & 0x20) && !(ctrl & 0x50) && queuedOrDone) {
        ++arbitrationEvents;
      } else {
        climateConsole.print(F("# TX retorno=")); climateConsole.print((int)result);
        climateConsole.print(F(" CTRL=")); climateConsole.println(ctrl, HEX);
        endTest(F("erro ao carregar TX"), true); return;
      }
    }
    pending = true;
    txStarted = lastSent = millis();
    ++sends;
    stateDirty = false;
  }
  if (millis() - lastStatus >= 1000) {
    lastStatus = millis();
    climateConsole.print(F("# rx=")); climateConsole.print(countRx);
    climateConsole.print(F(" base=")); climateConsole.print(saved);
    climateConsole.print(F(" ov=")); climateConsole.print(passiveOverflows);
    climateConsole.print(F(" H=")); climateConsole.print(hybrid);
    climateConsole.print(F(" C8=")); climateConsole.print(cactusFrames);
    climateConsole.print(F(" ms_sem_350=")); climateConsole.println(millis() - last350);
  }
}

void setup() {
  climateSetup();
  diagnosticSetup();
}
void climateStatus() {
  Serial.print(F("CLIMA base=")); Serial.print(saved);
  Serial.print(F(" fault=")); Serial.print(fault);
  Serial.print(F(" H=")); Serial.print(hybrid);
  Serial.print(F(" rx=")); Serial.print(countRx);
  Serial.print(F(" ov=")); Serial.println(passiveOverflows);
}
void loop() {
  diagnosticPoll();
  climateLoop();
  diagnosticPoll();
}
