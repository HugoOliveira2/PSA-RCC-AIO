#include <assert.h>
#include <iostream>
#include "../../.build/teste_montagem_c4/sketch/teste_montagem_c4.ino.cpp"
void tickBench(int milliseconds=1) { clockUs+=milliseconds*1000;loop(); }
void benchCommand(char c) { Serial.input.push_back(c);tickBench(); }
int main() {
  setup();assert(canReady && !ackMode && gpio[4]==HIGH);
  gpio[2]=LOW;tickBench();tickBench(20);
  gpio[2]=HIGH;tickBench();gpio[2]=LOW;tickBench();tickBench(39);
  assert(presses==0 && gpio[4]==HIGH);
  tickBench();assert(presses==1 && gpio[4]==LOW);
  for(int i=0;i<100;i++)tickBench();assert(presses==1);
  gpio[2]=HIGH;tickBench();tickBench(40);assert(gpio[4]==HIGH);
  benchCommand('1');assert(gpio[4]==LOW);
  benchCommand('0');assert(gpio[4]==HIGH);
  benchCommand('B');assert(gpio[4]==HIGH);
  benchCommand('A');assert(ackMode && !benchCan.config);
  can_frame f={};f.can_id=0x350;f.can_dlc=8;f.data[0]=1;
  benchCan.input.push_back(f);tickBench();assert(rx==1);
  assert(Serial.output.find(",350,0,0,8,0100000000000000\n")!=std::string::npos);
  for(int i=0;i<200;i++)benchCan.input.push_back(f);
  for(int i=0;i<50;i++)tickBench();assert(dropped>0);
  benchCan.flags=MCP2515::EFLG_RX0OVR;tickBench();assert(overflows==1);
  benchCommand('E');assert(!ackMode && benchCan.config);
  assert(benchCan.sent.empty());
  // A failed/missing CAN module must not disable button/indicator testing.
  canReady=false;gpio[2]=LOW;tickBench();tickBench(40);
  assert(presses==2 && gpio[4]==LOW);
  std::cout<<"PASS: debounce, active-low LED, forced LED modes, CAN logging, bounded queue, overflow reporting, ACK/listen switch, no application-frame TX, GPIO independent of CAN\n";
}
