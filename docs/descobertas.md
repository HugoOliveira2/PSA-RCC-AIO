# Descobertas e limites da evidência

Consolidação em 30/10/2026. Os arquivos de origem preservam as respectivas datas e o contexto histórico.

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

O relato de campo informou que a 0.6 funcionava após abrir o Diagbox e que versões posteriores ficaram impraticáveis. O relato não identifica qual operação preparou o estado da rede. A [análise de 30/10](analise-comunicacao-2026-10-30.txt) registra a sequência observada, diferenças de T20, manutenção da sessão e limites de leitura. As referências à 0.7–0.9 são comparações históricas; essas versões não substituem a 0.6.1 nesta publicação.

Faltam capturas controladas da inicialização a frio e após Diagbox, com timestamps. É preciso separar abrir sessão, desbloquear, ler, conferir, escrever e verificar. Não há um parâmetro persistente da BSI comprovado como solução.

## Retrofits relacionados

A [montagem da fachada AIO no C4 Lounge](retrofits/fachada-rt6-aio.md) foi relatada pelo autor em 30/10/2026 como cerca de 90% funcional. Foram publicados fotos, funções dos pinos e correspondências RT6 → AIO. Permanecem pendentes o botão do desembaçador e o ajuste de temperatura do passageiro na central; o ajuste do motorista funciona. O pino 14 da fachada é apenas uma hipótese de iluminação e o pino 7 não foi informado.

O [comparativo dos conectores traseiros RT6/AIO Wave 1](retrofits/conectores-rt6-aio.md) registra ca/cb/cc e a variante RT6 Hi-Fi. O vídeo 1 muda de RT6 cc/1 (+) e cc/7 (−) para AIO cc/10 (+) e cc/4 (−). Isso é uma correspondência da tabela, sem comprovação de funcionamento da câmera. As células AIO vazias permanecem como não informadas.

A [investigação do desembaçador](retrofits/desembacador.txt) combina medições do usuário na fachada nova (pino 11: botão; pino 13: indicador), consulta de esquema e referências CAN. O caminho original do Lounge é CAN; não foi encontrado um par de pinos BSI validado para conexão direta. Os campos CAN 0x350 e 0x227 são pistas de solicitação e indicação, ainda sem validação por captura no Lounge.

A [investigação da iluminação dos pés](retrofits/iluminacao-pes.txt) encontrou a saída BSI 60V BE pino 11 em desenhos R7 e DS5. Isso não comprova circuito, capacidade ou suporte na BSI real do Lounge. A função com dois níveis de brilho permanece não validada.

Esses relatórios mencionam esquemas e bases consultadas localmente. As bases proprietárias e os desenhos exportados não acompanham esta publicação; as referências de desenho foram mantidas para rastreabilidade.

## Próximas contribuições úteis

- Leituras por referência de hardware/software, com VIN anonimizado.
- Testes de um campo por vez, com antes/depois e efeito observado após desligamento.
- Captura de comunicação para isolar o preparo da sessão e as regressões.
- Validação de temas, câmera e climatização por variante.
- Capturas passivas dos sinais de botão e indicador do desembaçador.
- Novas evidências de firmware com verificação estrutural e integridade.
