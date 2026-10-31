# Mapa AIO B1

A fonte é [prototux/PSA-RE](https://github.com/prototux/PSA-RE), arquivos `diag/AEE2010.eco/BTEL/AIO/lids/B1.yml` e `ecu.yml`, preservados em `upstream/`. O mapa convertido está em [aio-b1.json](../src/references/aio-b1.json); a lista para planilha está em [parametros-b1.csv](parametros-b1.csv).

- Comprimento do payload: **180 bytes**, sem o prefixo de resposta `61B1`.
- Campos não reservados incorporados: **292**.
- Bytes do YAML começam em 1; bits são numerados de 7 a 0.
- A notação `13.7-13.5` indica três bits no byte 13, lidos na ordem declarada.
- O editor trabalha com valores brutos e preserva os bits vizinhos.
- Escala e offset declarados na fonte não autorizam tratar o número bruto como unidade física.
- Campos sobrepostos, tipos não suportados ou larguras acima de 32 bits não são editáveis no aplicativo.

## Exemplo de interpretação

`AMBIANCE_DEFAULT_000` usa os bits `13.7-13.5`, com códigos brutos de 0 a 7. No exemplo histórico, seu valor era 2. Preservando os outros bits, a alteração de 2 para 3 muda o byte 13 de `40` para `60`. Esse exemplo aritmético não atribui nomes visuais aos temas nem confirma o efeito em todas as centrais.

## Rastreabilidade

O JSON registra o SHA-256 da fonte YAML. A conversão preserva os 292 campos não reservados da interpretação principal e registra diferenças/adições das alternativas; os campos extras das alternativas não são incorporados. Para regenerar o mapa use `python tools/convert_aio_map.py`, com PyYAML instalado. O script histórico dependia de pastas externas; nesta publicação os caminhos apontam para as fontes incluídas.

O mapa NAC em `src/references/nac.json` contém 52 zonas e 351 parâmetros e não contém B1. O cruzamento `aio-nac-cross.json` é uma camada de pesquisa somente para consulta. Igualdade de nome ou deslocamento de bits não comprova equivalência funcional.

Os arquivos CAN 217/227/350 em `upstream/` mantêm os nomes e campos da fonte. Eles documentam quadros compartilhados; não são payloads prontos para transmissão.
