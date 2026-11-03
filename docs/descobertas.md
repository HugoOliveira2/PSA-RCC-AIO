# Descobertas e limites da evidência

Consolidei a pesquisa inicial em 30/10/2026 e atualizei a climatização em **03/11/2026**. Preservei as datas dos registros históricos para não misturar hipóteses antigas com os testes que fiz depois.

| Tema | Evidência disponível | Limite |
| --- | --- | --- |
| Protocolo AIO | KWP-PSA2000; requisição CAN 0x764 e resposta 0x664; sessão C0 | Não transferir automaticamente procedimentos UDS de NAC/RCC |
| Identificação histórica | FE termina em 931037; variante E3_V3 no mapa e na comparação com o Diagbox | Não escolher perfil apenas pelo modelo do veículo |
| B1 | 180 bytes; 292 campos não reservados no mapa convertido | Campo documentado não equivale a função validada no firmware |
| Escrita de configuração | 3B B1; confirmação 7B B1 e releitura 21 B1 | Resultado visual e persistência precisam de teste próprio |
| B0 | VIN em 17 bytes ASCII na leitura histórica | O aplicativo não implementa escrita de VIN |
| B2 | A leitura histórica recebeu 7F2112 | Isso não prova ausência da zona em todas as variantes |
| Temas | AMBIANCE_DEFAULT_000 é um campo de 3 bits; MENU_AMBIANCE é outro conjunto | Atribuição de nomes Red/Blue a códigos não foi validada |
| Cruzamento NAC | 258 correspondências por nome, 29 por posição e 5 sem correspondência | As descrições transferidas são hipóteses; não formam um mapa de escrita AIO |
| Calibração .cal | Registros S-record com endereços próprios | Endereço CAL não é ID de zona B1 nem substituto de backup |
| Comunicação | Diagbox observado alterna 1081/C0; a 0.6 não faz esse retorno explícito | Não está demonstrado que essa diferença explique sozinha a dependência do Diagbox |
| Firmware | Inventário e tentativas de descriptografia preservados | Produzir bytes não comprova descriptografia válida |

## Comunicação e regressões

Observei que a 0.6 funcionava após abrir o Diagbox e que versões posteriores ficaram impraticáveis. Não identifiquei apenas por esse comportamento qual operação preparou o estado da rede. Na [análise de 30/10](analise-comunicacao-2026-10-30.txt), registrei a sequência observada, diferenças de T20, manutenção da sessão e limites de leitura. As referências à 0.7–0.9 são comparações históricas; não substituí a 0.6.1 nesta publicação.

Faltam capturas controladas da inicialização a frio e após Diagbox, com timestamps. É preciso separar abrir sessão, desbloquear, ler, conferir, escrever e verificar. Não há um parâmetro persistente da BSI comprovado como solução.

## Retrofits relacionados

Em 30/10/2026, eu estimava a [montagem da fachada AIO no meu C4 Lounge](retrofits/fachada-rt6-aio.md) em cerca de 90% funcional. Depois montei o [módulo CAN de clima](retrofits/modulo-can-clima.md), recuperei a temperatura do passageiro, o AC MAX nos dois lados e o comando do desembaçador. Confirmei a versão híbrida 3.2 funcionando no carro em 03/11/2026. Publiquei fontes, `.hex`, circuito, transições CAN selecionadas e testes. Continuo sem confirmar a função do pino 14 e sem identificar o pino 7.

O [comparativo dos conectores traseiros RT6/AIO Wave 1](retrofits/conectores-rt6-aio.md) registra ca/cb/cc e a variante RT6 Hi-Fi. O vídeo 1 muda de RT6 cc/1 (+) e cc/7 (−) para AIO cc/10 (+) e cc/4 (−). Isso é uma correspondência da tabela, sem comprovação de funcionamento da câmera. As células AIO vazias permanecem como não informadas.

Na [investigação inicial do desembaçador](retrofits/desembacador.txt), combinei minhas medições na fachada nova, consulta de esquema e referências CAN. Depois confirmei o contato para GND no pino 11, o indicador positivo que também acende em 5 V no pino 13 e os campos de solicitação `0x350 data[0] bit 5` e indicação `0x227 data[1] bits 3..2`. Capturei os pulsos e ouvi a atuação do relé no carro. Não medi a corrente do aquecimento e não identifiquei um par de pinos BSI para ligação direta.

A [investigação da iluminação dos pés](retrofits/iluminacao-pes.txt) encontrou a saída BSI 60V BE pino 11 em desenhos R7 e DS5. Isso não comprova circuito, capacidade ou suporte na BSI real do Lounge. A função com dois níveis de brilho permanece não validada.

Esses relatórios mencionam esquemas e bases consultadas localmente. As bases proprietárias e os desenhos exportados não acompanham esta publicação; as referências de desenho foram mantidas para rastreabilidade.

## Próximas contribuições úteis

- Leituras por referência de hardware/software, com VIN anonimizado.
- Testes de um campo por vez, com antes/depois e efeito observado após desligamento.
- Captura de comunicação para isolar o preparo da sessão e as regressões.
- Validação de temas, câmera e climatização por variante.
- Capturas de outras variantes e ensaios prolongados do módulo de clima e alimentação.
- Novas evidências de firmware com verificação estrutural e integridade.
