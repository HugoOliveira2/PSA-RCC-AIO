# Fontes e créditos

## Materiais incluídos

| Origem | Material nesta publicação | Licença / tratamento |
| --- | --- | --- |
| [prototux/PSA-RE](https://github.com/prototux/PSA-RE) | AIO `B1.yml`, `ecu.yml`, mapas CAN 217/227/350, conversão B1 e dados usados no cruzamento | Apache-2.0; texto original em `LICENSES/PSA-RE-Apache-2.0.txt` |
| [ludwig-v/psa-nac-firmware-reverse-engineering](https://github.com/ludwig-v/psa-nac-firmware-reverse-engineering) | `Configuration/nac.json`, mantido sem alteração; descrições NAC usadas na camada de comparação | GPL-3.0; texto em `LICENSES/GPL-3.0.txt` e `src/references/LICENSE-map.txt` |

`aio-b1.json` é uma conversão do YAML PSA-RE; `aio-pt.json` fornece traduções para a interface. `aio-nac-cross.json` combina referências PSA-RE/NAC para pesquisa. As descrições herdadas não são apresentadas como descobertas próprias. O SHA-256 das fontes está registrado nos JSON.

## Projetos e documentação consultados

- [ludwig-v/arduino-psa-diag](https://github.com/ludwig-v/arduino-psa-diag): sketch, protocolo serial, transporte CAN e acesso KWP. Para o uso desta publicação, consulte a versão 2.0. A cópia antiga de sketch encontrada no projeto dragouf não foi tratada como se fosse a 2.0 instalada.
- [ludwig-v/psa-seedkey-algorithm](https://github.com/ludwig-v/psa-seedkey-algorithm): algoritmo e documentação de acesso de segurança; atribuição preservada como referência do protocolo.
- [ludwig-v/arduino-psa-nac_rcc_cirocco](https://github.com/ludwig-v/arduino-psa-nac_rcc_cirocco): ferramentas e calibrações. O endereço histórico `arduino-psa-nac_rcc` redireciona para esse projeto. Calibrações consultadas: `Calibrations/NAC/C4L_AIO-9693762280.cal` e `Calibrations/RCC/C4L-9693725980.cal`. Os arquivos CAL não são redistribuídos neste pacote.
- [dragouf/PSA-Arduino-NAC-RCC](https://github.com/dragouf/PSA-Arduino-NAC-RCC): aplicação e fontes examinadas; seu README atribui a base a VLud e Bagou91. O repositório completo não é redistribuído aqui.
- [pepelxl/psa_coder](https://github.com/pepelxl/psa_coder): comparação dos mapas de configuração; seus executáveis não são redistribuídos aqui.
- [autowp/arduino-mcp2515](https://github.com/autowp/arduino-mcp2515): referência de biblioteca e ligação SPI citada no tutorial de preparação.
- [Tutorial de VLud](https://vlud.net/nac-telecodage.html): montagem e telecodificação.
- [Mittns Peugeot Forum](https://www.mittns.de/thread/2253-how-to-change-vin-in-continentals-aio-infotainment/?pageNo=2): pesquisa de escrita de VIN; créditos a j23 e danielvieira pelo procedimento e relato.

## Contribuição desta publicação

As fotos da montagem e da placa da fachada, o pinout das fachadas e o comparativo dos conectores RT6/AIO foram fornecidos pelo autor em 30/10/2026. A imagem de numeração ca/cb/cc foi fornecida como referência, sem indicação de sua origem documental; não se atribui sua autoria ao projeto. Os documentos registram essa limitação e distinguem mapeamento de validação funcional.

Organização da investigação, cliente AIO Assist, consulta offline, conversão de mapas, traduções, comparação AIO/NAC, testes simulados e relatórios de limitações. A cópia pública foi organizada em 30/10/2026 e usa um VIN fictício nos exemplos e testes. Os arquivos YAML e o mapa NAC originais mantêm seus avisos.

Os relatórios de Diagbox/SEDRE/DocBackup descrevem observações da pesquisa local. Bases, logs pessoais, desenhos proprietários, instaladores e firmware binário não são redistribuídos. Referências a arquivos locais nos relatórios históricos indicam evidências da investigação, não arquivos presentes no pacote.

Agradecimentos a Ludwig V / VLud, Bagou91, prototux, dragouf, pepelxl e colaboradores dos projetos. Nenhum crédito implica endosso desta ferramenta.
