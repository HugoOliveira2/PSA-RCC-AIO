#pragma once
#include <stdint.h>
#include <string.h>
#define OUTPUT 1
#define HIGH 1
#define LOW 0
inline unsigned long nowMs=0;
inline unsigned long millis() { return nowMs++; }
inline void delay(unsigned long ms) { nowMs+=ms; }
inline void pinMode(uint8_t,int) {}
inline void digitalWrite(uint8_t,int) {}
