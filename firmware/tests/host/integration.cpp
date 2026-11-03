#include <assert.h>
#include <iostream>
#include "../../modulo_c4_hibrido/diagnostico.cpp"
// Arduino-builder generated prototypes are included with the actual sketch.
#include "../../.build/modulo_c4_hibrido/sketch/modulo_c4_hibrido.ino.cpp"
void car(uint32_t id,std::initializer_list<uint8_t> bytes) {
  can_frame f={};f.can_id=id;f.can_dlc=(uint8_t)bytes.size();
  int i=0;for(auto b:bytes)f.data[i++]=b;bus.input.push_back(f);
}
void cycle() {
  if(clockUs%20000==0)car(0xB6,{0,0,0,0,0,0,0,0});
  if(clockUs%500000==0){
    car(0xF6,{8,0,0,0,0,0,0,0});
    car(0x350,{1,0,0,6,8,0x30,9,0});
    car(0x227,{0,4});
  }
  loop();clockUs+=1000;
}
void cycles(int count) { for(int i=0;i<count;i++)cycle(); }
void hostLine(const std::string&s) {
  for(char c:s+"\n")Serial.input.push_back(c);cycles(100);
}
int main() {
  can_frame base={};base.can_id=0x350;base.can_dlc=7;
  const uint8_t bytes[]={1,0,0,6,8,0x30,9};memcpy(base.data,bytes,7);
  assert(saveNv(base));setup();cycles(5000);
  assert(saved && hybrid && active && !fault && gpio[4]==LOW);
  assert(Serial.output.empty()); // no climate log contaminates desktop protocol
  hostLine(">764:664");hostLine("21B1");
  can_frame f={};f.can_id=0x664;f.can_dlc=8;
  f.data[0]=0x10;f.data[1]=182;f.data[2]=0x61;f.data[3]=0xB1;
  diag.input.push_back(f);cycles(10);
  const size_t before=bus.sent.size();
  uint8_t seq=1;
  for(int p=6;p<182;p+=7){
    f={};f.can_id=0x664;f.can_dlc=std::min(7,182-p)+1;
    f.data[0]=0x20|seq;seq=(seq+1)&15;diag.input.push_back(f);
    cycles(25);
  }
  cycles(100);assert(!failed && !fault && hybrid && bus.sent.size()>before);
  const size_t received=countRx;diag.flags=MCP2515::EFLG_RX0OVR;cycles(1000);
  assert(failed && !fault && hybrid && countRx>received);
  // GPIO button and active-low indicator still operate after diagnostic failure.
  gpio[2]=LOW;cycles(50);assert(defogQueued || defogPulse || defogAwaiting);
  gpio[2]=HIGH;cycles(50);
  assert(gpio[4]==LOW);
  // Climate failure likewise does not prevent the USB diagnostic handshake.
  bus.flags=MCP2515::EFLG_TXBO;cycles(20);assert(fault && gpio[4]==HIGH);
  hostLine(">764:664");assert(connected && !failed);
  Serial.output.clear();hostLine("V");assert(Serial.output=="2.0\n");
  std::cout<<"PASS: simultaneous B1 receive and climate traffic, unchanged EEPROM, active-low LED, button, no serial log leakage, diagnostic/climate fault isolation\n";
}
