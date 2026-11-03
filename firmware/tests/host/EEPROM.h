#pragma once
#include <string.h>
struct EEPROMClass {
  uint8_t bytes[1024];
  EEPROMClass() { memset(bytes,255,sizeof bytes); }
  int length() { return sizeof bytes; }
  uint8_t read(int address) { return bytes[address]; }
  void update(int address,uint8_t value) { bytes[address]=value; }
} inline EEPROM;
