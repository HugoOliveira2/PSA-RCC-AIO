#include <iostream>
#include <iomanip>
#include "../../modulo_c4_hibrido/diagnostico.cpp"
void climateStatus() {}
int main() {
  uint32_t pin,seed;
  while(std::cin >> std::hex >> pin >> seed)
    std::cout<<std::hex<<std::setfill('0')<<std::setw(8)<<seedKey((uint16_t)pin,seed)<<'\n';
}
