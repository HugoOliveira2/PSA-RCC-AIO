# Primeiros passos

## Identifique sua AIO

Registre modelo da central, referência, hardware/software, veículo e eventual adaptador CAN. A denominação RCC, sozinha, não identifica o protocolo. Na leitura histórica deste projeto, FE termina em `931037`, associado à variante `E3_V3`; o nome do carro não autoriza selecionar uma variante B7.

O aplicativo foi preparado para Windows com .NET Framework 4.x e o sketch **arduino-psa-diag 2.0**. A interface de pesquisa usa Arduino Uno e MCP2515. Confira cristal, CS, transceptor, pinagem e rede do seu carro nas [instruções do projeto Arduino](https://github.com/ludwig-v/arduino-psa-diag) e no [tutorial de VLud](https://vlud.net/nac-telecodage.html). A montagem histórica usou cristal de 8 MHz; isso não é uma configuração universal.

## Primeiro explore offline

Compile conforme [compilar.md](compilar.md) ou obtenha o pacote da Release. Abra `AIO-Assist-0.6.1.exe` e use as funções de consulta offline com `examples/leitura-anonimizada.json`. O segundo executável, `AIO-Consulta-Offline-0.6.1.exe`, é uma cópia do mesmo programa; seu nome não constitui bloqueio de escrita ou isolamento de hardware.

Você pode consultar os parâmetros, os valores brutos e o cruzamento NAC sem conectar a central. Editar o JSON em `src/references` não muda um executável já compilado: os mapas são incorporados na compilação.

## Leitura na central

Com o veículo parado e alimentação estável, feche os programas que disputam a interface, selecione a COM e inicie a sessão. Faça uma nova leitura de FE, VIN e B1. Exporte e conserve os dados originais localmente. Não publique o VIN em texto, hexadecimal, logs ou capturas de tela.

Abrir um JSON offline permite preparar valores, mas não habilita gravação. O editor verifica variantes conhecidas e B1 com 180 bytes. Esses filtros comprovam o formato esperado, não a compatibilidade funcional de cada campo.

## Alteração de B1

Comece com um único campo, anote o valor original e revise os bytes propostos. O aplicativo usa valores brutos. No fluxo normal, relê FE/VIN/B1, confere a base, salva o backup, envia `3BB1` com o bloco completo e verifica por releitura. O backup de B1 não é uma imagem de memória nem uma cópia de firmware.

O botão **REENVIAR GRAVAÇÃO** da 0.6.1 tem comportamento diferente: envia a proposta retida sem a conferência prévia. O backup dessa tentativa contém a base anterior. Leia [a descrição exata](historico/LEIA-ME-0.6.1.txt). Não repita uma escrita de resultado incerto.

Uma resposta `7BB1` e uma releitura idêntica confirmam os bytes naquele momento. Registre também o efeito real e a persistência após o desligamento normal. Não há nomes visuais de temas confirmados para todos os códigos do mapa.
