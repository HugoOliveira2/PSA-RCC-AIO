# PSA RCC AIO

Um ponto de partida em português para entender, estudar e customizar a central **Continental RCC AIO** usada em veículos PSA/Stellantis.

Este projeto reúne o **AIO Assist 0.6.1**, o mapa do bloco B1, ferramentas de consulta offline e as descobertas de uma investigação de adaptação da AIO ao C4 Lounge. A ideia é tornar o caminho reproduzível e permitir que outras pessoas contribuam com novos testes e correções.

**Estado: experimental.** A presença de um parâmetro no mapa não confirma seu funcionamento em todas as revisões. Identifique a central e salve sua própria leitura antes de qualquer alteração. Escritas exigem alimentação estável e podem deixar a configuração incorreta se interrompidas.

## Baixar e usar no Windows

**[Baixar AIO Assist 0.6.1 pronto para uso](https://github.com/HugoOliveira2/PSA-RCC-AIO/releases/download/v0.6.1/AIO-Assist-0.6.1-publico.zip)** · [Ver a Release](https://github.com/HugoOliveira2/PSA-RCC-AIO/releases/tag/v0.6.1)

1. Baixe o ZIP e extraia a pasta inteira.
2. Abra **`Abrir-AIO-Assist.cmd`**, ou execute `src/AIO-Assist-0.6.1.exe` diretamente.
3. Para explorar sem conectar ao veículo, abra `examples/leitura-anonimizada.json` na consulta offline.

O programa já está compilado. Requer Windows com **.NET Framework 4.x**; para comunicação com o veículo, requer também a interface Arduino compatível e o sketch **arduino-psa-diag 2.0**. Fontes, mapas e licenças acompanham o pacote. O executável também está disponível separadamente nos arquivos da Release.

## Comece por aqui

1. Leia o [guia inicial](docs/primeiros-passos.md) para entender hardware, identificação e consulta offline.
2. Consulte o [que sabemos e o que falta comprovar](docs/descobertas.md).
3. Explore o [mapa B1](maps/README.md) e a [planilha CSV dos 292 parâmetros](maps/parametros-b1.csv).
4. Veja como [compilar e verificar o AIO Assist](docs/compilar.md).
5. Para propor testes ou registrar um resultado, leia [como contribuir](CONTRIBUTING.md).

## O que está incluído

| Pasta | Conteúdo |
| --- | --- |
| `src/` | Fontes C# do AIO Assist 0.6.1 e script de compilação para Windows/.NET Framework 4.x |
| `src/references/` | Mapa AIO convertido, traduções, mapa NAC e cruzamento AIO/NAC incorporados ao executável |
| `maps/` | YAML de origem, catálogo CSV e convenções de bytes/bits |
| `docs/` | Guia inicial, descobertas, VIN, análise de comunicação e histórico da 0.6.1 |
| `docs/retrofits/` | Resultados da investigação do desembaçador e da iluminação dos pés |
| `examples/` | Leitura histórica com VIN fictício para consulta offline |
| `research/` | Comparações AIO/NAC e resultados das análises de firmware |
| `tools/` | Conversão reproduzível do mapa e verificação do pacote |
| `LICENSES/` | Licenças e atribuições dos materiais de terceiros |

Baixe o [AIO Assist 0.6.1 com fontes e documentação](downloads/AIO-Assist-0.6.1-publico.zip), extraia a pasta inteira e abra `src/AIO-Assist-0.6.1.exe`. Os executáveis compilados e seus fontes correspondentes são distribuídos juntos nesse pacote.

## Sobre a versão 0.6.1

Esta é a versão escolhida para a publicação inicial. Deriva da 0.6 e acrescenta **reenvio manual de uma gravação preparada quando a conferência falhou antes do envio**. O reenvio não faz uma nova leitura prévia: usa o bloco preparado de 180 bytes e salva a base anterior como backup. Leia os [detalhes e limites](docs/historico/LEIA-ME-0.6.1.txt) antes de usar essa função.

A cópia pública substitui o VIN histórico nos dados de exemplo e nos testes por um VIN fictício e recompila os executáveis. Essa alteração de privacidade não muda o protocolo de comunicação. O [registro da publicação](docs/publicacao.md) descreve as diferenças.

O AIO Assist edita B1. **Não grava VIN, calibração ou firmware.** A pesquisa de VIN está documentada separadamente.

## Projetos que tornaram este trabalho possível

- [prototux/PSA-RE](https://github.com/prototux/PSA-RE): origem do mapa AIO B1, variantes e documentação CAN.
- [ludwig-v/arduino-psa-diag](https://github.com/ludwig-v/arduino-psa-diag): interface Arduino e protocolo de comunicação.
- [ludwig-v/psa-nac-firmware-reverse-engineering](https://github.com/ludwig-v/psa-nac-firmware-reverse-engineering): mapa NAC e referência para a investigação de firmware.
- [ludwig-v/arduino-psa-nac_rcc_cirocco](https://github.com/ludwig-v/arduino-psa-nac_rcc_cirocco): ferramentas de telecodificação/calibração e calibrações consultadas.
- [ludwig-v/psa-seedkey-algorithm](https://github.com/ludwig-v/psa-seedkey-algorithm): documentação do acesso de segurança.
- [dragouf/PSA-Arduino-NAC-RCC](https://github.com/dragouf/PSA-Arduino-NAC-RCC): ferramenta e fontes examinadas durante a pesquisa.
- [pepelxl/psa_coder](https://github.com/pepelxl/psa_coder): referência de organização dos mapas e comparação de configuração.

Créditos a **Ludwig V / VLud, Bagou91, prototux, dragouf, pepelxl e demais colaboradores**. Veja a [origem de cada material](THIRD_PARTY_NOTICES.md). O projeto é independente e não é um produto oficial desses autores, da Continental ou da PSA/Stellantis.

## Licença

O código próprio e a documentação desta publicação são disponibilizados sob [GPL-3.0](LICENSE). Os materiais do PSA-RE mantêm sua licença Apache-2.0; o mapa NAC mantém GPL-3.0. Atribuições e textos originais estão preservados em [LICENSES](LICENSES/) e [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
