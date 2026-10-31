# Publicação inicial

Destino: https://github.com/HugoOliveira2/PSA-RCC-AIO

Base escolhida pelo mantenedor: **AIO Assist 0.6.1**. Consolidação da pesquisa: 30/10/2026.

## Diferenças da cópia pública

- VIN observado substituído por `VF700000000000001` e seu ASCII hexadecimal nos fontes de teste e no exemplo JSON. O exemplo é anonimizado, não um backup para restaurar em outro carro.
- Executáveis recompilados a partir desses fontes. Não têm o mesmo hash dos executáveis históricos.
- Fontes C# e mapas necessários à compilação incluídos. Testes offline usam o exemplo anonimizado em `examples/`. Protocolo e lógica de escrita da 0.6.1 preservados.
- Conversor B1 adaptado para usar `maps/upstream/` e gerar `src/references/aio-b1.json` sem depender do workspace privado.
- Créditos, licenças, catálogo CSV, relatório das descobertas e instruções de contribuição adicionados.

## Organização dos materiais históricos

A análise de comunicação descreve inclusive versões posteriores para explicar regressões conhecidas. Sua inclusão não altera a versão principal publicada. O relatório antigo da edição B1 refere-se à 0.4 e foi guardado como histórico; para a 0.6.1 prevalecem as notas específicas de reenvio manual.

Os relatórios de retrofit mencionam arquivos de evidência existentes no workspace de pesquisa. Bases Diagbox/SEDRE/DocBackup, esquemas exportados e logs pessoais foram substituídos por conclusões e identificadores de referência. Calibrações CAL são indicadas por origem; não acompanham os binários.

O script histórico de regeneração do cruzamento dependia de um arquivo PSA-RE completo e caminhos privados. O JSON e o relatório do cruzamento foram preservados como snapshot; a regeneração integral dessa camada é uma melhoria pendente. A conversão B1 e a compilação do aplicativo usam apenas os arquivos documentados.

## Distribuição

O Git mantém fontes, mapas, documentação e o pacote em `downloads/AIO-Assist-0.6.1-publico.zip`. Esse pacote contém os executáveis recompilados junto com os fontes correspondentes, licenças e testes. O pacote local `PSA-RCC-AIO-publicacao.zip` contém o material da árvore Git para upload manual.

Os hashes SHA-256 de todos os arquivos versionados estão em `SHA256SUMS.txt`; o hash do próprio manifesto não é incluído nele.
