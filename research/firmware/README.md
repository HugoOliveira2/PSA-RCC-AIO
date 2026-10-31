# Investigação de firmware

O inventário local examina o pacote `67.02.24.22_RCC-r0`, declarado como `ovip-int-firmware-rcc-aio-latam` para RCC_AIO_LATAM_WAVE1/WAVE2 no `UpdateInfo.xml`. Metadados e hashes foram preservados; o firmware não acompanha o repositório.

`key-tests.json` e `datastore-key-tests.json` registram tentativas com referências de chaves públicas. Código de saída diferente de zero, falha de padding, estrutura gzip/tar inválida ou hash divergente não são evidência de extração bem-sucedida. Um arquivo intermediário de saída pode conter bytes mesmo quando a operação falha. Nenhum arquivo denominado `unverified` é apresentado como firmware descriptografado.

A investigação não produziu uma imagem modificada pronta para gravação. Os scripts e conceitos usados como referência pertencem ao [projeto de Ludwig V](https://github.com/ludwig-v/psa-nac-firmware-reverse-engineering). A existência de uma referência de chave para outra versão não comprova compatibilidade com este pacote.
