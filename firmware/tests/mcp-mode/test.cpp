#include <iostream>
#include "../../modulo_c4_hibrido/src/mcp2515_local/mcp2515.cpp"
int main() {
  MCP2515 controller(10,1000000);
  if(controller.reset()!=MCP2515::ERROR_OK ||
     controller.setBitrate(CAN_500KBPS,MCP_8MHZ)!=MCP2515::ERROR_OK ||
     controller.setListenOnlyMode()!=MCP2515::ERROR_OK) return 2;
  const auto result=controller.setNormalOneShotMode();
  std::cout<<"one-shot result="<<result<<" CANSTAT="<<(int)SPI.regs[0x0E]
           <<" CANCTRL="<<(int)SPI.regs[0x0F]<<"\n";
  if(result!=MCP2515::ERROR_OK || SPI.regs[0x0E]!=0 || SPI.regs[0x0F]!=8) return 1;
  if(controller.setNormalMode()!=MCP2515::ERROR_OK || SPI.regs[0x0F]!=0) return 3;
  if(controller.setListenOnlyMode()!=MCP2515::ERROR_OK || SPI.regs[0x0E]!=0x60) return 4;
  if(controller.setConfigMode()!=MCP2515::ERROR_OK || SPI.regs[0x0E]!=0x80) return 5;
  SPI.acceptMode=false;
  if(controller.setNormalOneShotMode()==MCP2515::ERROR_OK) return 6;
  std::cout<<"PASS: real driver one-shot, normal, listen-only, config and mode timeout\n";
}
