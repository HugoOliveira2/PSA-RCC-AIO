#pragma once
#include <stdint.h>
#include <string>
#include <deque>
#include <vector>
#include <sstream>
class __FlashStringHelper;
#define F(x) reinterpret_cast<const __FlashStringHelper*>(x)
#define HIGH 1
#define LOW 0
#define OUTPUT 1
#define INPUT_PULLUP 2
#define HEX 16
#define CAN_EFF_FLAG 0x80000000UL
#define CAN_RTR_FLAG 0x40000000UL
#define CAN_EFF_MASK 0x1FFFFFFFUL
#define CAN_SFF_MASK 0x7FFUL
inline int gpio[32] = {};
inline void pinMode(int p,int mode) { if(mode==INPUT_PULLUP) gpio[p]=HIGH; }
inline void digitalWrite(int p,int v) { gpio[p]=v; }
inline int digitalRead(int p) { return gpio[p]; }
#define _MCP2515_H_
inline uint32_t clockUs = 0;
inline uint32_t millis() { return clockUs / 1000; }
inline uint32_t micros() { return clockUs; }
struct SerialMock {
  std::deque<char> input;
  std::string output;
  void begin(unsigned long) {}
  int available() { return (int)input.size(); }
  int availableForWrite() { return 64; }
  int read() { const char c = input.front(); input.pop_front(); return c; }
  void write(char c) { output += c; }
  void write(const uint8_t* bytes, int count) { output.append((const char*)bytes,count); }
  void print(const __FlashStringHelper* p) { output += reinterpret_cast<const char*>(p); }
  template<class T> void print(T value) { std::ostringstream s; s << value; output += s.str(); }
  template<class T> void println(T value) { print(value); output += '\n'; }
} inline Serial;
struct can_frame { uint32_t can_id; uint8_t can_dlc; uint8_t data[8]; };
enum { CAN_500KBPS, CAN_125KBPS, MCP_8MHZ };
class MCP2515 {
public:
  enum ERROR { ERROR_OK, ERROR_NOMSG, ERROR_FAILTX };
  enum MASK { MASK0, MASK1 };
  enum RXF { RXF0, RXF1, RXF2, RXF3, RXF4, RXF5 };
  enum TXBn { TXB0 };
  enum { EFLG_TXBO=32, EFLG_TXEP=16, EFLG_RXEP=8, EFLG_RX0OVR=64, EFLG_RX1OVR=128 };
  std::deque<can_frame> input;
  std::vector<can_frame> sent;
  uint8_t flags=0, interrupts=0;
  bool offline=false, complete=true, config=true;
  MCP2515(uint8_t, uint32_t) {}
  ERROR reset() { input.clear(); flags=interrupts=0; config=true; return offline?ERROR_FAILTX:ERROR_OK; }
  ERROR setBitrate(int, int) { return ERROR_OK; }
  ERROR setFilterMask(MASK,bool,uint16_t) { return ERROR_OK; }
  ERROR setFilter(RXF,bool,uint16_t) { return ERROR_OK; }
  ERROR setListenOnlyMode() { config=true; return ERROR_OK; }
  ERROR setNormalOneShotMode() { config=false; return ERROR_OK; }
  ERROR setNormalMode() { config=false; return ERROR_OK; }
  ERROR setConfigMode() { config=true; return ERROR_OK; }
  void clearTXInterrupts() { interrupts=0; }
  uint8_t getInterrupts() { return interrupts; }
  uint8_t getErrorFlags() { return flags; }
  uint8_t getStatus() { return interrupts ? 8 : 0; }
  uint8_t errorCountRX() { return 0; }
  uint8_t errorCountTX() { return 0; }
  void clearInterrupts() { interrupts=0; }
  void clearRXnOVRFlags() { flags &= ~(EFLG_RX0OVR|EFLG_RX1OVR); }
  ERROR sendMessage(const can_frame* f) { return sendMessage(TXB0,f); }
  ERROR sendMessage(TXBn,const can_frame* f) {
    if (offline) return ERROR_FAILTX;
    sent.push_back(*f); if(complete) interrupts=4; return ERROR_OK;
  }
  ERROR readMessage(can_frame* f) {
    if(input.empty()) return ERROR_NOMSG;
    *f=input.front();input.pop_front();return ERROR_OK;
  }
};
