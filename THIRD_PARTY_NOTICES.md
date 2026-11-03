# Fontes e créditos

## Materiais incluídos

| Origem | Material nesta publicação | Licença / tratamento |
| --- | --- | --- |
| [prototux/PSA-RE](https://github.com/prototux/PSA-RE) | AIO `B1.yml`, `ecu.yml`, mapas CAN 217/227/350, conversão B1 e dados usados no cruzamento | Apache-2.0; texto original em `LICENSES/PSA-RE-Apache-2.0.txt` |
| [ludwig-v/psa-nac-firmware-reverse-engineering](https://github.com/ludwig-v/psa-nac-firmware-reverse-engineering) | `Configuration/nac.json`, mantido sem alteração; descrições NAC usadas na camada de comparação | GPL-3.0; texto em `LICENSES/GPL-3.0.txt` e `src/references/LICENSE-map.txt` |
| [autowp/arduino-mcp2515](https://github.com/autowp/arduino-mcp2515) | Driver 1.3.0 local em `firmware/*/src/mcp2515_local`; corrigi a verificação de modo one-shot na cópia do híbrido | MIT; preservei `LICENSE.md` em cada cópia |
| [ludwig-v/arduino-psa-diag](https://github.com/ludwig-v/arduino-psa-diag) e [psa-seedkey-algorithm](https://github.com/ludwig-v/psa-seedkey-algorithm) | Protocolo e transformação seed/key no híbrido; algoritmo original de Wouter Bokslag e Jason F. | GPL-3.0-or-later; créditos no fonte e `firmware/modulo_c4_hibrido/COPYING` |

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

Fotografei a montagem e a placa da fachada e publiquei meu levantamento dos pinos e o comparativo RT6/AIO em 30/10/2026. Reuni a imagem de numeração ca/cb/cc como referência, sem identificar sua origem documental; não reivindico sua autoria. Separei o que mapeei do que consegui validar em funcionamento.

Organização da investigação, cliente AIO Assist, consulta offline, conversão de mapas, traduções, comparação AIO/NAC, testes simulados e relatórios de limitações. A cópia pública foi organizada em 30/10/2026 e usa um VIN fictício nos exemplos e testes. Os arquivos YAML e o mapa NAC originais mantêm seus avisos.

Nos relatórios de Diagbox/SEDRE/DocBackup, descrevi minha pesquisa local. Não redistribuí bases, logs pessoais completos, desenhos proprietários, instaladores ou firmware da central. Os `.hex` em `firmware/bin/` são do módulo Arduino que montei, com fontes correspondentes incluídos; não são firmware Continental. Publiquei somente transições CAN selecionadas de clima e hashes dos logs de origem. Referências a arquivos locais nos relatórios históricos indicam evidências da pesquisa, não arquivos presentes no pacote.

Agradecimentos a Ludwig V / VLud, Bagou91, prototux, dragouf, pepelxl e colaboradores dos projetos. Nenhum crédito implica endosso desta ferramenta.
