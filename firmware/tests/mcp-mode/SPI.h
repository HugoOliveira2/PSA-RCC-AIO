#pragma once
#include "Arduino.h"
#define MSBFIRST 1
#define SPI_MODE0 0
class SPISettings { public: SPISettings(uint32_t,int,int) {} };
class SPIClass {
  uint8_t op=0,address=0,mask=0,phase=0;
  void mirror() { if(address==0x0F) regs[0x0E]=regs[0x0F]&0xE0; }
public:
  uint8_t regs[256]={};
  bool acceptMode=true;
  void begin() {}
  void beginTransaction(SPISettings) { phase=0; }
  void endTransaction() {}
  uint8_t transfer(uint8_t value) {
    if(phase++==0){op=value;if(op==0xC0){memset(regs,0,sizeof regs);regs[0x0E]=regs[0x0F]=0x80;}return 0;}
    if(op==0x03){if(phase==2){address=value;return 0;}return regs[address++];}
    if(op==0x02){if(phase==2){address=value;return 0;}regs[address]=value;if(acceptMode)mirror();address++;return 0;}
    if(op==0x05){
      if(phase==2)address=value;
      else if(phase==3)mask=value;
      else {regs[address]=(regs[address]&~mask)|(value&mask);if(acceptMode)mirror();}
    }
    return 0;
  }
} inline SPI;
