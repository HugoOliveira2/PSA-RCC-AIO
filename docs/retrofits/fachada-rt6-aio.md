# Fachada AIO no C4 Lounge: montagem e pinout

Mapeei esses conectores durante a adaptação da RCC AIO ao meu C4 Lounge, em **30/10/2026**, e atualizei o estado da montagem em **03/11/2026**. Na primeira etapa, eu estimava a adaptação em cerca de 90% funcional; era uma estimativa de uso, não um percentual obtido em ensaio de todas as funções.

## Estado atual da adaptação

Na primeira montagem, o botão do desembaçador da fachada e a temperatura do passageiro na AIO não funcionavam. Depois montei uma interface com Arduino Uno e MCP2515, mantendo a fachada Cactus, e recuperei o ajuste do passageiro, LO/HI, AC MAX nos dois lados e o comando do desembaçador.

Confirmei o botão, o indicador alimentado por 5 V e a recepção CAN na bancada; confirmei a atuação do relé do desembaçador no carro e o funcionamento da versão híbrida 3.2. No [registro do módulo CAN](modulo-can-clima.md), documentei o circuito final, os firmwares, a integração com AIO Assist e os limites de cada teste. Não atribuo essas funções somente a uma alteração de parâmetro B1.

## Fotos da tentativa

### Placa da fachada e orientação da numeração

![Placa da fachada AIO, com os pinos 1 e 15 que marquei](../../images/fachada-aio-placa-pinout.png)

Marquei **1 à esquerda e 15 à direita, na vista fotografada da placa e do conector**. Mantenha essa orientação ao comparar o mapeamento: a vista do chicote pelo lado oposto pode ficar espelhada. As inscrições visíveis incluem Valeo e `B109505-B`; registrei a minha peça, sem estender essa identificação a todas as fachadas AIO.

### Central na montagem de teste

![Central AIO no painel do C4 Lounge durante a tentativa de adaptação](../../images/aio-montagem-c4-lounge.jpg)

Fotografei essa etapa da montagem com um aviso na tela. Não uso a imagem para comprovar funcionamento da câmera, conclusão da instalação ou causa do aviso.

## Fachada original RT6: funções informadas

| Pino RT6 | Função que mapeei |
| --- | --- |
| 1 | Terra |
| 2 | Indicador de trava |
| 3 | Sem descrição |
| 4 | Comando liga/desliga do rádio — TOR BSI ON/OFF |
| 5 | Sem descrição |
| 6 | Botão de travamento centralizado |
| 7 | CAN H — rede Info Div |
| 8 | CAN L — rede Info Div |
| 9 | Botão do pisca-alerta |
| 10 | Indicador do pisca-alerta |
| 11 | Iluminação do painel |
| 12 | Alimentação +CAN, pelo fusível F21 |

“+CAN” reproduz a designação fornecida para a alimentação; não representa uma terceira linha de dados CAN. O par de dados é indicado separadamente como CAN H/L. A tabela não informa capacidade de corrente ou condição de chaveamento dessa alimentação.

## Fachada AIO: funções informadas

| Pino AIO | Função que mapeei | Observação |
| --- | --- | --- |
| 1 | Não utilizado | Classificação informada para esta montagem |
| 2 | Terra | Correspondência RT6 1 |
| 3 | Comando liga/desliga do rádio — TOR BSI ON/OFF | Correspondência RT6 4 |
| 4 | Alimentação +12 V | Correspondência RT6 12 |
| 5 | CAN H — rede Info Div | Correspondência RT6 7 |
| 6 | CAN L — rede Info Div | Correspondência RT6 8 |
| 7 | Não informado | O pino não aparece no mapeamento fornecido |
| 8 | Botão do pisca-alerta | Correspondência RT6 9 |
| 9 | Indicador de trava | Correspondência RT6 2 |
| 10 | Indicador do pisca-alerta | Correspondência RT6 10 |
| 11 | Botão do desembaçador | Confirmei fechamento para GND; uso entrada D2 do módulo |
| 12 | Botão de travamento centralizado | Correspondência RT6 6 |
| 13 | Indicador do desembaçador traseiro | Confirmei acendimento com positivo de 5 V; aciono por PNP |
| 14 | Possível iluminação | **Minha hipótese, ainda não confirmada** |
| 15 | Não utilizado | Classificação informada para esta montagem |

## Correspondências RT6 → AIO que levantei

Esta tabela se refere **aos conectores das fachadas**. O [comparativo dos conectores traseiros das centrais](conectores-rt6-aio.md) é outro mapeamento.

| Pino da fachada RT6 | Pino da fachada AIO | Função |
| --- | --- | --- |
| 1 | 2 | Terra |
| 2 | 9 | Indicador de trava |
| 4 | 3 | Liga/desliga do rádio — TOR BSI ON/OFF |
| 6 | 12 | Botão de travamento centralizado |
| 7 | 5 | CAN H — Info Div |
| 8 | 6 | CAN L — Info Div |
| 9 | 8 | Botão do pisca-alerta |
| 10 | 10 | Indicador do pisca-alerta |
| 12 | 4 | Alimentação |

Não identifiquei correspondências para RT6 3, 5 e 11. Na adaptação inicial, ainda não tinha ligado AIO 11, 13 ou 14. Depois liguei 11 e 13 ao módulo CAN, conforme o [circuito final](modulo-can-clima.md). Não liguei RT6 11 a AIO 14 com base apenas na hipótese de iluminação.

## Pendências e próximas evidências

**Desembaçador:** identifiquei contato para GND no botão 11 e acendimento por positivo de 5 V no indicador 13. Resolvi a adaptação pelo módulo CAN; não encontrei pinos BSI validados para ligação direta. A [investigação anterior](desembacador.txt) registra a etapa de 30/10, antes das capturas e da montagem do módulo. Não uso o circuito de potência do vidro como entrada de botão ou saída de indicador.

**Temperatura do passageiro:** identifiquei os pulsos da AIO em `0x364` e corrigi a consigna em `0x350`, preservando os demais campos da Cactus. Confirmei o efeito nas saídas de ar. A solução injeta uma cópia corrigida na rede; não bloqueia o quadro original nem comprova compatibilidade com todas as variantes.

**Iluminação:** conferir o pino AIO 14 antes de atribuir função ou alimentação. O pino AIO 7 continua sem informação.

Registrei aqui minhas fotos, meu mapeamento e meus testes. As tabelas descrevem a unidade que usei; não são uma pinagem universal de todas as fachadas RT6/AIO.
