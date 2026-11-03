// Host tests execute the actual diagnostic implementation with simulated IO.
#include <assert.h>
#include <iostream>
#include "../../modulo_c4_hibrido/diagnostico.cpp"
void climateStatus() {}
void tick(uint32_t us=1000) { clockUs+=us; diagnosticPoll(); }
void drain() { for(int i=0;i<80;i++) tick(); }
void host(const std::string& s) { for(char c:s+"\n") Serial.input.push_back(c); drain(); }
void fresh() {
  diag.offline=false; diag.complete=true; diag.sent.clear();
  resetTransaction(); txPending=keepAlive=connected=failed=dropping=false;
  lineSize=0; clockUs=0; Serial.input.clear();Serial.output.clear();
  diagnosticSetup();host(">764:664");Serial.output.clear();
}
void rx(std::initializer_list<uint8_t> bytes) {
  can_frame f={}; f.can_id=0x664; f.can_dlc=(uint8_t)bytes.size();
  int i=0;for(uint8_t b:bytes)f.data[i++]=b;
  diag.input.push_back(f);
}
void longResponse(const std::vector<uint8_t>& bytes) {
  can_frame f={};f.can_id=0x664;f.can_dlc=8;
  f.data[0]=0x10|(bytes.size()>>8);f.data[1]=(uint8_t)bytes.size();
  memcpy(f.data+2,bytes.data(),6);diag.input.push_back(f);tick();tick();
  uint8_t seq=1;
  for(size_t p=6;p<bytes.size();p+=7){
    f={};f.can_id=0x664;size_t n=std::min((size_t)7,bytes.size()-p);
    f.can_dlc=(uint8_t)n+1;f.data[0]=0x20|seq;seq=(seq+1)&15;
    memcpy(f.data+1,bytes.data()+p,n);diag.input.push_back(f);tick(5000);
  }
  drain();
}
int main() {
  fresh();host("V");assert(Serial.output=="2.0\n");
  Serial.output.clear();host("1081");rx({2,0x50,0x81});drain();
  assert(Serial.output=="5081\n");
  fresh();host(":D91C:83:C0");rx({2,0x50,0xC0});tick();tick();
  assert(diag.sent.back().data[1]==0x27 && diag.sent.back().data[2]==0x83);
  rx({6,0x67,0x83,0x12,0x34,0x56,0x78});tick();tick();
  // Golden key from the independent signed-16-bit PSA reference.
  assert(seedKey(0xD91C,0x12345678)==0x57F61D6F);
  const uint8_t expectedKeyFrame[]={6,0x27,0x84,0x57,0xF6,0x1D,0x6F};
  assert(diag.sent.back().can_dlc==7);
  assert(!memcmp(diag.sent.back().data,expectedKeyFrame,sizeof expectedKeyFrame));
  rx({2,0x67,0x84});drain();assert(Serial.output=="6784\n" && keepAlive);
  fresh();host(":D91C:83:C0");rx({3,0x7F,0x27,0x37});drain();
  size_t sent=diag.sent.size();for(int i=0;i<30;i++)tick(100000);
  assert(diag.sent.size()==sent && !keepAlive && Serial.output=="7F2737\n");
  for(uint8_t code:{0x33,0x35,0x36,0x37}) {
    fresh();host(":D91C:83:C0");rx({2,0x50,0xC0});tick();tick();
    rx({6,0x67,0x83,0x12,0x34,0x56,0x78});tick();tick();
    rx({3,0x7F,0x27,code});drain();
    sent=diag.sent.size();for(int i=0;i<50;i++)tick(100000);
    assert(diag.sent.size()==sent && !keepAlive && unlockStage==IDLE);
  }
  fresh();host("21B1");std::vector<uint8_t> payload(182);
  payload[0]=0x61;payload[1]=0xB1;for(int i=2;i<182;i++)payload[i]=(uint8_t)i;
  longResponse(payload);
  std::string expected;const char *hex="0123456789ABCDEF";
  for(auto b:payload){expected+=hex[b>>4];expected+=hex[b&15];}
  assert(Serial.output==expected+"\n" && !failed);
  fresh();host("21B0");rx({3,0x7F,0x21,0x78});rx({2,0x61,0xB0});drain();
  assert(Serial.output=="7F2178\n61B0\n");
  fresh();std::string write="3BB1";for(int i=0;i<180;i++)write+="A5";
  host(write);assert(diag.sent.size()==1 && flowWaiting && diag.sent[0].data[1]==182);
  rx({0x30,0,5});drain();
  // More passes finish all 26 consecutive frames, including sequence wrap.
  drain();drain();assert(diag.sent.size()==27 && !sending && !flowWaiting);
  std::vector<uint8_t> reconstructed(diag.sent[0].data+2,diag.sent[0].data+8);
  uint8_t seq=1;
  for(size_t i=1;i<diag.sent.size();i++){
    auto &f=diag.sent[i];assert(f.data[0]==(0x20|seq));seq=(seq+1)&15;
    for(int j=1;j<f.can_dlc;j++)reconstructed.push_back(f.data[j]);
  }
  assert(reconstructed.size()==182 && reconstructed[0]==0x3B && reconstructed[1]==0xB1);
  for(size_t i=2;i<182;i++)assert(reconstructed[i]==0xA5);
  rx({2,0x7B,0xB1});drain();assert(Serial.output=="7BB1\n");
  fresh();host(write);rx({0x30,1,0x7F});tick();tick();
  assert(separationUs==127000);sent=diag.sent.size();tick(125000);
  assert(diag.sent.size()==sent);tick(3000);assert(diag.sent.size()==sent+1);
  tick();assert(flowWaiting && !sending);rx({0x30,0,0});drain();assert(!flowWaiting && !sending);
  fresh();host("21B1");rx({0x10,182,0x61,0xB1,0,0,0,0});tick();tick();
  rx({0x22,0,0,0,0,0,0,0});drain();assert(failed && diag.config);
  fresh();host("21B1");rx({0x11,1,0,0,0,0,0,0});drain();assert(failed);
  fresh();host(std::string(400,'A'));assert(Serial.output=="7F0000\n" && diag.sent.empty());
  fresh();host("3BB1GG");assert(diag.sent.empty());
  fresh();diag.complete=false;host("1081");tick(101000);assert(failed && diag.config);
  fresh();host("21B1");tick(10001000);assert(failed && diag.sent.size()==1);
  fresh();host("KK");tick(60001000);tick();tick();assert(!connected && !keepAlive);
  fresh();diag.flags=MCP2515::EFLG_RX0OVR;tick();assert(failed);
  diag.offline=true;host(">764:664");assert(failed);
  diag.offline=false;host(">764:664");assert(connected && !failed);
  std::cout<<"PASS: handshake, unlock, no security retry, B1 read/write, NRC78, sequence wrap, block size, STmin127ms, malformed/oversized input, overflow, TX/RX timeout, inactivity, reconnect\n";
}
